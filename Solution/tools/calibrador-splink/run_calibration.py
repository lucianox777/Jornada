#!/usr/bin/env python3
"""Runner de calibração Splink para a Jornada.

Consome JORNADA_SPLINK_EXCHANGE_V1 e produz estimativas m/u versionadas.
Este utilitário é exclusivo de desenvolvimento/calibração: não participa do runtime
operacional da Jornada.
"""

from __future__ import annotations

import argparse
import importlib.metadata
import json
from pathlib import Path
from typing import Any

import pandas as pd
import splink.comparison_library as cl
from splink import DuckDBAPI, Linker, SettingsCreator, block_on

EXPECTED_SCHEMA = "JORNADA_SPLINK_EXCHANGE_V1"
RUNNER_SCHEMA = "JORNADA_SPLINK_ESTIMATES_V1"


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Estima m/u com Splink para o Calibrador Jornada")
    parser.add_argument("--input", required=True, type=Path, help="Pacote JSON exportado pela Jornada")
    parser.add_argument("--output", required=True, type=Path, help="JSON de estimativas")
    parser.add_argument("--seed", required=True, type=int, help="Seed explícita para estimação de u")
    parser.add_argument("--max-pairs", type=int, default=10_000_000, help="Máximo de pares aleatórios para u")
    parser.add_argument(
        "--name-thresholds",
        type=float,
        nargs="+",
        default=[0.95, 0.90],
        help="Thresholds Jaro-Winkler para prenome e sobrenome",
    )
    return parser.parse_args()


def _settings(thresholds: list[float]) -> SettingsCreator:
    if not thresholds:
        raise ValueError("Ao menos um threshold nominal é obrigatório")
    if any(value <= 0.0 or value >= 1.0 for value in thresholds):
        raise ValueError("Thresholds nominais devem estar em (0,1)")

    ordered = sorted(set(thresholds), reverse=True)
    return SettingsCreator(
        link_type="dedupe_only",
        unique_id_column_name="unique_id",
        comparisons=[
            cl.JaroWinklerAtThresholds("first_name", score_threshold_or_thresholds=ordered),
            cl.JaroWinklerAtThresholds("surname", score_threshold_or_thresholds=ordered),
        ],
        blocking_rules_to_generate_predictions=[
            block_on("first_name"),
            block_on("surname"),
        ],
        retain_intermediate_calculation_columns=True,
    )


def _load_package(path: Path) -> dict[str, Any]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schema_version") != EXPECTED_SCHEMA:
        raise ValueError(
            f"schema_version incompatível: esperado {EXPECTED_SCHEMA}, recebido {payload.get('schema_version')!r}"
        )
    records = payload.get("records")
    labels = payload.get("labels")
    if not isinstance(records, list) or not records:
        raise ValueError("Pacote sem records")
    if not isinstance(labels, list) or not labels:
        raise ValueError("Pacote sem labels")
    return payload


def _canonical_u_records(records: list[dict[str, Any]]) -> pd.DataFrame:
    """Uma observação canônica por indivíduo-base para preservar a hipótese de u.

    O gerador Jornada sempre mantém a observação canônica no lado esquerdo dos pares
    que origina para cada indivíduo. Preferimos IDs ':L' e usamos ordenação apenas
    como desempate determinístico.
    """
    frame = pd.DataFrame.from_records(records)
    required = {"unique_id", "base_person_id", "first_name", "surname"}
    missing = required.difference(frame.columns)
    if missing:
        raise ValueError(f"records sem colunas obrigatórias: {sorted(missing)}")

    frame = frame.copy()
    frame["_canonical_rank"] = (~frame["unique_id"].astype(str).str.endswith(":L")).astype(int)
    frame = frame.sort_values(["base_person_id", "_canonical_rank", "unique_id"], kind="stable")
    frame = frame.drop_duplicates(subset=["base_person_id"], keep="first")
    return frame.drop(columns=["_canonical_rank"]).reset_index(drop=True)


def _positive_labels(labels: list[dict[str, Any]]) -> pd.DataFrame:
    frame = pd.DataFrame.from_records(labels)
    required = {
        "source_dataset_l",
        "unique_id_l",
        "source_dataset_r",
        "unique_id_r",
        "clerical_match_score",
    }
    missing = required.difference(frame.columns)
    if missing:
        raise ValueError(f"labels sem colunas obrigatórias: {sorted(missing)}")

    positives = frame.loc[frame["clerical_match_score"] == 1].copy()
    if positives.empty:
        raise ValueError("Não há labels positivos para estimar m")
    return positives.drop(columns=["clerical_match_score"])


def _extract_estimates(model: dict[str, Any]) -> list[dict[str, Any]]:
    estimates: list[dict[str, Any]] = []
    for comparison in model.get("comparisons", []):
        feature = comparison.get("output_column_name")
        if not feature:
            raise ValueError("Comparação Splink sem output_column_name")
        for level in comparison.get("comparison_levels", []):
            if level.get("is_null_level"):
                continue
            estimates.append(
                {
                    "feature": feature,
                    "level": level.get("label_for_charts") or level.get("sql_condition") or "unknown",
                    "m_probability": level.get("m_probability"),
                    "u_probability": level.get("u_probability"),
                    "sql_condition": level.get("sql_condition"),
                }
            )
    if not estimates:
        raise ValueError("Modelo Splink não produziu níveis de comparação")
    return estimates


def _estimate_u(records: pd.DataFrame, thresholds: list[float], max_pairs: int, seed: int) -> dict[str, Any]:
    if len(records) < 2:
        raise ValueError("São necessários ao menos dois indivíduos-base para estimar u")
    linker = Linker(records, _settings(thresholds), db_api=DuckDBAPI())
    linker.training.estimate_u_using_random_sampling(max_pairs=max_pairs, seed=seed)
    return linker.misc.save_model_to_json()


def _estimate_m(
    records: list[dict[str, Any]],
    labels: pd.DataFrame,
    thresholds: list[float],
) -> dict[str, Any]:
    all_records = pd.DataFrame.from_records(records)
    linker = Linker(all_records, _settings(thresholds), db_api=DuckDBAPI())
    labels_table = linker.table_management.register_labels_table(labels, overwrite=True)
    linker.training.estimate_m_from_pairwise_labels(labels_table)
    return linker.misc.save_model_to_json()


def _merge_m_u(m_model: dict[str, Any], u_model: dict[str, Any]) -> list[dict[str, Any]]:
    m_levels = _extract_estimates(m_model)
    u_levels = _extract_estimates(u_model)

    def key(item: dict[str, Any]) -> tuple[str, str]:
        return str(item["feature"]), str(item["sql_condition"])

    m_by_key = {key(item): item for item in m_levels}
    u_by_key = {key(item): item for item in u_levels}
    if set(m_by_key) != set(u_by_key):
        missing_m = sorted(set(u_by_key).difference(m_by_key))
        missing_u = sorted(set(m_by_key).difference(u_by_key))
        raise ValueError(f"Níveis m/u divergentes. sem_m={missing_m}; sem_u={missing_u}")

    merged: list[dict[str, Any]] = []
    for level_key in sorted(m_by_key):
        m = m_by_key[level_key]
        u = u_by_key[level_key]
        if m.get("m_probability") is None:
            raise ValueError(f"m não estimado para {level_key}")
        if u.get("u_probability") is None:
            raise ValueError(f"u não estimado para {level_key}")
        merged.append(
            {
                "feature": m["feature"],
                "level": m["level"],
                "sql_condition": m["sql_condition"],
                "m_probability": m["m_probability"],
                "u_probability": u["u_probability"],
            }
        )
    return merged


def run(args: argparse.Namespace) -> dict[str, Any]:
    if args.max_pairs <= 0:
        raise ValueError("max_pairs deve ser positivo")

    package = _load_package(args.input)
    records = package["records"]
    labels = package["labels"]

    canonical_u = _canonical_u_records(records)
    positive_m = _positive_labels(labels)
    thresholds = sorted(set(args.name_thresholds), reverse=True)

    u_model = _estimate_u(canonical_u, thresholds, args.max_pairs, args.seed)
    m_model = _estimate_m(records, positive_m, thresholds)
    estimates = _merge_m_u(m_model, u_model)

    return {
        "schema_version": RUNNER_SCHEMA,
        "source_schema_version": package["schema_version"],
        "splink_version": importlib.metadata.version("splink"),
        "runner": "calibrador-splink/run_calibration.py",
        "generator_version": package.get("generator_version"),
        "ibge_source_version": package.get("ibge_source_version"),
        "ibge_fingerprint_sha256": package.get("ibge_fingerprint_sha256"),
        "partition": package.get("partition"),
        "seed": args.seed,
        "max_pairs": args.max_pairs,
        "name_thresholds": thresholds,
        "u_population_records": len(canonical_u),
        "m_positive_pairs": len(positive_m),
        "estimates": estimates,
    }


def main() -> None:
    args = _parse_args()
    result = run(args)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Runner nominal de calibração Splink para a Jornada.

Consome JORNADA_SPLINK_EXCHANGE_V1 e produz estimativas m/u para a evidência NOME
com a mesma semântica do scorer Jornada: EXACT, HIGH, MEDIUM e LOW.
Este utilitário é exclusivo de desenvolvimento/calibração e não participa do runtime
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
NOMINAL_SEMANTICS_VERSION = "IDENTITY_NAME_STATES_V1"
NAME_THRESHOLDS = [0.92, 0.80]
STATE_ORDER = ["EXACT", "HIGH", "MEDIUM", "LOW"]


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Estima m/u nominal com Splink para o Calibrador Jornada")
    parser.add_argument("--input", required=True, type=Path, help="Pacote JSON exportado pela Jornada")
    parser.add_argument("--output", required=True, type=Path, help="JSON de estimativas")
    parser.add_argument("--seed", required=True, type=int, help="Seed explícita para estimação de u")
    parser.add_argument("--max-pairs", type=int, default=10_000_000, help="Máximo de pares aleatórios para u")
    return parser.parse_args()


def _settings() -> SettingsCreator:
    return SettingsCreator(
        link_type="dedupe_only",
        unique_id_column_name="unique_id",
        comparisons=[
            cl.JaroWinklerAtThresholds("nome", score_threshold_or_thresholds=NAME_THRESHOLDS),
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


def _records_frame(records: list[dict[str, Any]]) -> pd.DataFrame:
    frame = pd.DataFrame.from_records(records)
    required = {"unique_id", "base_person_id", "first_name", "surname"}
    missing = required.difference(frame.columns)
    if missing:
        raise ValueError(f"records sem colunas obrigatórias: {sorted(missing)}")

    frame = frame.copy()
    frame["nome"] = (
        frame["first_name"].fillna("").astype(str).str.strip()
        + " "
        + frame["surname"].fillna("").astype(str).str.strip()
    ).str.strip()
    if (frame["nome"] == "").any():
        raise ValueError("Não é possível materializar NOME para todos os records")
    return frame


def _canonical_u_records(records: list[dict[str, Any]]) -> pd.DataFrame:
    """Uma observação canônica por indivíduo-base para preservar a hipótese de u."""
    frame = _records_frame(records)
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


def _extract_name_estimates(model: dict[str, Any]) -> list[dict[str, Any]]:
    comparisons = model.get("comparisons", [])
    comparison = next((item for item in comparisons if item.get("output_column_name") == "nome"), None)
    if comparison is None:
        raise ValueError("Modelo Splink sem comparação NOME")

    levels = [level for level in comparison.get("comparison_levels", []) if not level.get("is_null_level")]
    if len(levels) != len(STATE_ORDER):
        raise ValueError(f"Esperados {len(STATE_ORDER)} níveis NOME não-nulos; recebidos {len(levels)}")

    estimates: list[dict[str, Any]] = []
    for state, level in zip(STATE_ORDER, levels, strict=True):
        estimates.append(
            {
                "feature": "NOME",
                "level": state,
                "m_probability": level.get("m_probability"),
                "u_probability": level.get("u_probability"),
                "sql_condition": level.get("sql_condition"),
            }
        )
    return estimates


def _estimate_u(records: pd.DataFrame, max_pairs: int, seed: int) -> dict[str, Any]:
    if len(records) < 2:
        raise ValueError("São necessários ao menos dois indivíduos-base para estimar u")
    linker = Linker(records, _settings(), db_api=DuckDBAPI())
    linker.training.estimate_u_using_random_sampling(max_pairs=max_pairs, seed=seed)
    return linker.misc.save_model_to_json()


def _estimate_m(records: list[dict[str, Any]], labels: pd.DataFrame) -> dict[str, Any]:
    all_records = _records_frame(records)
    linker = Linker(all_records, _settings(), db_api=DuckDBAPI())
    labels_table = linker.table_management.register_labels_table(labels, overwrite=True)
    linker.training.estimate_m_from_pairwise_labels(labels_table)
    return linker.misc.save_model_to_json()


def _merge_m_u(m_model: dict[str, Any], u_model: dict[str, Any]) -> list[dict[str, Any]]:
    m_levels = _extract_name_estimates(m_model)
    u_levels = _extract_name_estimates(u_model)
    merged: list[dict[str, Any]] = []

    for m, u in zip(m_levels, u_levels, strict=True):
        if m["level"] != u["level"]:
            raise ValueError(f"Níveis m/u divergentes: {m['level']} vs {u['level']}")
        if m.get("m_probability") is None:
            raise ValueError(f"m não estimado para NOME/{m['level']}")
        if u.get("u_probability") is None:
            raise ValueError(f"u não estimado para NOME/{u['level']}")
        merged.append(
            {
                "feature": "NOME",
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

    u_model = _estimate_u(canonical_u, args.max_pairs, args.seed)
    m_model = _estimate_m(records, positive_m)
    estimates = _merge_m_u(m_model, u_model)

    return {
        "schema_version": RUNNER_SCHEMA,
        "source_schema_version": package["schema_version"],
        "splink_version": importlib.metadata.version("splink"),
        "runner": "calibrador-splink/run_calibration.py",
        "scope": "NOME",
        "nominal_semantics_version": NOMINAL_SEMANTICS_VERSION,
        "generator_version": package.get("generator_version"),
        "ibge_source_version": package.get("ibge_source_version"),
        "ibge_fingerprint_sha256": package.get("ibge_fingerprint_sha256"),
        "partition": package.get("partition"),
        "seed": args.seed,
        "max_pairs": args.max_pairs,
        "name_thresholds": NAME_THRESHOLDS,
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

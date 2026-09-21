#!/usr/bin/env python3
"""Gate de equivalência estatística entre gen_corpus_v2.py e o porte C#.

Não exige identidade linha a linha: os PRNGs são diferentes por desenho.
Exige equivalência das regras observáveis, distribuições e gabarito empírico.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import json
import shutil
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest().upper()


def make_fixture(root: Path) -> Path:
    projection = root / "projection"
    projection.mkdir(parents=True, exist_ok=True)
    rows = [
        {"tipo":"NOME","valor":"ANA","frequencia":1200},
        {"tipo":"NOME","valor":"MARIA","frequencia":1000},
        {"tipo":"NOME","valor":"JOAO","frequencia":800},
        {"tipo":"NOME","valor":"CARLOS","frequencia":600},
        {"tipo":"NOME","valor":"BEATRIZ","frequencia":350},
        {"tipo":"NOME","valor":"RAFAEL","frequencia":180},
        {"tipo":"NOME","valor":"LIA","frequencia":80},
        {"tipo":"NOME","valor":"IVO","frequencia":40},
        {"tipo":"SOBRENOME","valor":"SILVA","frequencia":1600},
        {"tipo":"SOBRENOME","valor":"SOUZA","frequencia":1200},
        {"tipo":"SOBRENOME","valor":"SANTOS","frequencia":900},
        {"tipo":"SOBRENOME","valor":"LIMA","frequencia":650},
        {"tipo":"SOBRENOME","valor":"COSTA","frequencia":400},
        {"tipo":"SOBRENOME","valor":"NUNES","frequencia":220},
        {"tipo":"SOBRENOME","valor":"REIS","frequencia":100},
        {"tipo":"SOBRENOME","valor":"LUZ","frequencia":40},
    ]
    canonical_lines = [
        json.dumps(
            {
                **row,
                "sexo":"TODOS",
                "periodoNascimento":"TODOS",
                "escopoGeografico":"BRASIL",
                "ufCodigo":"00",
                "municipioCodigo":"0000000",
            },
            separators=(",", ":"),
            ensure_ascii=False,
        )
        for row in rows
    ]
    canonical = ("\n".join(canonical_lines) + "\n").encode("utf-8")
    gz = projection / "frequencia-brasil.ndjson.gz"
    with gz.open("wb") as raw:
        with gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as zipped:
            zipped.write(canonical)

    manifest = {
        "schemaVersion": 1,
        "referenceCode": "SYNTHETIC_EQUIVALENCE_FIXTURE_V1",
        "format": "NDJSON_UTF8_GZIP",
        "generatedFrom": "synthetic-corpus-equivalence-gate.py",
        "files": [
            {
                "path": "projection/frequencia-brasil.ndjson.gz",
                "kind": "BRASIL_TOTAL",
                "required": True,
                "sha256": sha256(gz),
                "canonicalContentSha256": hashlib.sha256(canonical).hexdigest().upper(),
                "rowCount": len(rows),
            }
        ],
    }
    (root / "projection-manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )
    return gz


def run(command: list[str], cwd: Path) -> None:
    completed = subprocess.run(
        command, cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT
    )
    if completed.returncode != 0:
        raise SystemExit(
            "SYNTHETIC CORPUS EQUIVALENCE GATE: FAIL: comando falhou\n"
            + " ".join(command)
            + "\n"
            + completed.stdout
        )


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as fh:
        return list(csv.DictReader(fh))


def ratio(counter: Counter, key: str, denominator: int) -> float:
    return counter.get(key, 0) / denominator if denominator else 0.0


def corpus_metrics(directory: Path) -> tuple[dict[str, float], dict]:
    people = read_csv(directory / "pessoas_verdade.csv")
    observations = read_csv(directory / "observacoes.csv")
    truth = json.loads((directory / "gabarito.json").read_text(encoding="utf-8"))

    people_by_id = {row["base_person_id"]: row for row in people}
    partitions = Counter(row["particao"] for row in people)
    scenarios = Counter(row["cns_scenario"] for row in people)
    corruption_labels = Counter()
    for row in observations:
        for label in filter(None, row["corrupcoes"].split("|")):
            corruption_labels[label.split("_", 1)[0]] += 1

    cpf_den = 0
    cns_den = 0
    cpf_num = 0
    cns_num = 0
    for row in observations:
        person = people_by_id[row["base_person_id"]]
        if person["cpf"]:
            cpf_den += 1
            if row["cpf"]:
                cpf_num += 1
        if person["cns"]:
            cns_den += 1
            if row["cns"]:
                cns_num += 1

    n_people = len(people)
    n_obs = len(observations)
    metrics = {
        "partition_train": ratio(partitions, "TRAIN", n_people),
        "partition_validation": ratio(partitions, "VALIDATION", n_people),
        "partition_test": ratio(partitions, "TEST", n_people),
        "sex_m": sum(row["sexo"] == "M" for row in people) / n_people,
        "cpf_prevalence": sum(bool(row["cpf"]) for row in people) / n_people,
        "cns_prevalence": sum(bool(row["cns"]) for row in people) / n_people,
        "observations_per_person": n_obs / n_people,
        "cpf_retention": cpf_num / cpf_den if cpf_den else 0.0,
        "cns_retention": cns_num / cns_den if cns_den else 0.0,
        "missing_mother": sum(not row["nome_mae"] for row in observations) / n_obs,
        "missing_date": sum(not row["data_nascimento"] for row in observations) / n_obs,
        "name_corruption_labels": corruption_labels.get("NOME", 0) / n_obs,
        "date_corruption_labels": sum(
            count for label, count in corruption_labels.items() if label == "DATE"
        ) / n_obs,
        "scenario_invalid": ratio(scenarios, "INVALID_CHECK_DIGIT", n_people),
        "scenario_reused": ratio(scenarios, "REUSED", n_people),
        "scenario_dob_conflict": ratio(scenarios, "DOB_CONFLICT_REUSE", n_people),
        "evaluation_weight_mean": sum(float(row["evaluation_weight"]) for row in people) / n_people,
    }
    for field in ("NOME", "NOME_MAE", "NASCIMENTO"):
        value = truth["empirical_m_exact"][field]["m_exact_empirical"]
        metrics[f"m_{field.lower()}"] = float(value) if value is not None else 0.0
        weighted = truth["empirical_m_exact"][field]["m_exact_empirical_reweighted"]
        metrics[f"m_{field.lower()}_weighted"] = float(weighted) if weighted is not None else 0.0

    return metrics, truth


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--work", default=".local/synthetic-corpus-equivalence")
    parser.add_argument("--people", type=int, default=4000)
    parser.add_argument("--seed", type=int, default=424242)
    parser.add_argument("--tolerance", type=float, default=0.06)
    parser.add_argument("--summary", default=".local/synthetic-corpus-equivalence.json")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    work = (root / args.work).resolve()
    if work.exists():
        shutil.rmtree(work)
    reference = work / "reference"
    python_out = work / "python"
    csharp_out = work / "csharp"
    python_out.mkdir(parents=True)
    csharp_out.mkdir(parents=True)
    source = make_fixture(reference)

    common = [
        "--people", str(args.people),
        "--seed", str(args.seed),
        "--error-profile", "correlated",
        "--min-freq", "20",
        "--tail-oversample", "2.0",
        "--cpf-base-prevalence", ".22",
        "--cns-base-prevalence", ".55",
        "--cpf-observation-retention", ".55",
        "--cns-observation-retention", ".70",
        "--cns-invalid-rate", ".02",
        "--cns-reuse-rate", ".02",
        "--cns-dob-conflict-rate", ".02",
        "--gestores", "4",
    ]

    run(
        [
            sys.executable,
            "tools/calibrador/gen_corpus_v2.py",
            "--ibge-source", str(source),
            "--out", str(python_out),
            *common,
        ],
        root,
    )

    dll = root / "src/Jornada.Linkage.SyntheticCorpus/bin/Release/net8.0/Jornada.Linkage.SyntheticCorpus.dll"
    if not dll.exists():
        raise SystemExit(f"SYNTHETIC CORPUS EQUIVALENCE GATE: FAIL: DLL ausente: {dll}")
    run(
        [
            "dotnet", str(dll), "generate",
            "--reference-root", str(reference),
            "--out", str(csharp_out),
            *common,
        ],
        root,
    )

    python_metrics, python_truth = corpus_metrics(python_out)
    csharp_metrics, csharp_truth = corpus_metrics(csharp_out)

    if python_truth["declared_corruption_rates"] != csharp_truth["declared_corruption_rates"]:
        raise SystemExit("SYNTHETIC CORPUS EQUIVALENCE GATE: FAIL: perfis declarados divergiram")
    if python_truth["identifier_model"] != csharp_truth["identifier_model"]:
        raise SystemExit("SYNTHETIC CORPUS EQUIVALENCE GATE: FAIL: modelo de identificadores divergiu")

    failures = []
    deltas = {}
    for key in sorted(python_metrics):
        delta = abs(python_metrics[key] - csharp_metrics[key])
        deltas[key] = delta
        tolerance = .10 if key == "observations_per_person" else args.tolerance
        if delta > tolerance:
            failures.append(
                f"{key}: python={python_metrics[key]:.6f}; csharp={csharp_metrics[key]:.6f}; "
                f"delta={delta:.6f}; limite={tolerance:.6f}"
            )

    summary = {
        "status": "FAIL" if failures else "PASS",
        "people": args.people,
        "seed": args.seed,
        "tolerance": args.tolerance,
        "python": python_metrics,
        "csharp": csharp_metrics,
        "absoluteDelta": deltas,
        "failures": failures,
    }
    summary_path = (root / args.summary).resolve()
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    if failures:
        raise SystemExit(
            "SYNTHETIC CORPUS EQUIVALENCE GATE: FAIL:\n- " + "\n- ".join(failures)
        )

    print(
        "SYNTHETIC CORPUS EQUIVALENCE GATE: OK "
        f"(people={args.people}; metrics={len(python_metrics)}; max_delta={max(deltas.values()):.6f})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

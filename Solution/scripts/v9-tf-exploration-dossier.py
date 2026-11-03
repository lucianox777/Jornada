#!/usr/bin/env python3
"""V9-TF-001: small, read-only, aggregate dossier from physical study.json.

Never certifies, freezes, publishes, selects an operational model or reads TEST.
Only allowlisted aggregate fields enter the Markdown and manifest outputs.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

VERSION = "V9_TF_EXPLORATORY_DOSSIER_V1"
EXPECTED_STUDY = "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1"
SAFE_OPTION = re.compile(r"[A-Za-z0-9_.-]{1,110}\Z")


def digest(data):
    canonical = json.dumps(data, ensure_ascii=False, sort_keys=True,
                           separators=(",", ":"), allow_nan=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def file_digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()


def strict_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON property: " + key)
            result[key] = value
        return result

    def invalid(value):
        raise ValueError("Non-finite JSON literal: " + value)

    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream, object_pairs_hook=pairs, parse_constant=invalid)


def safe_option(option):
    if not isinstance(option, str):
        raise ValueError("Missing option identifier")
    name = option.split(":")[-1]
    if not SAFE_OPTION.fullmatch(name):
        raise ValueError("Unsafe option identifier")
    return name


def percentage(value):
    return f"{100 * value:.4f}%"


def summarize(study, study_sha, code_sha, run_id):
    if not isinstance(study, dict) or study.get("Version") != EXPECTED_STUDY:
        raise ValueError("Unexpected V9 physical study version")
    if not isinstance(code_sha, str) or not re.fullmatch(r"[a-fA-F0-9]{40}", code_sha):
        raise ValueError("A real 40-digit code SHA is required")
    if not isinstance(run_id, int) or run_id <= 0:
        raise ValueError("Valid workflow run ID required")

    selection = study.get("SourceSelection")
    if not isinstance(selection, dict) or not isinstance(selection.get("SourceReport"), dict):
        raise ValueError("SourceSelection/SourceReport missing")
    if selection.get("TestUsed") is not False or selection["SourceReport"].get("TestUsed") is not False:
        raise ValueError("TEST must not be used to select or summarize V9 TF bootstrap")
    if selection["SourceReport"].get("OperationalModelCreated") is not False:
        raise ValueError("Operational model must not be created by this exploration")
    if selection["SourceReport"].get("AutomaticActivationAllowed") is not False:
        raise ValueError("Automatic activation forbidden")

    rows = study.get("SourceMeasurements")
    frozen = study.get("FrozenConfigurations")
    if not isinstance(rows, list) or not rows or not isinstance(frozen, list):
        raise ValueError("Physical source measurements and frozen configurations required")
    if len(rows) != len(frozen):
        raise ValueError("Measurement/snapshot cardinality differs")
    seen = set()
    by_margin = {}
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Invalid source measurement")
        name = safe_option(row.get("OptionId"))
        margin = row.get("ConflictMargin")
        if not isinstance(margin, (int, float)) or isinstance(margin, bool) or margin <= 0:
            raise ValueError("Invalid conflict margin")
        key = (row.get("OptionId"), margin)
        if key in seen:
            raise ValueError("Duplicate option/margin")
        seen.add(key)
        tp = row.get("TrueAcceptedSourceObservations")
        fp = row.get("FalseAcceptedSourceObservations")
        truth = row.get("EligibleSourceObservationsBeforeBlocking")
        recall = row.get("GlobalSourceRecall")
        fdr = row.get("FdrAfterResolve")
        if (any(type(x) is not int or x < 0 for x in (tp, fp, truth))
                or truth == 0 or tp > truth or tp + fp == 0
                or not isinstance(recall, (int, float))
                or not isinstance(fdr, (int, float))
                or abs(tp / truth - recall) > 1e-10
                or abs(fp / (tp + fp) - fdr) > 1e-10):
            raise ValueError("Incoherent accepted-source metrics")
        by_margin.setdefault(margin, []).append((name, row))

    # Semantic digests, independent of row ordering and GitHub ZIP metadata.
    metrics_digest = digest(sorted(rows, key=lambda x: (x["OptionId"], x["ConflictMargin"])))
    snapshots = []
    for entry in frozen:
        if not isinstance(entry, dict):
            raise ValueError("Invalid frozen configuration")
        name = safe_option(entry.get("OptionId"))
        stamp = entry.get("Snapshot")
        sha = stamp.get("ContentSha256") if isinstance(stamp, dict) else None
        if not isinstance(sha, str) or not re.fullmatch(r"[a-fA-F0-9]{64}", sha):
            raise ValueError("Missing immutable snapshot SHA")
        snapshots.append((entry["OptionId"], entry.get("ConflictMargin"), sha.lower()))
    if len(set((x[0], x[1]) for x in snapshots)) != len(snapshots):
        raise ValueError("Duplicate frozen option/margin")
    if set((x[0], x[1]) for x in snapshots) != seen:
        raise ValueError("Snapshot and measurement keys do not match")
    snapshots_digest = digest(sorted(snapshots))

    top_rows = []
    for margin in sorted(by_margin):
        group = by_margin[margin]
        selectable = [(name, row) for name, row in group if not name.startswith("TF_SHARED_")]
        if not selectable:
            raise ValueError("No selectable TF options for margin")
        baseline = next(((name, row) for name, row in group if name == "T0.95_TF0"), None)
        if baseline is None:
            raise ValueError("TF0 control missing")
        leaders = sorted(selectable, key=lambda pair: (
            -pair[1]["GlobalSourceRecall"], pair[1]["FdrAfterResolve"], pair[0]))[:5]
        top_rows.append({
            "margin": margin,
            "options": len(group),
            "selectable": len(selectable),
            "baseline_recall": baseline[1]["GlobalSourceRecall"],
            "best_option": leaders[0][0],
            "best_recall": leaders[0][1]["GlobalSourceRecall"],
            "best_false_accepts": leaders[0][1]["FalseAcceptedSourceObservations"],
            "leaders": [{"option": name, "recall": row["GlobalSourceRecall"],
                         "false_accepts": row["FalseAcceptedSourceObservations"],
                         "fdr": row["FdrAfterResolve"]} for name, row in leaders],
            "identity_independence_attested": sum(
                x["ClusterIidIndependentlyValidated"] is True for _, x in selectable),
        })

    source = selection["SourceReport"]
    pareto = study.get("Pareto", {})
    e2e = study.get("EndToEndMeasurements")
    if not isinstance(e2e, list):
        raise ValueError("End-to-end measurements must be a list")
    manifest = {
        "schema": VERSION, "code_sha": code_sha.lower(), "workflow_run": run_id,
        "study_json_sha256": study_sha,
        "plan_sha256": study.get("PlanSha256"),
        "train_input_sha256": study.get("TrainInputSha256"),
        "validation_input_sha256": study.get("ValidationInputSha256"),
        "predeclared_comparisons": study.get("PredeclaredComparedConfigurations"),
        "measurement_count": len(rows), "snapshot_count": len(frozen),
        "metrics_semantic_sha256": metrics_digest,
        "snapshots_semantic_sha256": snapshots_digest,
        "source_selection_status": source.get("Status"),
        "pareto_status": pareto.get("Status") if isinstance(pareto, dict) else None,
        "end_to_end_measurements": len(e2e),
        "test_used": False, "operationally_certified": False,
        "published": False, "activated": False,
        "margin_results": top_rows,
    }
    lines = [
        "# V9-TF-001 — dossiê exploratório (gerado)",
        "",
        "**Somente exploração TRAIN/VALIDATION. Não é certificação, escolha operacional, TEST ou ativação.**",
        "",
        f"- SHA do código: `{manifest['code_sha']}`",
        f"- GitHub Actions run: {run_id}",
        f"- SHA-256 do study.json: `{study_sha}`",
        f"- Plano: `{manifest['plan_sha256']}`",
        f"- TRAIN: `{manifest['train_input_sha256']}`",
        f"- VALIDATION: `{manifest['validation_input_sha256']}`",
        f"- Comparações pré-declaradas: {manifest['predeclared_comparisons']}",
        f"- Medições: {len(rows)}; snapshots: {len(frozen)}; medições fim a fim: {len(e2e)}",
        f"- Seleção de fonte: `{manifest['source_selection_status']}`",
        f"- Pareto: `{manifest['pareto_status']}`",
        "",
        "## Melhores resultados descritivos por margem (não certificados)",
        "",
        "| Margem | TF0 recall | Melhor opção por recall | Recall | Falsos aceites |",
        "|---|---:|---|---:|---:|",
    ]
    for item in top_rows:
        lines.append(f"| {item['margin']:g} | {percentage(item['baseline_recall'])} | "
                     f"`{item['best_option']}` | {percentage(item['best_recall'])} | "
                     f"{item['best_false_accepts']} |")
    lines += ["", "## Limitações e governança", "",
              "- O ranking acima não substitui a fronteira exploratória completa, a análise independente nem a decisão da autoridade.",
              "- O estudo TF fixou limiar, blocking e margens; não cobre descoberta conjunta do bootstrap nem o lower Gold.",
              "- Independência de identidades-base e incerteza FDR devem ser justificadas, nunca atestadas por booleano arbitrário.",
              "- Repetições requerem comparação de hashes semânticos e de proveniência, não do ZIP inteiro.",
              "- Dados de TEST, nomes e identificadores de pessoas não entram neste documento.",
              ""]
    return "\n".join(lines), manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--study", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--sha", required=True, help="exact code SHA of the physical run")
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--baseline-manifest", type=Path)
    args = parser.parse_args()
    raw_sha = file_digest(args.study)
    markdown, manifest = summarize(strict_json(args.study), raw_sha, args.sha, args.run_id)
    if args.baseline_manifest is not None:
        baseline = strict_json(args.baseline_manifest)
        fields = ("code_sha", "plan_sha256", "train_input_sha256",
                  "validation_input_sha256", "measurement_count", "snapshot_count",
                  "metrics_semantic_sha256", "snapshots_semantic_sha256")
        differences = [key for key in fields if baseline.get(key) != manifest.get(key)]
        if differences:
            raise ValueError("Physical repetition mismatch (fail-closed): " + ", ".join(differences))
        manifest["replication_equal_to_baseline"] = True
    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "V9-TF-001.md").write_text(markdown, encoding="utf-8")
    (args.out_dir / "V9-TF-001.manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8")


if __name__ == "__main__":
    main()

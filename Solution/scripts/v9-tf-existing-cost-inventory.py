#!/usr/bin/env python3
"""Read-only, TEST-blind cost evidence crosswalk for a frozen TF-001 physical study.

No imputed cost, no Pareto/selector updates, no new calibrations and no activation.
Outputs only allowlisted aggregates and code-level identifiers, never source data.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
from pathlib import Path

STUDY_VERSION = "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1"
REPORT_VERSION = "V9_TF_EXISTING_COST_INVENTORY_V1"
ID = re.compile(r"^([0-9a-fA-F]{64}):([A-Za-z0-9_.-]{1,110})$")
SHA = re.compile(r"^[0-9a-fA-F]{64}$")
SRC_COLUMNS = ("OptionId", "ConflictMargin", "GlobalSourceRecall", "FdrAfterResolve",
               "TrueAcceptedSourceObservations", "FalseAcceptedSourceObservations")
CSV_COLUMNS = ("option", "margin", "recall", "fdr", "true_accepted", "false_accepted",
               "same_sql_candidate_frame_sha256", "shared_surname_pairs_diagnostic",
               "end_to_end_p95_ms", "end_to_end_sample_count", "relative_p95_vs_tf0_pct",
               "end_to_end_cost_status")


def digest_file(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            h.update(part)
    return h.hexdigest()


def read_json(path):
    def no_duplicate_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("duplicate JSON key")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=no_duplicate_pairs,
                      parse_constant=lambda x: (_ for _ in ()).throw(ValueError("nonfinite JSON")))


def number(value, field, *, positive=False):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0 or positive and value == 0:
        raise ValueError("invalid " + field)
    return value


def hash64(v, field):
    if not isinstance(v, str) or not SHA.fullmatch(v):
        raise ValueError("invalid hash " + field)
    return v.lower()


def key_for(x):
    if not isinstance(x, dict):
        raise ValueError("non-object evidence")
    oid = x.get("OptionId")
    if not isinstance(oid, str) or not ID.fullmatch(oid):
        raise ValueError("invalid ruleset-prefixed option identifier")
    margin = number(x.get("ConflictMargin"), "ConflictMargin", positive=True)
    return oid, margin


def valid_timer(timer):
    for key in ("ProtocolSha256", "EnvironmentSha256", "OutcomeSha256"):
        hash64(timer.get(key), key)
    samples = timer.get("SamplesMilliseconds")
    q = timer.get("Quantiles")
    if not isinstance(samples, list) or len(samples) < 20 or not isinstance(q, dict):
        raise ValueError("insufficient physical latency samples")
    vals = sorted(number(x, "SamplesMilliseconds") for x in samples)
    if type(q.get("SampleCount")) is not int or q["SampleCount"] != len(vals):
        raise ValueError("latency sample count mismatch")
    p95 = vals[(len(vals) * 95 + 99)//100 - 1]
    p99 = vals[(len(vals) * 99 + 99)//100 - 1]
    for name, value in (("P95Milliseconds", p95), ("P99Milliseconds", p99),
                        ("MaximumMilliseconds", vals[-1])):
        found = number(q.get(name), name)
        if not math.isclose(value, found, abs_tol=1e-9, rel_tol=1e-12):
            raise ValueError("incoherent physical latency quantiles")
    return p95, len(vals)


def build(study, *, code_sha, run_id, study_sha):
    if not isinstance(study, dict) or study.get("Version") != STUDY_VERSION:
        raise ValueError("unexpected study protocol")
    if not isinstance(code_sha, str) or not re.fullmatch(r"[a-fA-F0-9]{40}", code_sha):
        raise ValueError("exact code SHA required")
    if type(run_id) is not int or run_id <= 0:
        raise ValueError("workflow run ID required")
    for name in ("PlanSha256", "TrainInputSha256", "ValidationInputSha256"):
        hash64(study.get(name), name)
    source = study.get("SourceSelection")
    if (not isinstance(source, dict) or source.get("TestUsed") is not False
        or not isinstance(source.get("SourceReport"), dict)
        or source["SourceReport"].get("TestUsed") is not False
        or source["SourceReport"].get("OperationalModelCreated") is not False
        or source["SourceReport"].get("AutomaticActivationAllowed") is not False):
        raise ValueError("source selection not provably TEST-blind and nonoperational")
    measurements = study.get("SourceMeasurements")
    snapshots = study.get("FrozenConfigurations")
    if not isinstance(measurements, list) or len(measurements) != 135 or not isinstance(snapshots, list):
        raise ValueError("TF-001 requires 135 source measurements and snapshots")
    source_keys, snapshot_keys, rulesets, arms = set(), set(), set(), set()
    for row in measurements:
        key = key_for(row)
        if key in source_keys: raise ValueError("duplicate source option/margin")
        source_keys.add(key)
        rulesets.add(key[0].split(":")[0].lower())
        arms.add(key[0].split(":")[1])
        if any(k not in row for k in SRC_COLUMNS): raise ValueError("missing source aggregate")
        recall = number(row["GlobalSourceRecall"], "recall")
        if recall > 1: raise ValueError("invalid recall")
        tp = row["TrueAcceptedSourceObservations"]
        fp = row["FalseAcceptedSourceObservations"]
        truth = row.get("EligibleSourceObservationsBeforeBlocking")
        if any(type(x) is not int or x < 0 for x in (tp, fp, truth)) or truth == 0 or tp > truth:
            raise ValueError("invalid accepted-source counts")
        fdr = row["FdrAfterResolve"]
        if tp + fp == 0 or type(fdr) not in (float, int) or not 0 <= fdr <= 1:
            raise ValueError("missing/incoherent FDR for TF-001")
        if abs(tp/truth - recall) > 1e-10 or abs(fp/(tp+fp) - fdr) > 1e-10:
            raise ValueError("recall/FDR do not reconcile with counts")
        if row.get("CandidateGrouping") != "BLIND_FS_COMPLETE_LINK_CANDIDATES" or row.get("SyntheticGoldOracleGrouping") is not False:
            raise ValueError("untrusted grouping")
    for row in snapshots:
        key = key_for(row)
        if key in snapshot_keys: raise ValueError("duplicate physical snapshot")
        snapshot_keys.add(key)
        if not isinstance(row.get("Snapshot"), dict): raise ValueError("missing snapshot")
        hash64(row["Snapshot"].get("ContentSha256"), "snapshot")
    if source_keys != snapshot_keys or len(rulesets) != 1 or len(arms) != 45:
        raise ValueError("incomplete, unpaired TF-001 or multiple blocking frames")
    expected_margins = {0.5, 2, 4}
    if any({m for oid, m in source_keys if oid.endswith(":" + arm)} != expected_margins for arm in arms):
        raise ValueError("unexpected TF-001 margin family")
    diagnostic = {a for a in arms if a.startswith("TF_SHARED_PL_ML_")}
    if len(diagnostic) != 4 or "T0.95_TF0" not in arms:
        raise ValueError("missing TF0 or four paired diagnostic arms")
    # A fingerprint attests that a SQL candidate frame was common. It is NOT a
    # measured number of pairs, CPU seconds, cost per option or memory usage.
    frame = study.get("CandidatePairsSha256ByRuleset")
    if frame is None:
        frame = {}
    if not isinstance(frame, dict) or set(s.lower() for s in frame) - rulesets:
        raise ValueError("candidate-frame provenance not paired")
    frame_sha = hash64(next(iter(frame.values())), "candidate frame") if len(frame) == 1 else None
    subset = study.get("SharedSurnameCandidatePairsByOption") or {}
    if not isinstance(subset, dict): raise ValueError("invalid shared surname diagnostics")
    if set(subset) - {oid for oid, _ in source_keys}:
        raise ValueError("unknown option in shared-surname diagnostic")
    if any(type(v) is not int or v < 0 for v in subset.values()):
        raise ValueError("invalid shared surname count")
    e2e = study.get("EndToEndMeasurements")
    if not isinstance(e2e, list): raise ValueError("missing EndToEndMeasurements array")
    timings, envs, protocols = {}, set(), set()
    for row in e2e:
        key = key_for(row)
        if key not in source_keys or key in timings:
            raise ValueError("unknown or duplicated end-to-end sample")
        if row.get("CandidateGrouping") != "BLIND_FS_COMPLETE_LINK_CANDIDATES":
            raise ValueError("incorrect timer grouping")
        timings[key] = valid_timer(row)
        envs.add(row["EnvironmentSha256"].lower())
        protocols.add(row["ProtocolSha256"].lower())
    if len(envs) > 1 or len(protocols) > 1:
        raise ValueError("cannot compare timing runs across environments or protocols")
    baseline = {(oid.split(":")[0].lower(), margin): timings.get((oid, margin))
                for oid, margin in source_keys if oid.endswith(":T0.95_TF0")}
    rows = []
    for row in sorted(measurements, key=lambda x: (x["ConflictMargin"], x["OptionId"])):
        oid, margin = key_for(row)
        name = oid.split(":", 1)[1]
        t = timings.get((oid, margin))
        base = baseline[(oid.split(":")[0].lower(), margin)]
        delta = 100*(t[0]/base[0]-1) if t and base and base[0] > 0 else None
        rows.append({
            "option": name, "margin": margin, "recall": row["GlobalSourceRecall"],
            "fdr": row["FdrAfterResolve"],
            "true_accepted": row["TrueAcceptedSourceObservations"],
            "false_accepted": row["FalseAcceptedSourceObservations"],
            "same_sql_candidate_frame_sha256": frame_sha,
            "shared_surname_pairs_diagnostic": subset.get(oid),
            "end_to_end_p95_ms": t[0] if t else None,
            "end_to_end_sample_count": t[1] if t else None,
            "relative_p95_vs_tf0_pct": delta,
            "end_to_end_cost_status": "MEASURED_RELATIVE_TO_TF0" if delta is not None
                else "MEASURED_NO_COMPARABLE_TF0" if t else "NOT_MEASURED",
        })
    manifest = {
        "schema": REPORT_VERSION, "study_sha256": study_sha,
        "code_sha": code_sha.lower(), "run_id": run_id,
        "plan_sha256": study["PlanSha256"].lower(),
        "train_sha256": study["TrainInputSha256"].lower(),
        "validation_sha256": study["ValidationInputSha256"].lower(),
        "source_rows": len(rows), "unique_arms": len(arms),
        "physical_rulesets": len(rulesets), "diagnostic_arms": len(diagnostic),
        "timed_configurations": len(timings),
        "untimed_configurations": len(rows) - len(timings),
        "measured_shared_surname_diagnostics": len(subset),
        "shared_sql_candidate_frame_proven": frame_sha is not None,
        "relative_tf0_latency_comparisons": sum(x["relative_p95_vs_tf0_pct"] is not None for x in rows),
        "cost_eligibility_or_selection_modified": False,
        "cost_monetary_measured": False, "cost_cpu_or_memory_per_arm_measured": False,
        "test_used": False, "activated": False,
        "status": "COMPLETE_LATENCY_WITH_MATCHED_TF0" if len(timings) == 135 and all(x["relative_p95_vs_tf0_pct"] is not None for x in rows)
            else "PARTIAL_PHYSICAL_LATENCY" if timings else "NO_PER_CONFIGURATION_PHYSICAL_COST",
    }
    return manifest, rows


def render(manifest, rows):
    by_margin = sorted({row["margin"] for row in rows})
    lines = ["# V9 TF-001 — inventário de custo observado (sem V4)", "",
             "**Somente TRAIN/VALIDATION e dados agregados. Nenhum parâmetro ou Pareto alterado.**", "",
             "- Fonte SHA-256: " + manifest["study_sha256"],
             "- SHA do código / run: " + manifest["code_sha"] + " / " + str(manifest["run_id"]),
             "- 135 avaliações: 45 braços × 3 margens, 4 braços diagnósticos fora da seleção.",
             "- Configurações com P95 fim a fim medido: " + str(manifest["timed_configurations"]),
             "- Configurações **sem** P95 fim a fim: " + str(manifest["untimed_configurations"]),
             "- Identidade do frame SQL comum: " + ("hash disponível" if manifest["shared_sql_candidate_frame_proven"] else "não comprovada no JSON"),
             "- Diagnósticos por braço de pares com sobrenome compartilhado: " + str(manifest["measured_shared_surname_diagnostics"]),
             "- **Não há, neste study.json, medidas monetárias, CPU ou memória por configuração.**", "",
             "## Disponibilidade por margem", "",
             "| Margem | Braços | P95 físico medido | Comparação P95 vs TF0 |",
             "|---|---:|---:|---:|"]
    for margin in by_margin:
        subset = [x for x in rows if x["margin"] == margin]
        lines.append(f"| {margin:g} | {len(subset)} | {sum(x['end_to_end_p95_ms'] is not None for x in subset)} | "
                     f"{sum(x['relative_p95_vs_tf0_pct'] is not None for x in subset)} |")
    lines += ["", "## Limites de interpretação", "",
              "A contagem shared_surname_pairs_diagnostic (quando presente) é um subconjunto relacionado ao TF; "
              "**não** constitui custo total ou número de comparações SQL de cada braço.",
              "O hash do frame SQL atesta apenas a identidade das arestas geradas; "
              "não mede tempo, CPU ou memória, nem permite rateá-los pelos braços TF.",
              "As contagens de aceites/rejeições, recall e FDR também não são medidas de custo.",
              "Comparações relativas de P95 só aparecem quando o mesmo ruleset e margem incluem "
              "amostras físicas válidas para o braço e para TF0 sob mesmo protocolo e ambiente.",
              "Sem essas medições, custo por configuração = **NÃO MEDIDO**; não presumir empate em custo.",
              "Benchmarks CPU sintéticos e orçamentos SQLite existentes são diagnósticos de outro escopo "
              "e não foram combinados artificialmente com as 135 medições.",
              "Este relatório não ordena candidatos por custo, não modifica a seleção V3, "
              "não usa TEST, não certifica FDR/P95 nem publica/ativa modelo.", "",
              "## Valores agregados por configuração", "",
              "Os 135 registros numéricos e marcações de ausência estão no CSV adjacente; "
              "os quatro diagnósticos TF_SHARED_PL_ML_* não são finalistas."]
    return "\n".join(lines) + "\n"


def run(study_path, out_dir, code_sha, run_id):
    path = Path(study_path).resolve()
    dest = Path(out_dir).resolve()
    if dest.exists() or dest == path or dest in path.parents:
        raise ValueError("output directory must be new and separate from frozen study")
    manifest, rows = build(read_json(path), code_sha=code_sha, run_id=run_id,
                           study_sha=digest_file(path))
    dest.mkdir(parents=True, exist_ok=False)
    (dest / "V9-TF-001-cost-inventory.manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False, sort_keys=True, allow_nan=False) + "\n",
        encoding="utf-8")
    (dest / "V9-TF-001-cost-inventory.md").write_text(render(manifest, rows), encoding="utf-8")
    with (dest / "V9-TF-001-cost-inventory.csv").open("x", newline="", encoding="utf-8") as stream:
        out = csv.DictWriter(stream, CSV_COLUMNS)
        out.writeheader()
        out.writerows(rows)
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--study", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    args = parser.parse_args()
    print(json.dumps(run(args.study, args.out_dir, args.sha, args.run_id), sort_keys=True))

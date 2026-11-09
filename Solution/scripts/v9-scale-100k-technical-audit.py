#!/usr/bin/env python3
"""Fail-closed aggregated 100k V9 multiparameter technical audit (NO TEST)."""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location(
    "j_v9_official_physical", HERE / "v9-j-official-physical-audit.py")
j = importlib.util.module_from_spec(spec)
spec.loader.exec_module(j)
bind_spec = importlib.util.spec_from_file_location(
    "v9_scale_bind", HERE / "v9-scale-100k-technical-plan.py")
binder = importlib.util.module_from_spec(bind_spec)
bind_spec.loader.exec_module(binder)


def require(ok, msg):
    if not ok:
        raise ValueError("V9_100K_AUDIT_FAIL_CLOSED: " + msg)


def audit(plan_path, receipt_path, analysis_path, runtime_path, output_path):
    plan = binder.json_unique(Path(plan_path).read_bytes())
    receipt = binder.json_unique(Path(receipt_path).read_bytes())
    report = binder.json_unique(Path(analysis_path).read_bytes())
    runtime = binder.json_unique(Path(runtime_path).read_bytes())
    require(receipt.get("schema") == binder.SCHEMA
            and receipt.get("people") == 100000 and receipt.get("knownTrainSeed") == 42
            and receipt.get("physicalEvaluations") == 24
            and receipt.get("technicalPlanSha256") == binder.file_sha(plan_path)
            and receipt.get("testUsed") is False and receipt.get("activated") is False,
            "technical bound plan/receipt mismatch")
    c = plan["Calibration"]
    require(c["StudyFingerprintSha256"] == receipt["studyFingerprintSha256"]
            and c["Budget"]["StudyFingerprintSha256"] == receipt["studyFingerprintSha256"]
            and c["ObservationCapDeclaration"]["ManifestSha256"] == receipt["technicalCorpusManifestSha256"]
            and c["ObservationCapDeclaration"]["ObservationsSha256"] == receipt["technicalCorpusObservationsSha256"]
            and plan["PinnedIbgeBootstrap"]["People"] == 30000,
            "plan provenance or historical IBGE pin drift")
    require(report.get("schema") == "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            and report.get("scope") == "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            and report.get("testUsed") is False and report.get("activated") is False
            and report.get("predeclaredCandidateCpMultiplicity") == 8
            and report.get("sourceObservationsSha256") == receipt["technicalCorpusObservationsSha256"]
            and report.get("producerManifestSha256") == receipt["technicalCorpusManifestSha256"]
            and report.get("producerSeed") == 42,
            "physical report cannot be bound to 100k technical corpus")
    require(runtime.get("schema") == "V9_LOCAL_RUNTIME_DIAGNOSTICS_V1"
            and runtime.get("operation") == "TRAIN_SELECTION"
            and runtime.get("testUsed") is False and runtime.get("activated") is False,
            "runtime diagnostics missing or imply activation")
    rows = report.get("rows")
    require(isinstance(rows, list) and len(rows) == 24
            and report.get("evaluations") == 24 and report.get("measured") == 24,
            "all 24 multiparameter configurations must be physically measured")
    by_key, requests, frames = {}, set(), {}
    eligible_count, diagnostic_count, observed = 0, 0, []
    for row in rows:
        key = j.classify(row)
        passes, option, method, weight = key
        require(key not in by_key and row["RequestSha256"] not in requests,
                "duplicate physical hypothesis/fit")
        requests.add(row["RequestSha256"])
        by_key[key] = row
        require(row.get("Batch") == 1 and row.get("ConflictMargin") == 0.5
                and row.get("outcome", "").startswith("MEASURED_"),
                "unmeasured/unexpected physical frame")
        frame = (row["CandidatePairsSha256"], row["fittedPairPrior"],
                 row["eligibleSources"], row["allSelectionObservations"])
        if passes in frames:
            require(frame == frames[passes], "V2 and V3 not on same candidate/SQL frame")
        frames[passes] = frame
        diagnostic = (passes == 19 or method == "DEFAULT"
                      or method == "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE" and weight == 0)
        require(row.get("EligibleForSelection") is (not diagnostic),
                "V2/19-pass/lambda0 eligibility changed")
        diagnostic_count += int(diagnostic)
        eligible_count += int(not diagnostic)
        cells = row.get("observedSupportAudit", {}).get("Cells")
        require(isinstance(cells, list) and len(cells) == 81,
                "observed 81-cell support missing")
        if method != "DEFAULT":
            dist = row.get("estimatorJointSupport", {}).get("Cells")
            require(isinstance(dist, list) and len(dist) == 81,
                    "V3 81-cell fit missing")
        else:
            require(row.get("estimatorJointSupport") is None,
                    "V2 must not write V3 fit distribution")
        tp, fp, count = (row.get("trueAccepted"), row.get("falseAccepted"),
                         row.get("eligibleSources"))
        require(all(type(v) is int for v in (tp, fp, count))
                and 0 <= tp <= count and fp >= 0 and count > 0,
                "accepted source denominators invalid")
        recall = row.get("recall")
        fdr = row.get("fdrObserved")
        p95 = row.get("p95Milliseconds")
        require(type(recall) in (float, int)
                and math.isfinite(recall) and abs(recall-tp/count) < 1e-10,
                "measured recall invalid")
        require(tp+fp > 0 and type(fdr) in (float, int)
                and math.isfinite(fdr) and abs(fdr-fp/(tp+fp)) < 1e-10,
                "observed FDR undefined or incorrectly calculated")
        require(type(p95) in (float, int) and math.isfinite(p95) and p95 >= 0,
                "per-option source-resolution p95 missing")
        require(row.get("clusterIidIndependentlyValidated") is False,
                "synthetic test must not claim independence certification")
        require(row.get("allSelectionObservations") ==
                tp+fp+row["conflicts"]+row["unresolved"],
                "Resolve count reconciliation failed")
        observed.append({
            "optionId": row["OptionId"], "passes": passes,
            "method": method, "uMixtureLambda": weight,
            "role": "WITNESS_OR_DIAGNOSTIC" if diagnostic else "CANDIDATE",
            "tp": tp, "fp": fp, "fn": count-tp, "eligibleSources": count,
            "recall": recall, "fdrObserved": fdr,
            "p95SourceResolutionMilliseconds": p95,
            "candidatePairsSha256": row["CandidatePairsSha256"],
            "fitSnapshotSha256": row["fitSnapshotSha256"]})
    require(diagnostic_count == 16 and eligible_count == 8,
            "24 physical /16 diagnostics /8 candidates partition drift")
    expected = {(passes, option, method, weight)
                for passes in (3, 19) for option in j.OPTIONS
                for method, weight in [("DEFAULT", None),
                                       ("CLASSWISE_SUPPORT_BACKOFF", None)] +
                [("CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE", w) for w in (0,.25,.5,1)]}
    require(set(by_key) == expected, "missing multiparameter physical option")
    for passes in (3, 19):
        for option in j.OPTIONS:
            old = by_key[(passes,option,"CLASSWISE_SUPPORT_BACKOFF",None)]
            zero = by_key[(passes,option,"CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE",0)]
            for field in j.FIELDS:
                require(old[field] == zero[field], "lambda0 V3 physical parity failed")
    duration = runtime.get("totalMilliseconds")
    peak = runtime.get("peakWorkingSetBytes")
    parallel = runtime.get("effectiveParallelism")
    require(type(duration) is int and duration > 0
            and type(peak) is int and peak > 0
            and type(parallel) is int and parallel >= 1,
            "runtime duration/RSS/workers unavailable")
    v2_by_frame = [
        {"optionId": option, "blockingPassCount": 3,
         "v2": {"tp": by_key[3,option,"DEFAULT",None]["trueAccepted"],
                "fp": by_key[3,option,"DEFAULT",None]["falseAccepted"]},
         "sameFrameV3": {"tp": by_key[3,option,"CLASSWISE_SUPPORT_BACKOFF",None]["trueAccepted"],
                         "fp": by_key[3,option,"CLASSWISE_SUPPORT_BACKOFF",None]["falseAccepted"]}}
        for option in j.OPTIONS
    ]
    evidence = {
        "schema": "V9_100K_MULTIPARAM_TECHNICAL_AUDIT_V1",
        "scope": "100K_SYNTHETIC_TRAIN_ONLY_NO_TEST_NO_ACTIVATION",
        "algorithm": "V9_CSHARP_MULTIPARAMETER_24_HYPOTHESES",
        "actualCorpusPeople": 100000, "corpusSeed": 42,
        "historicalOfficialPlanSha256": receipt["historicalOfficialPlanSha256"],
        "technicalPlanSha256": receipt["technicalPlanSha256"],
        "technicalCorpusManifestSha256": receipt["technicalCorpusManifestSha256"],
        "technicalCorpusObservationsSha256": receipt["technicalCorpusObservationsSha256"],
        "physicalHypothesesMeasured": 24, "witnessAndDiagnostics": 16,
        "eligibleCpHypotheses": 8, "totalMilliseconds": duration,
        "peakProcessRssBytes": peak, "effectiveParallelism": parallel,
        "perOption": observed, "sameFrameV2vsV3": v2_by_frame,
        "metrics": {
            "physical24": "PASS", "corpusAndPlanSha": "PASS",
            "tpFpFnRecallFdrObserved": "PASS", "sourceResolutionP95": "PASS",
            "wallClock": "PASS", "peakProcessRss": "PASS",
            "observedDiskPeak": "NAO_COBERTO",
            "independentPopulationFdr": "NAO_COBERTO",
            "HTTP_100K": "NAO_COBERTO"
        },
        "testUsed": False, "certified": False, "activated": False
    }
    output_path = Path(output_path)
    with output_path.open("x", encoding="utf-8") as f:
        json.dump(evidence, f, indent=2, allow_nan=False)
        f.write("\n")
    output_path.with_suffix(".md").write_text(
        "# V9 100k — prova técnica física, não certificação científica\n\n"
        "Corpus sintético 100000 pessoas; SHA do plano: " +
        receipt["technicalPlanSha256"] + "\n\n"
        "24/24 hipóteses M/U/TF/SQL; 16 testemunhas/diagnósticos e 8 CP elegíveis. "
        "TP/FP/FN, recall, FDR observado e P95 de resolução por opção em JSON. "
        "Duração total em ms: " + str(duration) +
        "; pico RSS do processo (bytes): " + str(peak) +
        ". NÃO COBERTO: FDR populacional, HTTP 100k e pico de disco sem coleta.\n",
        encoding="utf-8")
    return evidence


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("plan", "receipt", "analysis", "runtime", "out"):
        parser.add_argument("--" + name, type=Path, required=True)
    a = parser.parse_args()
    e = audit(a.plan, a.receipt, a.analysis, a.runtime, a.out)
    print(json.dumps({"result": "V9_100K_TRAIN_TECHNICAL_PASS",
                      "physical": e["physicalHypothesesMeasured"],
                      "planSha256": e["technicalPlanSha256"],
                      "certified": False, "activated": False}))

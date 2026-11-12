#!/usr/bin/env python3
"""Fail-closed audit of ONE V3-only TF4 TRAIN campaign; no FDR certification."""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("v9_tf4_plan", HERE / "v9-v3-four-name-100k-plan.py")
study = importlib.util.module_from_spec(spec)
spec.loader.exec_module(study)
SCHEMA = "V9_V3_TF4_100K_TECHNICAL_AUDIT_V1"


def require(condition, message):
    if not condition:
        raise ValueError("V9_V3_TF4_AUDIT_FAIL_CLOSED: " + message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def p95_was_measured(row):
    value = row.get("p95Milliseconds")
    rejection = row.get("Rejections")
    require(isinstance(rejection, list) and all(type(x) is str for x in rejection),
            "invalid rejection list")
    if value is None:
        # The C# evaluator skips the replay for ANY of these three exact
        # pre-replay exclusions; a 3-pass selectable arm becomes
        # MEASURED_NOT_ELIGIBLE (not a structurally diagnostic arm).
        allowed = {"FANOUT_P99_ACIMA_DO_ORCAMENTO",
                   "SUPORTE_OBSERVADO_NO_NUCLEO_DE_MASSA_INSUFICIENTE",
                   "FDR_INDEFINIDO_SEM_ACEITOS"}
        require(len(rejection) > 0 and len(rejection) == len(set(rejection))
                and set(rejection).issubset(allowed)
                and row.get("outcome") == "MEASURED_NOT_ELIGIBLE",
                "missing P95 without verified pre-replay exclusion")
        return False
    require(type(value) in (float, int) and math.isfinite(value) and value >= 0,
            "invalid P95 value")
    return True


def audit(plan_path, receipt_path, analysis_path, runtime_path, selection_path,
          out_path, code_sha, run_id):
    require(isinstance(code_sha, str) and len(code_sha) == 40
            and all(c in "0123456789abcdef" for c in code_sha),
            "invalid immutable HEAD SHA")
    require(type(run_id) is int and run_id > 0, "invalid GitHub run")
    plan = study.strict_json(Path(plan_path).read_bytes())
    receipt = study.strict_json(Path(receipt_path).read_bytes())
    report = study.strict_json(Path(analysis_path).read_bytes())
    runtime = study.strict_json(Path(runtime_path).read_bytes())
    selection = study.strict_json(Path(selection_path).read_bytes())
    c = plan["Calibration"]
    require(receipt.get("schema") == study.SCHEMA
            and receipt.get("planSha256") == sha(plan_path)
            and receipt.get("physicalHypotheses") == study.EVALUATIONS
            and receipt.get("tfOptions") == study.OPTIONS
            and receipt.get("testUsed") is False
            and receipt.get("certified") is False
            and receipt.get("activated") is False
            and c["StudyFingerprintSha256"] == receipt["studyFingerprintSha256"]
            and c["Budget"]["StudyFingerprintSha256"] == receipt["studyFingerprintSha256"]
            and c["ObservationCapDeclaration"]["ManifestSha256"]
                == receipt["technicalCorpusManifestSha256"]
            and c["ObservationCapDeclaration"]["ObservationsSha256"]
                == receipt["technicalCorpusObservationsSha256"]
            and plan["PinnedIbgeBootstrap"]["People"] == 30000,
            "plan, provenance or budget receipt drift")
    require(c["ModelOptions"] == study.grid(c["ModelOptions"][0])
            and c["ConflictMargins"] == list(study.MARGINS)
            and c["MaximumComparedConfigurations"] == study.EVALUATIONS
            and plan["MaximumSearchConfigurations"] == study.EVALUATIONS
            and plan["MaximumBatches"] == 1
            and plan["PredeclaredCandidateCpMultiplicity"] == study.EVALUATIONS
            and len(plan["MuEstimationMethods"]) == 1
            and plan["MuEstimationMethods"][0]["Method"] ==
                "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"
            and plan["MuEstimationMethods"][0]["Parameters"]["UBackoffJointMixtureWeights"]
                == list(study.LAMBDAS)
            and c["SelectionPolicy"]["MaximumP95Milliseconds"] == 60000
            and c["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == 0.01
            and c["Budget"]["MaximumObservedAcceptedFdrPoint"] == 0.01
            and c["MaximumPhysicalRuleSets"] == 1
            and c["MaximumParallelRuleSets"] == 0,
            "scientific V3-only search drift")

    require(report.get("schema") == "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            and report.get("scope") == "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            and report.get("testUsed") is False and report.get("activated") is False
            and report.get("sourceObservationsSha256")
                == receipt["technicalCorpusObservationsSha256"]
            and report.get("producerManifestSha256")
                == receipt["technicalCorpusManifestSha256"]
            and report.get("producerSeed") == 42
            and report.get("predeclaredCandidateCpMultiplicity") == study.EVALUATIONS,
            "unbound or unapproved scientific report")
    require(runtime.get("schema") == "V9_LOCAL_RUNTIME_DIAGNOSTICS_V1"
            and runtime.get("operation") == "TRAIN_SELECTION"
            and runtime.get("testUsed") is False
            and runtime.get("activated") is False,
            "missing runtime evidence")
    require(selection.get("testUsed") is False
            and selection.get("activated") is False
            and selection.get("externalValidationUsed") is False,
            "unexpected holdout or activation")
    rows = report.get("rows")
    require(isinstance(rows, list) and len(rows) == study.EVALUATIONS
            and report.get("evaluations") == study.EVALUATIONS,
            "missing one or more declared hypothesis rows")
    measured = sum(isinstance(row, dict)
                   and row.get("outcome", "").startswith("MEASURED_") for row in rows)
    require(report.get("measured") == measured == study.EVALUATIONS
            and report.get("publishedTfCoverageInsufficient") == 0
            and report.get("scientificallyNonEvaluable") == 0,
            "partial physical experiment, not a 324/324 technical proof")
    expected = {(o["OptionId"], u, m) for o in c["ModelOptions"]
                for u in study.LAMBDAS for m in study.MARGINS}
    observed, request_hashes, frame_sha, summary = set(), set(), None, []
    replayed, fanout = 0, 0
    pass_ids = {p["PassId"] for p in c["BaselineRuleSets"][0]}
    for row in rows:
        require(isinstance(row, dict), "invalid hypothesis row")
        option_id = row.get("OptionId", "")
        require(type(option_id) is str and "__MU_" in option_id,
                "unidentifiable M/U option")
        root, suffix = option_id.split("__MU_", 1)
        policy = row.get("MuPolicy")
        require(isinstance(policy, dict), "M/U policy missing")
        weight = policy.get("UBackoffJointMixtureWeight")
        margin = row.get("ConflictMargin")
        key = (root, weight, margin)
        require(key in expected and key not in observed
                and suffix.endswith("UJW" + format(weight, "g"))
                and row.get("MuEstimationMethod") ==
                    "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"
                and row.get("Batch") == 1
                and row.get("EligibleForSelection") is True,
                "unknown or duplicate V3 hypothesis")
        observed.add(key)
        request_hash = row.get("RequestSha256")
        require(type(request_hash) is str and len(request_hash) == 64
                and request_hash not in request_hashes,
                "duplicate/missing request fingerprint")
        request_hashes.add(request_hash)
        passes = row.get("blockingPasses")
        require(isinstance(passes, list) and len(passes) == 3
                and {p["PassId"] for p in passes} == pass_ids,
                "different blocking frame")
        digest = row.get("CandidatePairsSha256")
        require(type(digest) is str and len(digest) == 64,
                "missing physical pair digest")
        if frame_sha is None:
            frame_sha = digest
        require(digest == frame_sha, "different SQL candidate set for TF/margin")
        require(len(row.get("observedSupportAudit", {}).get("Cells", [])) == 81
                and len(row.get("estimatorJointSupport", {}).get("Cells", [])) == 81
                and row.get("clusterIidIndependentlyValidated") is False,
                "missing 81-cell V3 fit or false scientific independence")
        tp, fp, eligible = row.get("trueAccepted"), row.get("falseAccepted"), row.get("eligibleSources")
        conflict, unresolved, total = row.get("conflicts"), row.get("unresolved"), row.get("allSelectionObservations")
        require(all(type(x) is int for x in (tp, fp, eligible, conflict, unresolved, total))
                and eligible > 0 and 0 <= tp <= eligible and fp >= 0
                and total == tp + fp + conflict + unresolved,
                "TP/FP/FN or Resolve accounting invalid")
        recall, fdr = row.get("recall"), row.get("fdrObserved")
        require(type(recall) in (float, int) and math.isfinite(recall)
                and abs(recall - tp / eligible) < 1e-10,
                "recall not reconciled")
        if tp + fp == 0:
            require(fdr is None
                    and "FDR_INDEFINIDO_SEM_ACEITOS" in row.get("Rejections", []),
                    "zero-accepted option incorrectly claims FDR or eligibility")
        else:
            require(type(fdr) in (float, int) and math.isfinite(fdr)
                    and abs(fdr - fp / (tp + fp)) < 1e-10,
                    "observed FDR not reconciled")
        if p95_was_measured(row):
            replayed += 1
        else:
            fanout += 1
        summary.append({"optionId": root, "lambda": weight, "margin": margin,
                        "tp": tp, "fp": fp, "fn": eligible - tp, "recall": recall,
                        "fdrObserved": fdr, "p95Milliseconds": row["p95Milliseconds"],
                        "rejections": row["Rejections"], "fitSha256": row.get("fitSnapshotSha256")})
    require(observed == expected and replayed + fanout == study.EVALUATIONS,
            "incomplete unique physical hypothesis set")
    require(type(runtime.get("totalMilliseconds")) is int
            and runtime["totalMilliseconds"] > 0
            and type(runtime.get("peakWorkingSetBytes")) is int
            and runtime["peakWorkingSetBytes"] > 0
            and type(runtime.get("effectiveParallelism")) is int
            and runtime["effectiveParallelism"] >= 1,
            "runtime resource measurements missing")
    blocking_passes = c["BaselineRuleSets"][0]
    require(all(isinstance(p, dict) and isinstance(p.get("PassId"), str)
                for p in blocking_passes), "invalid blocking pass definition")
    finalist_shas = selection.get("FinalistSnapshotShas") or []
    require(isinstance(finalist_shas, list), "invalid finalist list")
    finalist_details = [
        {"snapshotSha256": sha, "fileName": sha + ".fitted.json",
         "blockingPasses": blocking_passes}
        for sha in finalist_shas
    ]
    finalist_markdown = "\n".join(
        "### " + item["fileName"] + "\n\n"
        + "SHA: " + item["snapshotSha256"] + "\n\n"
        + "\n".join(
            "- **" + str(p["PassId"]) + "**: "
            + json.dumps(p, ensure_ascii=False, sort_keys=True)
            for p in item["blockingPasses"])
        for item in finalist_details) or "- Nenhum finalista selecionado."

    evidence = {
        "schema": SCHEMA, "status": "TRAIN_TECHNICAL_PASS_NOT_SCIENTIFIC_CERTIFICATION",
        "codeSha": code_sha, "workflowRunId": run_id,
        "planSha256": receipt["planSha256"],
        "corpusObservationsSha256": receipt["technicalCorpusObservationsSha256"],
        "physicalMeasured": study.EVALUATIONS, "tfOptions": study.OPTIONS,
        "margins": list(study.MARGINS), "v3MixtureLambdas": list(study.LAMBDAS),
        "replayedP95": replayed, "preReplayRejectedWithoutP95": fanout,
        "totalMilliseconds": runtime["totalMilliseconds"],
        "peakProcessRssBytes": runtime["peakWorkingSetBytes"],
        "effectiveParallelism": runtime["effectiveParallelism"],
        "stopReason": selection.get("StopReason"),
        "finalistSnapshotShas": selection.get("FinalistSnapshotShas"),
        "finalistDetails": finalist_details,
        "perHypothesis": summary, "testUsed": False, "certified": False,
        "activated": False, "populationFdrIndependent": "NOT_COVERED",
        "http100k": "NOT_COVERED"}
    target = Path(out_path)
    with target.open("x", encoding="utf8") as output:
        json.dump(evidence, output, indent=2, allow_nan=False)
        output.write("\n")
    target.with_suffix(".md").write_text(
        "# V9 four-name 100k — PASS técnico TRAIN, não certificação\n\n"
        "324 hipóteses V3-only medidas; TF=81, λ=2, margens=2. "
        "P95 reais: " + str(replayed) + "; P95 ausentes por rejeição pré-replay: "
        + str(fanout) + ". TEST, validação independente e ativação: NÃO.\n\n"
        + "## Passes de blocking dos finalistas\n\n"
        + finalist_markdown + "\n",
        encoding="utf8")
    return evidence


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    for key in ("plan", "receipt", "analysis", "runtime", "selection", "out"):
        p.add_argument("--" + key, type=Path, required=True)
    p.add_argument("--code-sha", required=True)
    p.add_argument("--run-id", type=int, required=True)
    a = p.parse_args()
    result = audit(a.plan, a.receipt, a.analysis, a.runtime, a.selection, a.out,
                   a.code_sha, a.run_id)
    print(json.dumps({"technical": "V9_V3_TF4_100K_TRAIN_PASS",
                      "measured": result["physicalMeasured"],
                      "certified": False, "activated": False}))

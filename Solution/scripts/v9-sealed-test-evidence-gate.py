#!/usr/bin/env python3
"""Fail-closed validation of externally produced V9 sealed TEST evidence.

This verifier does not run TEST, calibrate a model or certify production.
Input must come from an independently executed, auditable physical SQL run.
"""
import argparse
import json
import math
import re
from pathlib import Path

HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(value, name):
    require(isinstance(value, str) and HEX64.fullmatch(value) is not None, name + " invalid")


def count(value, name):
    require(type(value) is int and value >= 0, name + " must be a nonnegative integer")
    return value


def validate(document):
    require(type(document) is dict and document.get("schema") == "V9_SEALED_TEST_EVIDENCE_V1", "schema invalid")
    frozen = document["frozen"]
    for name in ("snapshotSha256", "scorerSha256", "tfPolicySha256", "blockingSha256", "testFrameSha256"):
        digest(frozen[name], name)
    require(isinstance(frozen.get("gitCommit"), str) and HEX40.fullmatch(frozen["gitCommit"]) is not None, "gitCommit invalid")
    require(frozen.get("frozenBeforeTest") is True, "model must be frozen before TEST")
    require(document.get("partition") == "TEST", "independent TEST partition required")
    require(document.get("testUsedForTuning") is False, "TEST cannot tune parameters")
    require(document.get("independentEvaluator") is True, "independent evaluator required")
    require(document.get("completeTruthLabels") is True, "complete truth labels required")
    require(document.get("sqlPhysicalExecution") is True, "physical SQL execution required")
    require(document.get("artifactSha256") is not None, "external evidence artifact hash required")
    digest(document["artifactSha256"], "artifactSha256")
    require(isinstance(document.get("runId"), str) and bool(document["runId"].strip()), "runId missing")

    blocking = document["blocking"]
    eligible = count(blocking["eligibleTruePairsBeforeBlocking"], "eligibleTruePairsBeforeBlocking")
    recovered = count(blocking["recoveredTruePairsBySql"], "recoveredTruePairsBySql")
    require(eligible > 0 and recovered <= eligible, "blocking denominator invalid")
    require(count(blocking["physicalDistinctCandidatePairs"], "physicalDistinctCandidatePairs") >= recovered, "candidate count invalid")

    fdr = document["fdr"]
    cap = count(fdr["predeclaredMaxAcceptedPerIdentity"], "predeclaredMaxAcceptedPerIdentity")
    require(cap > 0 and fdr.get("capEnforcedByDesign") is True, "fanout cap must be predeclared and enforced")
    require(fdr.get("independentIdentityClusters") is True, "independent identity clusters required")
    groups = fdr["groups"]
    require(isinstance(groups, list) and len(groups) >= 2, "at least two identity clusters required")
    identities = set()
    false_total = accepted_total = 0
    for group in groups:
        identity = group["baseIdentityFingerprint"]
        digest(identity, "baseIdentityFingerprint")
        require(identity not in identities, "duplicate identity cluster")
        identities.add(identity)
        tp = count(group["trueAccepted"], "trueAccepted")
        fp = count(group["falseAccepted"], "falseAccepted")
        require(tp + fp <= cap, "identity cluster exceeds predeclared cap")
        false_total += fp
        accepted_total += tp + fp
    require(accepted_total > 0, "zero accepted links: FDR undefined")
    alpha = fdr.get("alpha")
    require(type(alpha) in (float, int) and math.isfinite(alpha) and alpha == 0.05, "one-sided alpha must equal 0.05")
    n = len(groups)
    radius = cap * math.sqrt(math.log(2 / alpha) / (2 * n))
    lower_accepted = accepted_total / n - radius
    require(lower_accepted > 0, "FDR upper bound denominator unsupported")
    upper = min(1.0, (false_total / n + radius) / lower_accepted)
    require(math.isfinite(upper), "invalid upper FDR")
    # No automatic certification: an approved threshold and governance are external.
    runs = document["physicalSqlRuns"]
    require(isinstance(runs, list) and len(runs) >= 20, "20 comparable physical SQL runs required")
    unique_runs = set()
    for run in runs:
        for name in ("testFrameSha256", "blockingSha256"):
            require(run[name] == frozen[name], "physical SQL provenance differs: " + name)
        for name in ("environmentSha256", "sqlRulesetSha256"):
            digest(run[name], name)
            require(run[name] == runs[0][name], "incomparable SQL run: " + name)
        run_id = run["runId"]
        require(isinstance(run_id, str) and run_id.strip() and run_id not in unique_runs, "duplicate/invalid SQL run ID")
        unique_runs.add(run_id)
        for name in ("createTablesMs", "bulkObservationsMs", "bulkSignaturesMs", "materializePairsMs", "readPairsMs"):
            value = run[name]
            require(type(value) in (float, int) and math.isfinite(value) and value >= 0, "invalid physical duration: " + name)
    samples = sorted(run["materializePairsMs"] for run in runs)
    rank = lambda p: samples[math.ceil(len(samples) * p) - 1]
    return {"status": "EVIDENCE_CONTRACT_VALID_NOT_CERTIFIED", "blockingRecall": recovered / eligible,
            "observedFdr": false_total / accepted_total, "fdrOneSided95Upper": upper,
            "identityClusters": n, "physicalSqlRuns": len(runs),
            "materializePairsP95Ms": rank(.95), "materializePairsP99Ms": rank(.99),
            "operationallyCertified": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(validate(json.loads(args.evidence.read_text(encoding="utf-8"))), sort_keys=True))
    except (ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        parser.exit(1, "FAIL_CLOSED: " + str(exc) + "\n")


if __name__ == "__main__":
    main()

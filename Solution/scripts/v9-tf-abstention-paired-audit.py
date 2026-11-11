#!/usr/bin/env python3
"""TRAIN-only paired TF0/TF abstention audit. Descriptive, NEVER certification."""
import argparse
import json
import math
import random
from pathlib import Path

SCHEMA = "V9_TF_ABSTENTION_PAIRED_CLUSTERS_V1"
ARMS = ("TF0", "TF_ABSTAIN")


def check(ok, message):
    if not ok:
        raise ValueError("V9_ABSTENTION_FAIL_CLOSED: " + message)


def integer(value):
    return type(value) is int and value >= 0


def rate(num, den):
    return num / den if den else None


def metrics(rows):
    positive = sum(x["positive"] for x in rows)
    tp = sum(x["tp"] for x in rows)
    fp = sum(x["fp"] for x in rows)
    abstained = sum(x["abstained"] for x in rows)
    abstained_positive = sum(x["abstainedPositive"] for x in rows)
    return {
        "positive": positive, "trueAccepted": tp, "falseAccepted": fp,
        "abstained": abstained, "abstainedPositive": abstained_positive,
        "recallEndToEnd": rate(tp, positive),
        "fdrObserved": rate(fp, tp + fp),
    }


def audit(payload, replicates=2000, seed=42):
    check(isinstance(payload, dict) and payload.get("schema") == SCHEMA,
          "schema mismatch")
    check(payload.get("scope") == "TRAIN_ONLY_NO_TEST_NO_ACTIVATION"
          and payload.get("testUsed") is False
          and payload.get("activated") is False, "unsafe scope")
    check(type(replicates) is int and replicates >= 200,
          "too few bootstrap replicates")
    clusters = payload.get("clusters")
    check(isinstance(clusters, list) and len(clusters) >= 2,
          "at least two independent identity clusters required")
    seen = set()
    paired = {arm: [] for arm in ARMS}
    for c in clusters:
        check(isinstance(c, dict) and isinstance(c.get("clusterId"), str)
              and c["clusterId"] and c["clusterId"] not in seen,
              "duplicate/invalid cluster")
        seen.add(c["clusterId"])
        check(all(isinstance(c.get(arm), dict) for arm in ARMS),
              "missing paired arm")
        for arm in ARMS:
            x = c[arm]
            fields = ("eligible", "positive", "tp", "fp",
                      "abstained", "abstainedPositive")
            check(all(integer(x.get(k)) for k in fields), "invalid count")
            check(x["positive"] <= x["eligible"]
                  and x["tp"] + x["abstainedPositive"] <= x["positive"]
                  and x["fp"] <= x["eligible"] - x["positive"]
                  and x["abstainedPositive"] <= x["abstained"]
                  and x["abstained"] <= x["eligible"]
                  and x["tp"] + x["fp"] + x["abstained"] <= x["eligible"],
                  "inconsistent counts")
            paired[arm].append(x)
        check(c["TF0"]["eligible"] == c["TF_ABSTAIN"]["eligible"]
              and c["TF0"]["positive"] == c["TF_ABSTAIN"]["positive"],
              "SQL frame/gabarito differs across arms")
    check(isinstance(payload.get("candidatePairsSha256"), str)
          and len(payload["candidatePairsSha256"]) == 64
          and all(ch in "0123456789abcdef" for ch in payload["candidatePairsSha256"]),
          "missing immutable common candidate frame")
    summary = {arm: metrics(paired[arm]) for arm in ARMS}
    check(all(summary[a]["positive"] > 0 for a in ARMS),
          "no positive examples; recall undefined")
    rng = random.Random(seed)
    n = len(clusters)
    differences = []
    fdr_samples = {arm: [] for arm in ARMS}
    for _ in range(replicates):
        indices = [rng.randrange(n) for __ in range(n)]
        sample = {arm: metrics([paired[arm][i] for i in indices])
                  for arm in ARMS}
        a, b = sample["TF0"], sample["TF_ABSTAIN"]
        if a["recallEndToEnd"] is not None and b["recallEndToEnd"] is not None:
            differences.append(b["recallEndToEnd"] - a["recallEndToEnd"])
        for arm in ARMS:
            if sample[arm]["fdrObserved"] is not None:
                fdr_samples[arm].append(sample[arm]["fdrObserved"])
    def percentile(values, p):
        if not values:
            return None
        values.sort()
        return values[min(len(values) - 1, math.ceil(p * len(values)) - 1)]
    return {
        "schema": "V9_TF_ABSTENTION_PAIRED_DESCRIPTIVE_V1",
        "status": "INCOMPLETE_NOT_CERTIFIED",
        "scope": "TRAIN_ONLY_NO_TEST_NO_ACTIVATION",
        "candidatePairsSha256": payload["candidatePairsSha256"],
        "clusters": n, "bootstrapReplicates": replicates, "seed": seed,
        "arms": summary,
        "deltaRecallAbstainMinusTF0": (
            summary["TF_ABSTAIN"]["recallEndToEnd"] -
            summary["TF0"]["recallEndToEnd"]),
        "deltaRecallClusterBootstrapPercentile95": [
            percentile(differences, .025), percentile(differences, .975)],
        "fdrClusterBootstrapDescriptiveUpper95": {
            arm: percentile(fdr_samples[arm], .95) for arm in ARMS},
        "certified": False, "testUsed": False, "activated": False,
        "warning": ("Cluster bootstrap percentile is descriptive, not an "
                    "FDR upper confidence bound certified under arbitrary "
                    "dependence; requires independent validation, cluster "
                    "assumptions and predeclared multiplicity before PASS."),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--replicates", type=int, default=2000)
    args = parser.parse_args()
    source = Path(args.input)
    output = Path(args.output)
    check(source.resolve() != output.resolve(), "input/output collision")
    payload = json.loads(source.read_text(encoding="utf-8"),
                         parse_constant=lambda x: check(False, "nonfinite input"))
    result = audit(payload, args.replicates)
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2, allow_nan=False)
        stream.write("\n")


if __name__ == "__main__":
    main()

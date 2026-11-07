#!/usr/bin/env python3
"""Independent audit for private in-memory SAME-SOURCE V2/V3 Rank transition totals.

Consumes ONLY released aggregate histograms and paired counters; source IDs and
candidate scores are never loaded from the artifact. No causal U estimate,
IID inference, FDR certification, independent validation, or TEST.
"""
import argparse
import hashlib
import json
from pathlib import Path

METHODS = ("DEFAULT", "CLASSWISE_SUPPORT_BACKOFF")
OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
TRANSITION_BINS = (
    "LESS_THAN_MINUS_2", "MINUS_2_TO_MINUS_0.5",
    "MINUS_0.5_TO_MINUS_0.25", "MINUS_0.25_TO_0",
    "0_TO_0.25", "0.25_TO_0.5", "0.5_TO_2", "2_AND_ABOVE",
)
LOSSES = (
    "V2CorrectLostToV3Conflict",
    "V2CorrectLostToV3Unresolved",
    "V2CorrectLostToV3IncorrectResolved",
)


def number(v, label):
    if type(v) is not int or v < 0:
        raise ValueError("Missing or invalid counter: " + label)
    return v


def check_pair(p, left, right):
    pair = p["pair"]
    count = number(pair.get("SourceCount"), "sources")
    if (p.get("v2RequestSha256") != left["RequestSha256"]
            or p.get("v3RequestSha256") != right["RequestSha256"]
            or p.get("candidatePairsSha256") != left["CandidatePairsSha256"]
            or left["CandidatePairsSha256"] != right["CandidatePairsSha256"]
            or left["fittedPairPrior"] != right["fittedPairPrior"]
            or left["eligibleSources"] != right["eligibleSources"]
            or count != left["allSelectionObservations"]
            or count != right["allSelectionObservations"]
            or p.get("blockingPassCount") != len(left["blockingPasses"])
            or p.get("blockingPassCount") != len(right["blockingPasses"])
            or p.get("ConflictMargin") != .5
            or pair.get("Scope") != "SAME_SQL_SYNTHETIC_TRAIN_SELECTION_PAIRED_SOURCE_DIAGNOSTIC"
            or pair.get("IncludesIndividualSourceData") is not False
            or pair.get("UFallbackCausalityEstablished") is not False
            or pair.get("IndependentValidation") is not False
            or pair.get("TestUsed") is not False
            or pair.get("Certified") is not False):
        raise ValueError("Paired Rank evidence violates provenance or governance")
    if (number(pair.get("V2CorrectResolved"), "V2 TP") != left["trueAccepted"]
            or number(pair.get("V3CorrectResolved"), "V3 TP") != right["trueAccepted"]
            or number(pair.get("V2IncorrectResolved"), "V2 FP") != left["falseAccepted"]
            or number(pair.get("V3IncorrectResolved"), "V3 FP") != right["falseAccepted"]):
        raise ValueError("Rank pair TP/FP does not equal fixed physical Resolve")
    losses = sum(number(pair.get(k), k) for k in LOSSES)
    gains = number(pair.get("V3CorrectGained"), "gained")
    if (left["trueAccepted"] - right["trueAccepted"] != losses - gains
            or losses != (number(pair.get("V2CorrectLostWithV3UFallback"), "fallback")
                          + number(pair.get("V2CorrectLostWithoutV3UFallback"), "without fallback"))
            or any(number(pair.get(key), key) > count for key in (
                "V2CorrectRetainedWithV3UFallback",
                "V2CorrectLostSameTopTwoGroups",
                "V2CorrectLostChangedTopCandidate",
                "V2CorrectLostMarginHalfCrossedWithStableTopTwo",
                "DifferentFirstCandidate", "DifferentSecondCandidate",
                "DifferentBlindGroupMembership", "ComparableTopTwoWithBothScores"))
            or number(pair["V2CorrectLostSameTopTwoGroups"], "stable") > losses
            or number(pair["V2CorrectLostChangedTopCandidate"], "changed top") > losses
            or number(pair["V2CorrectLostMarginHalfCrossedWithStableTopTwo"], "crossed") >
                number(pair["V2CorrectLostSameTopTwoGroups"], "stable")):
        raise ValueError("Source-paired loss, gain and fallback counts do not reconcile")
    reasons = pair.get("V3ReasonsOnV2CorrectLoss")
    bins = pair.get("ChangeInTopTwoGapBins")
    if (not isinstance(reasons, dict) or not isinstance(bins, dict)
            or set(bins) != set(TRANSITION_BINS)
            or any(type(v) is not int or v < 0 for v in (*reasons.values(), *bins.values()))
            or sum(reasons.values()) != losses
            or sum(bins.values()) != pair["ComparableTopTwoWithBothScores"]):
        raise ValueError("Paired source loss reasons or gap histogram non-reconciled")
    return {"blockingPasses":p["blockingPassCount"],
            "option":p["rootOptionId"],
            "sources":count, "v2Tp":left["trueAccepted"],
            "v3Tp":right["trueAccepted"],
            "resolvedCorrectLosses":losses,
            "resolvedCorrectGains":gains,
            "lossesWithFallbackU":pair["V2CorrectLostWithV3UFallback"],
            "lostToConflict":pair["V2CorrectLostToV3Conflict"],
            "sameTopTwoAndBlindGroups":pair["V2CorrectLostSameTopTwoGroups"],
            "marginHalfCrossed":pair["V2CorrectLostMarginHalfCrossedWithStableTopTwo"],
            "sourceSqlPairsSha256":left["CandidatePairsSha256"]}


def audit(path):
    raw = Path(path).read_bytes()
    report = json.loads(raw)
    if (b'"SourceRankInternal"' in raw
            or report.get("schema") != "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1"
            or report.get("scope") != "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            or report.get("testUsed") is not False or report.get("activated") is not False
            or report.get("evaluations") != 8 or report.get("measured") != 8
            or len(report.get("rows", [])) != 8):
        raise ValueError("Not the eight-arm TRAIN run or private source details leaked")
    rows = {}
    for row in report["rows"]:
        option = next((x for x in OPTIONS
                       if row.get("OptionId", "").startswith(x + "__MU_")), None)
        key = (len(row.get("blockingPasses") or []), option, row.get("MuEstimationMethod"))
        if (option is None or key[0] not in (3,19) or key[2] not in METHODS
                or key in rows or row.get("ConflictMargin") != .5):
            raise ValueError("Incorrect or duplicate row in frozen source run")
        rows[key] = row
    pairs = report.get("pairedSourceRankAttribution")
    if not isinstance(pairs, list) or len(pairs) != 4:
        raise ValueError("Missing all four source-paired Rank comparisons")
    result, seen = [], set()
    for p in pairs:
        key = (p.get("blockingPassCount"), p.get("rootOptionId"))
        if key in seen or key[0] not in (3,19) or key[1] not in OPTIONS:
            raise ValueError("Duplicate/unknown source-paired comparison")
        seen.add(key)
        left = rows[(key[0], key[1], METHODS[0])]
        right = rows[(key[0], key[1], METHODS[1])]
        result.append(check_pair(p, left, right))
    if len(rows) != 8 or len(seen) != 4:
        raise ValueError("Missing 3/19 × TF0/ONE_MF strata")
    return {
        "schema": "V9_V3_TF0_PAIRED_SOURCE_U_ASSOCIATION_V1",
        "scope": "KNOWN_SEED42_TRAIN_SELECTION_DESCRIPTIVE_NOT_INDEPENDENT",
        "analysisSha256": hashlib.sha256(raw).hexdigest(),
        "comparisons": sorted(result, key=lambda x:(x["blockingPasses"],x["option"])),
        "uCausalityEstablished": False, "independentValidation": False,
        "testUsed": False, "goldUsed": False, "certified": False, "activated": False,
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--analysis", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    args = p.parse_args()
    result = audit(args.analysis)
    with args.out.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, ensure_ascii=False, allow_nan=False)
        out.write("\n")
    print(json.dumps({"schema":result["schema"],"strata":len(result["comparisons"]),
                      "uCausalityEstablished":False,"certified":False},sort_keys=True))

if __name__ == "__main__":
    main()

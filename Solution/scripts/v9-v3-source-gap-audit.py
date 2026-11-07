#!/usr/bin/env python3
"""Audit aggregated top-two Rank gaps for SAME-FRAME V2/V3 × TF0/ONE_MF.

No individual observations, candidates, IDs, names, scores or truth labels
are read or written. Histogram differences are descriptive, NOT paired
candidate-level attribution, causal proof, independent FDR or certification.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

BINS = ("NEGATIVE", "0_TO_0.25", "0.25_TO_0.5", "0.5_TO_1",
        "1_TO_2", "2_TO_4", "4_AND_ABOVE")
METHODS = ("DEFAULT", "CLASSWISE_SUPPORT_BACKOFF")
OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
SCOPE = "SYNTHETIC_TRAIN_SELECTION_RANK_ONLY_NOT_CAUSAL"


def nonnegative_integer(v, name):
    if type(v) is not int or v < 0:
        raise ValueError("Invalid aggregate count: " + name)
    return v


def histogram(value, name, complete=False, suppress=False):
    if not isinstance(value, dict):
        raise ValueError("Missing histogram: " + name)
    if complete and set(value) != set(BINS):
        raise ValueError("Missing/unknown log-odds bins: " + name)
    if suppress and any(not re.fullmatch(r"[0-8]/[0-8]", k)
                        for k in value):
        raise ValueError("Raw or invalid nominal cell bucket")
    for k, v in value.items():
        if not isinstance(k, str):
            raise ValueError("Non-string aggregate key")
        nonnegative_integer(v, name)
        if suppress and v < 5:
            raise ValueError("Small cell count was disclosed")
    return value


def check(row):
    h = row.get("sourceGapAttribution")
    if not isinstance(h, dict) or h.get("Scope") != SCOPE:
        raise ValueError("Missing TRAIN-only source Rank histogram")
    if (h.get("MinimumCellDisclosureCount") != 5
            or h.get("PairedV2V3") is not False
            or h.get("TestUsed") is not False
            or h.get("Certified") is not False):
        raise ValueError("Rank histogram changed its disclosure/certification contract")
    total = nonnegative_integer(h.get("TotalSources"), "total")
    no_best = nonnegative_integer(h.get("WithoutFirstCandidate"), "no best")
    no_second = nonnegative_integer(h.get("WithoutSecondCandidate"), "no second")
    best_yes = nonnegative_integer(h.get("BestCandidateCorrect"), "best TP")
    best_no = nonnegative_integer(h.get("BestCandidateIncorrect"), "best FP")
    second_yes = nonnegative_integer(h.get("SecondCandidateCorrect"), "second true")
    second_no = nonnegative_integer(h.get("SecondCandidateIncorrect"), "second false")
    supp_first = nonnegative_integer(h.get("SuppressedBestCellObservations"), "suppressed best")
    supp_second = nonnegative_integer(h.get("SuppressedSecondCellObservations"), "suppressed second")
    bins = histogram(h.get("BestMinusSecondLogOddsBins"), "gaps", complete=True)
    first = histogram(h.get("BestCandidateCells"), "first cells", suppress=True)
    second = histogram(h.get("SecondCandidateCells"), "second cells", suppress=True)
    if (total <= 0 or total != row.get("allSelectionObservations")
            or not (0 <= no_best <= no_second <= total)
            or best_yes + best_no + no_best != total
            or second_yes + second_no + no_second != total
            or sum(bins.values()) + no_second != total
            or sum(first.values()) + supp_first + no_best != total
            or sum(second.values()) + supp_second + no_second != total):
        raise ValueError("Rank histogram totals do not reconcile with physical source frame")
    return h


def audit(analysis_path):
    data = Path(analysis_path).read_bytes()
    report = json.loads(data)
    if (report.get("scope") != "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION"
            or report.get("testUsed") is not False
            or report.get("activated") is not False
            or report.get("evaluations") != 8
            or report.get("measured") != 8
            or len(report.get("rows", [])) != 8):
        raise ValueError("Not the frozen eight-arm SAME-FRAME experiment")
    rows = {}
    for row in report["rows"]:
        method = row.get("MuEstimationMethod")
        option = next((x for x in OPTIONS if row.get("OptionId", "").startswith(x + "__MU_")), None)
        passes = len(row.get("blockingPasses") or [])
        key = (passes, option, method)
        if (passes not in (3, 19) or option is None or method not in METHODS
                or key in rows or row.get("ConflictMargin") != .5
                or row.get("CandidatePairsSha256") is None):
            raise ValueError("Unknown/duplicated physical Rank stratum")
        check(row)
        rows[key] = row
    if len(rows) != 8:
        raise ValueError("Missing physical rank strata")
    result = []
    for passes in (3, 19):
        for option in OPTIONS:
            v2 = rows[(passes, option, METHODS[0])]
            v3 = rows[(passes, option, METHODS[1])]
            if (v2["CandidatePairsSha256"] != v3["CandidatePairsSha256"]
                    or v2["fittedPairPrior"] != v3["fittedPairPrior"]
                    or v2["allSelectionObservations"] != v3["allSelectionObservations"]):
                raise ValueError("Model rank histograms do not share SQL frame/prior/source")
            a, b = v2["sourceGapAttribution"], v3["sourceGapAttribution"]
            result.append({
                "blockingPasses": passes, "optionId": option,
                "candidatePairsSha256": v2["CandidatePairsSha256"],
                "sources": a["TotalSources"],
                "v2BestMinusSecondBins": a["BestMinusSecondLogOddsBins"],
                "v3BestMinusSecondBins": b["BestMinusSecondLogOddsBins"],
                "deltaGapBinsV3MinusV2": {
                    k: b["BestMinusSecondLogOddsBins"][k] - a["BestMinusSecondLogOddsBins"][k]
                    for k in BINS
                },
                "v2BestCells": a["BestCandidateCells"],
                "v3BestCells": b["BestCandidateCells"],
                "v2SecondCells": a["SecondCandidateCells"],
                "v3SecondCells": b["SecondCandidateCells"],
                "v2SecondCorrect": a["SecondCandidateCorrect"],
                "v3SecondCorrect": b["SecondCandidateCorrect"],
                "v2SecondMissing": a["WithoutSecondCandidate"],
                "v3SecondMissing": b["WithoutSecondCandidate"],
                "noPairedCandidateIdentifiersExported": True,
                "causalAttributionProved": False,
            })
    return {
        "schema": "V9_V3_SOURCE_GAP_AGGREGATE_SAME_FRAME_V1",
        "scope": "KNOWN_SYNTHETIC_TRAIN_SELECTION_NOT_CAUSAL",
        "analysisSha256": hashlib.sha256(data).hexdigest(),
        "sameFrameHistograms": result,
        "fullV2V3PairedSourceAttribution": False,
        "testUsed": False, "goldUsed": False,
        "statisticallyCertified": False, "activated": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--analysis", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.analysis)
    with args.out.open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2, ensure_ascii=False, allow_nan=False)
        output.write("\n")
    print(json.dumps({
        "schema": result["schema"],
        "pairedSourceAttribution": False,
        "histograms": len(result["sameFrameHistograms"]),
        "certified": False}, sort_keys=True))


if __name__ == "__main__":
    main()

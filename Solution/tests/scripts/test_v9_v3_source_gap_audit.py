"""Offline integrity and privacy tests for source Rank-gap aggregate audit."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-v3-source-gap-audit.py"
spec = importlib.util.spec_from_file_location("source_gap_audit", SCRIPT)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def gap():
    return {
        "Scope": audit.SCOPE, "MinimumCellDisclosureCount": 5,
        "TotalSources": 6, "WithoutFirstCandidate": 1,
        "WithoutSecondCandidate": 2,
        "BestMinusSecondLogOddsBins": {k: (4 if k == "0.5_TO_1" else 0) for k in audit.BINS},
        "BestCandidateCells": {"1/1": 5}, "SecondCandidateCells": {},
        "SuppressedBestCellObservations": 0, "SuppressedSecondCellObservations": 4,
        "BestCandidateCorrect": 4, "BestCandidateIncorrect": 1,
        "SecondCandidateCorrect": 1, "SecondCandidateIncorrect": 3,
        "PairedV2V3": False, "TestUsed": False, "Certified": False,
    }


def sample():
    rows = []
    for passes in (3, 19):
        for option in audit.OPTIONS:
            for method in audit.METHODS:
                rows.append({
                    "OptionId": option+"__MU_"+method,
                    "MuEstimationMethod": method,
                    "blockingPasses": [{}]*passes,
                    "ConflictMargin": .5,
                    "CandidatePairsSha256": str(passes)*64,
                    "fittedPairPrior": .5,
                    "allSelectionObservations": 6,
                    "sourceGapAttribution": gap(),
                })
    return {"scope":"TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
            "testUsed":False, "activated":False, "evaluations":8,
            "measured":8,"rows":rows}


class GapAuditTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name)/"analysis.json"
        self.data = sample()

    def check(self):
        self.path.write_text(json.dumps(self.data), encoding="utf-8")
        return audit.audit(self.path)

    def test_eight_source_histograms_reconcile_without_claiming_causality(self):
        result = self.check()
        self.assertEqual(len(result["sameFrameHistograms"]),4)
        self.assertFalse(result["fullV2V3PairedSourceAttribution"])
        self.assertFalse(result["statisticallyCertified"])
        self.assertTrue(all(not row["causalAttributionProved"]
                            for row in result["sameFrameHistograms"]))
        self.assertEqual(result["sameFrameHistograms"][0]["sources"],6)

    def test_missing_count_and_disclosure_and_nonmatching_frame_fail_closed(self):
        changes = (
            lambda d: d["rows"][0]["sourceGapAttribution"].update(TotalSources=7),
            lambda d: d["rows"][0]["sourceGapAttribution"]["BestCandidateCells"].update({"1/2":1}),
            lambda d: d["rows"][0]["sourceGapAttribution"]["BestMinusSecondLogOddsBins"].pop("2_TO_4"),
            lambda d: d["rows"][0]["sourceGapAttribution"].update(PairedV2V3=True),
            lambda d: d["rows"][0].update(CandidatePairsSha256="c"*64),
            lambda d: d["rows"][0].update(sourceGapAttribution=None),
            lambda d: d["rows"].pop(),
        )
        for mutate in changes:
            self.data = sample()
            mutate(self.data)
            with self.assertRaises(ValueError):
                self.check()


if __name__ == "__main__":
    unittest.main()

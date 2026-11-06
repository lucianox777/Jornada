"""Offline fail-closed tests for private SAME-SOURCE V2/V3 aggregate receipt."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-v3-source-paired-audit.py"
spec = importlib.util.spec_from_file_location("source_paired_audit", SCRIPT)
subject = importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


def fixture():
    rows, paired = [], []
    for passes in (3, 19):
        for option in subject.OPTIONS:
            current = []
            for method, tp in (("DEFAULT",3), ("CLASSWISE_SUPPORT_BACKOFF",2)):
                req = f"{passes}_{option}_{method}"
                row = {
                    "OptionId":option + "__MU_" + method,
                    "MuEstimationMethod":method,
                    "blockingPasses":[{}]*passes,
                    "ConflictMargin":.5, "RequestSha256":req,
                    "CandidatePairsSha256":str(passes)*64,
                    "fittedPairPrior":.5, "eligibleSources":6,
                    "allSelectionObservations":6, "trueAccepted":tp,
                    "falseAccepted":0,
                }
                rows.append(row)
                current.append(row)
            pair = {
                "Scope":"SAME_SQL_SYNTHETIC_TRAIN_SELECTION_PAIRED_SOURCE_DIAGNOSTIC",
                "SourceCount":6, "V2CorrectResolved":3, "V3CorrectResolved":2,
                "V2IncorrectResolved":0, "V3IncorrectResolved":0,
                "V2CorrectLostToV3Conflict":1,
                "V2CorrectLostToV3Unresolved":0,
                "V2CorrectLostToV3IncorrectResolved":0,
                "V3CorrectGained":0,
                "V2CorrectLostWithV3UFallback":1,
                "V2CorrectLostWithoutV3UFallback":0,
                "V2CorrectRetainedWithV3UFallback":1,
                "V2CorrectLostSameTopTwoGroups":1,
                "V2CorrectLostChangedTopCandidate":0,
                "V2CorrectLostMarginHalfCrossedWithStableTopTwo":1,
                "DifferentFirstCandidate":0, "DifferentSecondCandidate":0,
                "DifferentBlindGroupMembership":0,
                "ComparableTopTwoWithBothScores":1,
                "V3ReasonsOnV2CorrectLoss":{"MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE":1},
                "ChangeInTopTwoGapBins":{k:(1 if k=="MINUS_2_TO_MINUS_0.5" else 0)
                                          for k in subject.TRANSITION_BINS},
                "IncludesIndividualSourceData":False,
                "UFallbackCausalityEstablished":False,
                "IndependentValidation":False,
                "TestUsed":False,"Certified":False,
            }
            paired.append({
                "blockingPassCount":passes, "rootOptionId":option,
                "v2RequestSha256":current[0]["RequestSha256"],
                "v3RequestSha256":current[1]["RequestSha256"],
                "candidatePairsSha256":current[0]["CandidatePairsSha256"],
                "ConflictMargin":.5,"pair":pair,
            })
    return {
        "schema":"V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1",
        "scope":"TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
        "testUsed":False, "activated":False,
        "evaluations":8,"measured":8,"rows":rows,
        "pairedSourceRankAttribution":paired,
    }


class SourcePairedTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.file = Path(t.name) / "analysis.json"
        self.data = fixture()

    def check(self):
        self.file.write_text(json.dumps(self.data), encoding="utf-8")
        return subject.audit(self.file)

    def test_four_pair_reconciliations_never_certify_u_fallback(self):
        result = self.check()
        self.assertEqual(len(result["comparisons"]), 4)
        self.assertTrue(all(r["resolvedCorrectLosses"] == 1 for r in result["comparisons"]))
        self.assertFalse(result["uCausalityEstablished"])
        self.assertFalse(result["certified"])

    def test_missing_frame_mutated_loss_and_private_leak_rejected(self):
        changes=(
            lambda d: d["pairedSourceRankAttribution"].pop(),
            lambda d: d["pairedSourceRankAttribution"][0]["pair"].update(V2CorrectLostWithV3UFallback=0),
            lambda d: d["pairedSourceRankAttribution"][0]["pair"].update(UFallbackCausalityEstablished=True),
            lambda d: d["pairedSourceRankAttribution"][0].update(candidatePairsSha256="bad"),
            lambda d: d["rows"][0].update(trueAccepted=4),
            lambda d: d.update(SourceRankInternal={"SourceObservationId":1}),
        )
        for mutate in changes:
            self.data=fixture()
            mutate(self.data)
            with self.assertRaises(ValueError):
                self.check()


if __name__ == "__main__":
    unittest.main()

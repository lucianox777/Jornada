"""Fail-closed offline tests for the 24-arm synthetic TRAIN mixture audit."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-u-rare-mixture-physical-audit.py"
spec = importlib.util.spec_from_file_location("rare_u_physical_audit", SCRIPT)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def payload(margin=.5, weights=None):
    weights = list(audit.WEIGHTS if weights is None else weights)
    count = (2 + len(weights)) * 4
    policy = {"ClassMassCoverage": [.99], "MinimumObservedMPerCell": [5],
              "MinimumObservedUPerCell": [5], "RetainSupportedTail": [True]}
    methods = [{"Method": audit.V2},
               {"Method": audit.V3, "Parameters": dict(policy)},
               {"Method": audit.MIX, "Parameters": {
                   **policy, "UBackoffJointMixtureWeights": weights}}]
    plan = {
        "MaximumBatches": 1, "MaximumSearchConfigurations": count,
        "DuplicateCandidatePolicies": ["COUNT_ALL", "COUNT_UNIQUE"],
        "Calibration": {
            "MaximumComparedConfigurations": count, "ConflictMargins": [margin],
            "Budget": {"MaximumObservedAcceptedFdrPoint": .01},
            "SelectionPolicy": {"MaximumAcceptedFdrUpper": .01},
            "BaselineRuleSets": [[{"PassId": "frozen"}]],
            "ModelOptions": [{"OptionId": name} for name in audit.OPTIONS]
        }, "MuEstimationMethods": methods
    }
    rows = []
    for passes in (3, 19):
        for option in audit.OPTIONS:
            for method, weight in [(audit.V2, None), (audit.V3, None)] + [
                    (audit.MIX, w) for w in weights]:
                tp = 6800 if passes == 19 else (
                    (6758 if method == audit.V2 else 6306)
                    if option == audit.OPTIONS[0] else
                    (6702 if method == audit.V2 else 6771))
                # Historical V3 and explicit mixture zero are identical;
                # other lambda values can differ without asserting improvement.
                if method == audit.MIX and weight == 1:
                    tp += 2
                suffix = method if method == audit.V2 else (
                    method + "_C0.99_M5_U5_T1" +
                    ("" if weight is None else "_UJW" + format(weight, "g")))
                mu = None if method == audit.V2 else {
                    **audit.POLICY,
                    **({} if weight is None else {"UBackoffJointMixtureWeight": weight})
                }
                row = {
                    "Batch": 1, "OptionId": option + "__MU_" + suffix,
                    "MuEstimationMethod": method, "MuPolicy": mu,
                    "ConflictMargin": margin, "blockingPasses": [{}] * passes,
                    "RequestSha256": hashlib.sha256(
                        f"{passes}/{option}/{method}/{weight}".encode()).hexdigest(),
                    "CandidatePairsSha256": ("a" if passes == 3 else "b") * 64,
                    "fitSnapshotSha256": hashlib.sha256(
                        f"fit/{passes}/{option}/{method}/{weight}".encode()).hexdigest(),
                    "observedSupportAudit": {"Cells": [0] * 81},
                    "estimatorJointSupport": None if method == audit.V2
                        else {"Cells": [0] * 81},
                    "candidatePriorTrainingAudit": {
                        "PhysicalCandidatePairsSha256": ("a" if passes == 3 else "b") * 64},
                    "fittedPairPrior": .027, "trueAccepted": tp, "falseAccepted": 0,
                    "eligibleSources": 7011, "allSelectionObservations": 7011,
                    "recall": tp/7011, "fdrObserved": 0.,
                    "conditionalClusterCpFdrUpper": .004,
                    "clusterIidIndependentlyValidated": False,
                    "conflicts": 7011-tp, "unresolved": 0,
                    "resolveReasonCounts": {"RESOLVIDO": tp, "CONFLITO": 7011-tp},
                    "Rejections": [], "outcome": "MEASURED_ELIGIBLE"
                }
                rows.append(row)
    report = {
        "schema": "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1",
        "scope": "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
        "PlanSha256": "f" * 64, "testUsed": False, "activated": False,
        "originalMethodsAndParameterArrays": methods,
        "declaredVariantCount": 2 + len(weights), "evaluations": count, "measured": count,
        "duplicateCandidatePolicySelections": [
            {"Policy": name, "EligibleHypotheses": count if name == "COUNT_ALL" else count - (4 if 0 in weights else 0),
             "FrontierSnapshotShas": [], "FinalistSnapshotShas": [], "IndependentlyCertified": False}
            for name in ("COUNT_ALL", "COUNT_UNIQUE")],
        "rows": rows
    }
    envelope = {
        "result": {"PlanSha256": "f"*64, "TestUsed": False,
                   "Activated": False, "Evaluations": [{} for _ in range(count)]},
        "statisticalCertification": False, "operationalActivation": False
    }
    return plan, report, envelope


class PhysicalAuditTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.plan = root/"plan.json"
        self.report = root/"analysis.json"
        self.selection = root/"selection.json"
        self.objects = payload()

    def verify(self):
        for file, obj in zip((self.plan, self.report, self.selection), self.objects):
            file.write_text(json.dumps(obj), encoding="utf-8")
        return audit.audit(self.plan, self.report, self.selection)

    def test_all_twenty_four_are_covered_without_certification(self):
        result = self.verify()
        self.assertEqual(result["physicalComparisons"], 24)
        policies = result["duplicateCandidatePolicyComparisons"]
        self.assertEqual([p["eligibleHypotheses"] for p in policies], [24, 20])
        self.assertEqual(len(policies[1]["equivalentGroups"]), 4)
        self.assertFalse(policies[1]["statisticalSelectionCertified"])
        self.assertEqual(len(result["comparisons"]), 4)
        self.assertTrue(result["historicalLambdaZeroParity"])
        self.assertFalse(result["certified"])
        self.assertFalse(result["uFallbackCausalityEstablished"])
        self.assertEqual(result["comparisons"][0]["historicalV2"]["tp"], 6758)
        self.assertEqual(result["comparisons"][0]["historicalV3"]["tp"], 6306)
        self.assertEqual([x["lambda"] for x in result["comparisons"][0]["alternatives"]],
                         [None, 0, .25, .5, 1])
        self.assertEqual(result["sha256"]["plan"], audit.digest(self.plan))

    def test_explicit_grid_without_zero_is_audited_without_fake_parity(self):
        self.objects = payload(.3, [.1, .4, .8])
        result = self.verify()
        self.assertEqual(result["physicalComparisons"], 20)
        self.assertEqual(result["margin"], .3)
        self.assertFalse(result["historicalLambdaZeroParity"])
        self.assertFalse(result["multiplicityIncludesLambdaZeroTwice"])
        self.assertEqual([x["lambda"] for x in result["comparisons"][0]["alternatives"]],
                         [None, .1, .4, .8])

    def test_receipt_source_plan_is_in_public_aggregate_artifact(self):
        # The physical receipt hashes 'sourcePlan'. A checksum without the
        # corresponding non-private source-plan artifact cannot be verified
        # independently by the user after the GitHub job finishes.
        root = Path(__file__).resolve().parents[3]
        workflow = (root / ".github/workflows/v9-u-rare-mixture-physical.yml").read_text(
            encoding="utf-8")
        publish = workflow.split("      - name: Publish only plan, aggregate research", 1)[1]
        self.assertIn("/source-nested-plan.json", publish)
        self.assertNotIn("/results/*.selection.json", publish)
        self.assertNotIn("/people-30k/observacoes.csv", publish)

    def test_quarter_margin_is_a_separate_valid_sensitivity(self):
        self.objects = payload(.25)
        outcome = self.verify()
        self.assertEqual(outcome["margin"], .25)
        self.assertFalse(outcome["certified"])

    def test_missing_duplicated_unpaired_private_and_false_certification_rejected(self):
        modifications = (
            lambda p,r,e: r["rows"].pop(),
            lambda p,r,e: r["rows"][0].update(ConflictMargin=0),
            lambda p,r,e: r["rows"][0].update(fitSnapshotSha256="bad"),
            lambda p,r,e: r["rows"][1].update(CandidatePairsSha256="c"*64),
            lambda p,r,e: r["rows"][0].update(trueAccepted=6757),
            lambda p,r,e: r["rows"][2].update(falseAccepted=1),
            lambda p,r,e: r["rows"][3]["observedSupportAudit"]["Cells"].pop(),
            lambda p,r,e: r["rows"][2]["MuPolicy"].update(UBackoffJointMixtureWeight=.75),
            lambda p,r,e: r["rows"][3].update(OptionId="duplicated"),
            lambda p,r,e: r["rows"][0].update(SourceObservationId=112),
            lambda p,r,e: r.update(activated=True),
            lambda p,r,e: e.update(statisticalCertification=True),
            lambda p,r,e: p["Calibration"].update(MaximumComparedConfigurations=8),
            lambda p,r,e: p["MuEstimationMethods"][2]["Parameters"].update(
                UBackoffJointMixtureWeights=[0,.5,1]),
        )
        for i, mutate in enumerate(modifications):
            with self.subTest(i=i):
                self.objects = payload()
                mutate(*self.objects)
                with self.assertRaises((ValueError, KeyError, TypeError)):
                    self.verify()


if __name__ == "__main__":
    unittest.main()

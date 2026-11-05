#!/usr/bin/env python3
"""Offline tests for the seed-43 frozen-transfer analyzer and committed plan (no SQL, no TEST)."""
import copy
import importlib.util
import json
import unittest
from pathlib import Path

SOLUTION = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "v9_seed43_transfer_physical", SOLUTION / "scripts" / "v9-tf-seed43-frozen-transfer-physical.py")
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)
pre_spec = importlib.util.spec_from_file_location(
    "v9_seed43_preflight_for_plan", SOLUTION / "scripts" / "v9-tf-seed43-frozen-transfer-preflight.py")
preflight = importlib.util.module_from_spec(pre_spec)
pre_spec.loader.exec_module(preflight)
DATA = SOLUTION / "data" / "reference" / "v9-tf-seed43-frozen-transfer"


def arm(option, margin, outcomes):
    tp = sum(o[1] for o in outcomes)
    fp = sum(o[2] for o in outcomes)
    eligible = sum(o[0] for o in outcomes)
    role = ("PRIMARY_" if margin == 0.5 else "SECONDARY_") + ("CONTROL" if option == tool.CONTROL else "TREATMENT")
    return {
        "Arm": {"OptionId": option, "ConflictMargin": margin, "SnapshotSha256": "a" * 64, "Role": role},
        "ComparedConfigurationsInFamily": 2,
        "Family": "PRIMARY_PREDECLARED" if margin == 0.5 else "SECONDARY_DESCRIPTIVE",
        "Evaluation": {"SnapshotSha256": "a" * 64, "P95Milliseconds": 1234.0, "FalseDegreeP99": 3.0,
                       "SamplesMilliseconds": [1.0] * 20, "Refitted": False,
                       "Metric": {"TrueAcceptedSourceObservations": tp, "FalseAcceptedSourceObservations": fp,
                                  "EligibleSourceObservationsBeforeBlocking": eligible,
                                  "GlobalSourceRecall": tp / eligible, "BlockingSourceRecall": 0.99,
                                  "ConditionalClusterCpFdrUpper": 0.007, "HoeffdingUpperBySourceBaseIdentity": 0.05,
                                  "ClopperPearsonUpperAnyFpByIdentity": 0.002, "BonferroniComparedConfigurations": 2,
                                  "Conflicts": 1, "Unresolved": 2, "ReasonsByResolveStatus": {},
                                  "PreBlockingBaseIdentities": len(outcomes),
                                  "IdentityOutcomes": [{"BaseIdentity": f"B{i:04d}", "EligibleSources": e,
                                                        "TrueAccepted": t, "FalseAccepted": f}
                                                       for i, (e, t, f) in enumerate(outcomes)]}}}


def results(treatment_gain=True):
    control = [(2, 1, 0)] * 30 + [(2, 2, 0)] * 70
    treated = [(2, 2, 0)] * 30 + [(2, 2, 0)] * 70 if treatment_gain else control
    arms = []
    for margin in (0.5, 2, 4):
        arms += [arm(tool.CONTROL, margin, control), arm(tool.TREATMENT, margin, treated)]
    return {"Version": "V9_TF_SEED43_FROZEN_TRANSFER_RESULT_V1", "TargetSeed": 43, "Refitted": False,
            "TestUsed": False, "Certified": False, "Activated": False, "ExecutorPlanSha256": "b" * 64,
            "TargetObservationsSha256": "c" * 64, "TargetManifestSha256": "d" * 64,
            "ValidationObservations": 200, "ValidationBaseIdentities": 100, "Arms": arms}


class Seed43TransferAnalysisTests(unittest.TestCase):
    def test_paired_gain_is_identity_clustered_and_never_certifies(self):
        report = tool.analyze(results())
        primary = next(c for c in report["PairedContrasts"] if c["Family"] == "PRIMARY_PREDECLARED")
        self.assertEqual(primary["DeltaTP"], 30)
        self.assertEqual(primary["DeltaFP"], 0)
        self.assertEqual((primary["IdentitiesTreatmentBetter"], primary["IdentitiesControlBetter"]), (30, 0))
        self.assertAlmostEqual(primary["DeltaRecall"], 30 / 200)
        self.assertLess(primary["SignTestOneSidedP"], 1e-8)
        self.assertTrue(report["DescriptiveStabilityMet"])
        self.assertTrue(report["Certification"].startswith("NOT_CERTIFIED"))
        self.assertFalse(report["OperationalAuthorization"])
        control = next(a for a in report["Arms"] if a["Role"] == "PRIMARY_CONTROL")
        self.assertEqual((control["TP"], control["FN"], control["FragmentedIdentities"]), (170, 30, 30))
        self.assertAlmostEqual(control["FragmentationRate"], 0.30)
        self.assertEqual(control["FusedIdentities"], 0)
        self.assertIn("ΔTP", tool.markdown(report))

    def test_no_gain_is_reported_as_not_met(self):
        report = tool.analyze(results(treatment_gain=False))
        self.assertFalse(report["DescriptiveStabilityMet"])
        self.assertFalse(report["InferentialGainEstablished"])

    def test_rejects_mismatched_arms_or_contract(self):
        bad = results()
        bad["Arms"][1]["Evaluation"]["Metric"]["IdentityOutcomes"].pop()
        with self.assertRaisesRegex(ValueError, "same identities"):
            tool.analyze(bad)
        for key, value in (("Refitted", True), ("TestUsed", True), ("Certified", True), ("TargetSeed", 42)):
            bad = results()
            bad[key] = value
            with self.assertRaises(ValueError):
                tool.analyze(bad)
        bad = copy.deepcopy(results())
        bad["Arms"] = bad["Arms"][:5]
        with self.assertRaises(ValueError):
            tool.analyze(bad)

    def test_sign_test_tail_is_exact(self):
        self.assertAlmostEqual(tool._binomial_tail_ge(3, 3), 0.125)
        self.assertAlmostEqual(tool._binomial_tail_ge(0, 5), 1.0)
        self.assertAlmostEqual(tool._binomial_tail_ge(2, 3), 0.5)


class TransferWorkflowTests(unittest.TestCase):
    def test_dotnet_run_receives_only_absolute_paths(self):
        workflow = (SOLUTION.parent / ".github" / "workflows" / "v9-tf-seed43-frozen-transfer.yml").read_text(encoding="utf-8")
        block = workflow[workflow.index("--v9-frozen-transfer"):workflow.index('"$EVIDENCE/transfer"') + 20]
        for line in block.splitlines()[:4]:
            for token in line.replace("\\", " ").split():
                if token.startswith("--"):
                    continue
                self.assertTrue(token.startswith('"$PWD/') or token.startswith('"$GITHUB_WORKSPACE/') or token.startswith('"$SOURCE') or token.startswith('"$EVIDENCE'), token)


class CommittedPlanTests(unittest.TestCase):
    def test_committed_preflight_plan_matches_executor_primary_arms(self):
        plan = json.loads((DATA / "frozen-transfer-plan.json").read_bytes())
        executor = json.loads((DATA / "executor.json").read_bytes())
        primary = {a["OptionId"]: a["SnapshotSha256"] for a in executor["Arms"] if a["Role"].startswith("PRIMARY_")}
        self.assertEqual({m["option_id"]: m["snapshot_sha256"] for m in plan["frozen_models"]}, primary)
        self.assertEqual(plan["source_run_id"], executor["SourceRunId"])
        self.assertEqual(plan["source_commit_sha"], executor["SourceCommitSha"])
        self.assertEqual(plan["source_study_sha256"], executor["SourceStudySha256"])
        # The committed plan satisfies the frozen preflight contract (snapshot bytes stubbed).
        import tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as tmp:
            for value in primary.values():
                (Path(tmp) / (value + ".fitted.json")).write_text("{}", encoding="utf-8")
            with patch.object(preflight, "sha256", side_effect=lambda p: Path(p).name.split(".")[0]):
                preflight.check_plan(plan, Path(tmp))

    def test_executor_is_frozen_source_registry_not_seed43_corpus(self):
        executor = json.loads((DATA / "executor.json").read_bytes())
        registry = json.loads((SOLUTION / "data" / "reference" / "v9-synthetic-seed42-30000" / "manifest.json").read_bytes())
        self.assertEqual(executor["SourceSeed"], registry["seed"])
        self.assertEqual(executor["TargetSeed"], 43)
        self.assertEqual(len({a["SnapshotSha256"] for a in executor["Arms"]}), 6)
        # The frozen source observations are exactly the registered seed-42 bytes.
        registered = {f["file"]: f["sha256"].lower() for f in registry["generated_files"]}
        self.assertEqual(executor["SourceObservationsSha256"], registered["observacoes.csv"])

class AggregateStatAuditCITests(unittest.TestCase):
    """Exercise the standalone aggregate audit in the existing CI test job."""

    def test_aggregate_audit_self_test(self):
        import subprocess
        import sys
        script = SOLUTION / "scripts" / "v9-tf-seed43-aggregate-stat-audit.py"
        result = subprocess.run(
            [sys.executable, str(script), "--self-test"],
            capture_output=True, text=True, timeout=30, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

if __name__ == "__main__":
    unittest.main()

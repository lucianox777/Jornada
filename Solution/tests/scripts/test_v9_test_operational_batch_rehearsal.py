"""GOV-DOC-01 regression: V9 read-only candidate batching is not a PROD batch certification."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "src/Jornada.Linkage.Runner"
CORE = ROOT / "src/Jornada.Linkage.Core"
WORKER = ROOT / "src/Jornada.Linkage.Parameters.Worker"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
SMOKE = ROOT.parent / ".github/workflows/v9-operational-model-sql-smoke.yml"
CI = ROOT.parent / ".github/workflows/jornada-pr-required-fast.yml"


class V9NonpublishingBatchRehearsalContract(unittest.TestCase):
    def test_production_sql_retrieval_is_batched_but_resolve_is_global(self):
        src = (RUNNER / "V9OperationalCandidateGraphParityAudit.cs").read_text()
        self.assertIn("int batchSize = MaximumAuditObservations", src)
        self.assertIn("ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1)", src)
        self.assertIn("ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, MaximumAuditObservations)", src)
        self.assertIn("for (var offset = 0; offset < ordered.Length; offset += batchSize)", src)
        self.assertIn("ordered.Skip(offset).Take(batchSize)", src)
        self.assertIn("planner.BuildCandidateUuidQuery(", src)
        self.assertIn("if (observationsRead != ordered.Length", src)
        self.assertIn("actualDirections.Values.Any(v => v != 3)", src)
        self.assertIn("expected.SetEquals(actual)", src)
        self.assertIn("var operational = operationalRuntime.ReplayTest(test,", src)
        self.assertIn("var reference = operationalRuntime.ReplayTest(test,", src)
        self.assertIn("reference.Decisions.SequenceEqual(operational.Decisions)", src)
        self.assertIn("reference.IdentityByObservation.Any(", src)
        self.assertIn("DecisionsSha256: decisionsSha, IdentityGroupsSha256: groupsSha", src)
        self.assertIn("ProductionBatchPipelineCertified: false", src)
        self.assertNotIn("INSERT INTO ", src)
        self.assertNotIn("UPDATE ", src)
        self.assertNotIn("DELETE FROM ", src)
        self.assertIn('"JornadaHarness"', src)
        self.assertNotIn("ProbabilisticLinkageBatchRunner.RunAsync(", src)
        entry = (RUNNER / "V9ReadOnlyTestBatchRunner.cs").read_text()
        self.assertIn('DB_NAME()', entry)
        self.assertIn('"JornadaHarness"', entry)
        self.assertLess(entry.index("DB_NAME()"), entry.index("V9OperationalPinnedCohortRuntime.LoadPinnedAsync("))
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", entry)
        self.assertIn("V9OperationalCandidateGraphParityAudit.CompareTestAsync(", entry)
        self.assertIn("receipt.ProductionBatchPipelineCertified", entry)
        self.assertNotIn("INSERT INTO ", entry)
        self.assertNotIn("UPDATE ", entry)
        self.assertNotIn("DELETE FROM ", entry)
        self.assertNotIn("ProbabilisticLinkageBatchRunner.RunAsync(", entry)

    def test_sql_smoke_proves_four_batch_sizes_and_rejects_projection_drift(self):
        src = (ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs").read_text()
        self.assertIn("foreach (var size in new[] { 1, 2, 3, 4 })", src)
        self.assertIn("cohort.Reverse().ToArray()", src)
        self.assertIn("V9ReadOnlyTestBatchRunner.RunAsync(", src)
        self.assertIn("batched.DecisionsSha256, Is.EqualTo(parity.DecisionsSha256)", src)
        self.assertIn("batched.IdentityGroupsSha256, Is.EqualTo(parity.IdentityGroupsSha256)", src)
        self.assertIn("batchSize: 0", src)
        self.assertIn("batchSize: 129", src)
        self.assertIn("projection_fingerprint_sha256=@bad", src)
        self.assertIn("FullyQualifiedName~V9FittedSnapshotTests.Operational_SQL_roundtrip_", SMOKE.read_text())
        self.assertIn("test_v9_test_operational_batch_rehearsal.py", CI.read_text())

    def test_test_admission_remains_closed_and_train_validation_unchanged(self):
        workflow = WF.read_text()
        prep = workflow.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        train = workflow.split("\n  physical-100k:\n", 1)[1].split("\n  validation-100k:\n", 1)[0]
        assert_gate = "V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"
        self.assertIn(assert_gate, prep)
        self.assertIn(assert_gate, train)
        self.assertIn("timeout-minutes: 340", train)
        self.assertIn("timeout-minutes: 360", train)
        self.assertIn("timeout-minutes: 330", workflow)
        self.assertIn("V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges)",
                      (WORKER / "V9PhysicalSqlFrozenHoldout.cs").read_text())
        self.assertIn('public V9FrozenSourceReplayResult ReplayTest(',
                      (CORE / "V9OperationalPinnedCohortRuntime.cs").read_text())


if __name__ == "__main__":
    unittest.main()

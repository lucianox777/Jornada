"""Guard TEST-only, read-only actual operational SQL candidate graph parity.

Source contract is NOT a substitute for SQL smoke, live 100k or full batch parity.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PARITY = ROOT / "src/Jornada.Linkage.Runner/V9OperationalCandidateGraphParityAudit.cs"
CORE_REPLAY = ROOT / "src/Jornada.Linkage.Core/V9FrozenSourceResolveReplay.cs"
HOLDOUT = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs"
TESTS = ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs"
SMOKE = ROOT.parent / ".github/workflows/v9-operational-model-sql-smoke.yml"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class OperationalCandidateParity(unittest.TestCase):
    def test_provider_query_reuses_production_builder_and_requires_full_bidirectional_graph(self):
        code = PARITY.read_text()
        self.assertIn("V9FittedSnapshotExecutionRuntime.Load(", code)
        self.assertIn("planner.BuildCandidateUuidQuery(", code)
        self.assertIn("command.ExecuteReaderAsync(ct)", code)
        self.assertIn('DB_NAME()', code)
        self.assertIn('"JornadaHarness"', code)
        self.assertIn("MaximumAuditObservations = V9OperationalPinnedCohortRuntime.MaximumAuditObservations", code)
        self.assertIn("row.Partition != \"TEST\"", code)
        self.assertIn("expected.SetEquals(actual)", code)
        self.assertIn("actualDirections.Values.Any(v => v != 3)", code)
        self.assertIn("V9FrozenSourceResolveReplay.ObservationGuid(", code)
        self.assertIn("ProductionBatchPipelineCertified: false", code)
        self.assertNotIn("INSERT INTO ", code)
        self.assertNotIn("UPDATE ", code)
        self.assertNotIn("DELETE FROM ", code)
        self.assertIn("public static Guid ObservationGuid(", CORE_REPLAY.read_text())

    def test_reference_graph_is_existing_worker_sql_not_synthetic_faked_edges(self):
        holdout = HOLDOUT.read_text()
        self.assertIn("ReadFrozenTestCandidateEdgesForParityAsync(", holdout)
        self.assertIn('PrepareFrozenValidationPairsAsync(\n            connection, test, winner, execution, "TEST"', holdout)
        self.assertIn("await RequireHarnessAsync(connection, cancellationToken)", holdout)
        self.assertIn("if (pairCount != edges.Count)", holdout)
        self.assertIn("V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges)", holdout)
        self.assertIn("operationalRuntime!.ReplayTest(validation, edges)", holdout)

    def test_physical_sql_smoke_proves_edges_and_negative_drift(self):
        fixture = TESTS.read_text()
        smoke = SMOKE.read_text()
        self.assertIn("V9CalibrationBlockingProjection.ComputeSchemaFingerprint(4)", fixture)
        self.assertIn("CREATE TABLE identidade.blocking_chave(", fixture)
        self.assertIn("ReadFrozenTestCandidateEdgesForParityAsync(", fixture)
        self.assertIn("V9OperationalCandidateGraphParityAudit.CompareTestAsync(", fixture)
        self.assertIn("projection_fingerprint_sha256=@bad", fixture)
        self.assertIn("ProductionBatchPipelineCertified, Is.False", fixture)
        self.assertIn("FullyQualifiedName~V9FittedSnapshotTests.Operational_SQL_roundtrip_", smoke)
        self.assertIn("JORNADA_V9_SQL_MODEL_SMOKE_CONNECTION=", smoke)
        self.assertIn("one-winner-sql-smoke:", smoke)

    def test_no_workflow_test_opening_or_training_validation_changes(self):
        workflow = WF.read_text()
        prep = workflow.split("\n  prepare-test-100k:\n",1)[1].split("\n  physical-100k:\n",1)[0]
        train = workflow.split("\n  physical-100k:\n",1)[1].split("\n  validation-100k:\n",1)[0]
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", prep)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", train)
        self.assertIn("timeout-minutes: 340", train)
        self.assertIn("timeout-minutes: 360", train)
        self.assertIn("timeout-minutes: 330", workflow)
        self.assertNotIn("ReadFrozenTestCandidateEdgesForParityAsync", train)


if __name__ == "__main__":
    unittest.main()

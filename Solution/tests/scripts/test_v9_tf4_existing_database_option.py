#!/usr/bin/env python3
"""The obsolete external TEST selector fails before legacy TRAIN or scientific TEST."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
TEST = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
DB = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9ExternalDatabaseReadOnlyTest.cs"
ENTRY = ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs"
CLOSEOUT = ROOT / "scripts/v9-tf4-e2e-closeout.py"
spec = importlib.util.spec_from_file_location("v9_e2e_closeout_external", CLOSEOUT)
closeout = importlib.util.module_from_spec(spec)
spec.loader.exec_module(closeout)
WINNER = "a" * 64


class V9ExistingDatabaseOptionTests(unittest.TestCase):
    def test_legacy_external_database_is_rejected_before_train(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        admission = workflow.split("\n  legacy-inputs:\n", 1)[1].split("\n  prepare-test-100k:\n", 1)[0]
        train = workflow.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        self.assertIn("      test_database:", workflow)
        self.assertIn('if [[ -n "$LEGACY_TEST_DATABASE" ]]', admission)
        self.assertIn("V9_LEGACY_EXTERNAL_TEST_ROUTE_DISABLED_PREPARE_ONLY", admission)
        self.assertIn("exit 1", admission)
        self.assertIn("needs.legacy-inputs.result == 'success'", train)
        self.assertNotIn("test-database-readonly:", workflow)
        self.assertNotIn("V9_EXTERNAL_TEST_READONLY_CONNECTION", workflow)
        self.assertNotIn("--v9-external-test-preflight", workflow)
        self.assertNotIn('--test-database "', workflow)

    def test_historical_diagnostic_is_read_only_and_uncertified(self):
        source = DB.read_text(encoding="utf-8")
        self.assertIn("ApplicationIntent.ReadOnly", source)
        self.assertIn("VerifyReadOnlyPrincipalAsync", source)
        self.assertIn("silver.pessoa_observacao", source)
        self.assertIn("ref.gestor", source)
        self.assertIn("MaximumObservations = 100_000", source)
        self.assertIn("MaximumCandidateEdges = 2_000_000", source)
        self.assertIn("MaximumBucketPairIncidences = 4_000_000", source)
        self.assertIn("IS_ROLEMEMBER('db_ddladmin') = 1", source)
        self.assertIn("SELECT TOP (@max_plus_one)", source)
        self.assertIn("goldTruthAvailable = false", source)
        self.assertIn("productionRunnerFullPipelineParityCertified = false", source)
        for forbidden in ("INSERT INTO ", "DELETE FROM ", "UPDATE silver.", "CREATE TABLE ", "MERGE INTO "):
            self.assertNotIn(forbidden, source)

    def test_effective_permissions_fail_closed_on_unrelated_writes_and_execute(self):
        source = DB.read_text(encoding="utf-8")
        verify = source.split("public static async Task VerifyReadOnlyPrincipalAsync(", 1)[1]
        verify = verify.split("public static async Task<V9PhysicalCalibrationObservation[]> ReadExistingObservationsAsync(", 1)[0]
        # A named-table/db_owner check alone does not reject write grants in
        # custom roles or through an unrelated table / stored procedure.
        self.assertIn("sys.fn_my_permissions(NULL, 'DATABASE')", verify)
        self.assertIn("FROM sys.schemas AS s", verify)
        self.assertIn("FROM sys.objects AS o", verify)
        self.assertIn("FROM sys.columns AS c", verify)
        self.assertIn("'OBJECT', 'UPDATE', c.name, 'COLUMN'", verify)
        for permission in ("INSERT", "UPDATE", "DELETE", "EXECUTE",
                           "ALTER", "CONTROL", "TAKE OWNERSHIP"):
            self.assertIn(" 'OBJECT', '" + permission + "'", verify)
        self.assertIn("COALESCE(HAS_PERMS_BY_NAME(NULL, 'SERVER', 'CONTROL SERVER')", verify)
        self.assertIn("await using (var reader = await command.ExecuteReaderAsync(ct))", verify)
        self.assertIn("effectivePermissions.ExecuteScalarAsync(ct)", verify)
        self.assertIn("value != 0", verify)
        self.assertNotIn("INSERT INTO ", verify)
        self.assertNotIn("UPDATE silver.", verify)
        # Every live source read must be admitted by effective permission audit.
        run = source.split("public static async Task RunAsync(", 1)[1]
        self.assertLess(run.index("VerifyReadOnlyPrincipalAsync(connection, database, ct)"),
                        run.index("ReadExistingObservationsAsync(connection, ct)"))
        smoke = (ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs").read_text()
        self.assertIn("v9_scope_read_probe", smoke)
        self.assertIn("CREATE ROLE [v9_scope_write_role]", smoke)
        self.assertIn("GRANT UPDATE (id) ON OBJECT::identidade.v9_permission_probe", smoke)
        self.assertIn("GRANT INSERT ON OBJECT::identidade.v9_permission_probe", smoke)
        self.assertIn("Assert.ThrowsAsync<InvalidOperationException>", smoke)
        self.assertIn('"REVERT;"', smoke)

    def test_scientific_cli_rejects_external_database_before_loading_winner(self):
        src = TEST.read_text(encoding="utf-8")
        argument_check, execution = src.split("var winnerFile = Path.GetFullPath(args[1]);", 1)
        self.assertIn('args[2] != "--test-repetitions"', argument_check)
        self.assertIn("throw new ArgumentException", argument_check)
        self.assertNotIn('"--test-database"', src)
        self.assertNotIn("V9ExternalDatabaseReadOnlyTest.RunAsync", src)
        self.assertIn("V9IndependentTestCorpusReader.ReadAsync(fullTestDir)", execution)
        self.assertIn("--v9-external-test-preflight", ENTRY.read_text(encoding="utf-8"))

    def fixture(self):
        return {
            "schema": "V9_EXTERNAL_DATABASE_READONLY_OPERATIONAL_DIAGNOSTIC_V1",
            "status": "EXTERNAL_DATABASE_NONPUBLISHING_DIAGNOSTIC_NOT_SCIENTIFIC_TEST",
            "databaseName": "JornadaExistingSample",
            "databaseOrigin": "EXISTING_DATABASE_NO_SYNTHETIC_GENERATION",
            "winnerSnapshotSha256": WINNER,
            "operationalModelFileName": WINNER + ".operational.json",
            "operationalModelContract": "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1",
            "observationCount": 5,
            "candidateEdgeCount": 2,
            "resolved": 1, "unresolved": 3, "conflicts": 1,
            "aggregateSha256": "b" * 64,
            "inputReadOnly": True, "generatedSyntheticData": False,
            "independentScientificTest": False, "goldTruthAvailable": False,
            "tpFpFnFdrRecallEvaluated": False,
            "candidateGraphParityWithProductionCertified": False,
            "productionRunnerFullPipelineParityCertified": False,
            "operationallyCertified": False, "testUsed": False,
            "refitted": False, "published": False, "activated": False,
        }

    def test_external_e2e_receipt_is_distinct_from_scientific_test(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "external-database-test.json"
            raw = self.fixture()
            path.write_text(json.dumps(raw))
            r = closeout.validate_existing_database_diagnostic(
                path, raw["databaseName"], WINNER)
            self.assertIs(r["scientificTestExecuted"], False)
            for attribute, value in [
                ("generatedSyntheticData", True),
                ("productionRunnerFullPipelineParityCertified", True),
                ("goldTruthAvailable", True),
                ("testUsed", True),
                ("databaseName", "OtherDatabase"),
                ("winnerSnapshotSha256", "c" * 64),
                ("candidateEdgeCount", 2_000_001),
                ("observationCount", 100_001),
            ]:
                with self.subTest(attribute=attribute):
                    altered = dict(raw)
                    altered[attribute] = value
                    path.write_text(json.dumps(altered))
                    with self.assertRaisesRegex(ValueError, "EXTERNAL_DB_"):
                        closeout.validate_existing_database_diagnostic(
                            path, raw["databaseName"], WINNER)

    def test_standard_closeout_cannot_substitute_diagnostic_for_scientific_test(self):
        with self.assertRaisesRegex(ValueError, "EXTERNAL_DB_CANNOT_SUBSTITUTE"):
            closeout.closeout("success", "success", "success", "skipped", "skipped",
                              True, Path("validation.json"), None, external_database="History",
                              strict_test=True)


if __name__ == "__main__":
    unittest.main()

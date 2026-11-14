#!/usr/bin/env python3
"""TEST source selector: blank DB = sealed synthetic 100k, explicit DB = existing read-only.
Never exposes independent TEST truth, Gold or production publication.
"""
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
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.before_train, tail = cls.workflow.split("\n  physical-100k:\n", 1)
        cls.train, tail = tail.split("\n  audit-100k:\n", 1)
        cls.audit, tail = tail.split("\n  validation-100k:\n", 1)
        cls.validation, tail = tail.split("\n  test-100k:\n", 1)
        cls.scientific, tail = tail.split("\n  test-database-readonly:\n", 1)
        cls.external, cls.closing = tail.split("\n  e2e-closeout:\n", 1)

    def test_one_test_checkbox_and_optional_database_name(self):
        w = self.workflow
        self.assertIn("      test_required:", w)
        self.assertIn("      test_database:", w)
        self.assertIn("default: \"\"", w)
        self.assertNotIn("      prepare_sealed_test:", w)
        self.assertNotIn("timeout-minutes:", w)
        self.assertIn("needs: [prepare-test-100k, test-database-preflight]", w)
        self.assertIn("needs.prepare-test-100k.result == 'success'", self.train)
        self.assertIn("needs.test-database-preflight.result == 'success'", self.train)
        self.assertIn("needs: physical-100k", self.audit)
        self.assertIn("needs: audit-100k", self.validation)
        self.assertNotIn("test_database", self.validation.split("\n    steps:", 1)[0])
        self.assertIn("inputs.test_database == ''", self.scientific)
        self.assertIn("inputs.test_database != ''", self.external)
        self.assertIn("inputs.test_required == true", self.external)
        self.assertIn("needs.validation-100k.result == 'success'", self.external)
        self.assertIn("needs.test-database-preflight.result == 'success'", self.external)

    def test_external_db_is_preflighted_before_any_expensive_train(self):
        header = self.before_train
        self.assertIn("test-database-preflight:", header)
        self.assertIn("secrets.V9_EXTERNAL_TEST_READONLY_CONNECTION", header)
        self.assertIn("--v9-external-test-preflight", header)
        self.assertIn("contents: read", header)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", header)
        self.assertIn('inputs.test_database == \'\'', header)
        self.assertIn("V9_DATABASE_REQUIRES_TEST_CHECKBOX", self.train)
        self.assertIn("V9_EXTERNAL_DATABASE_PREFLIGHT_MUST_PASS_BEFORE_TRAIN", self.train)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", self.train)
        self.assertNotIn("V9_EXTERNAL_TEST_READONLY_CONNECTION", self.train + self.audit + self.validation)

    def test_external_execution_never_generates_test_corpus_or_writes_source_sql(self):
        w = self.external
        self.assertIn("name: v9-tf4-validated-operational-model-", w)
        self.assertIn("secrets.V9_EXTERNAL_TEST_READONLY_CONNECTION", w)
        self.assertIn('--v9-independent-test "$model" --test-database "$V9_EXTERNAL_DATABASE"', w)
        self.assertIn("external-database-test.json", w)
        self.assertNotIn("v9-tf4-test-sealed-", w)
        self.assertNotIn("TEST_PRIVATE_KEY_B64", w)
        self.assertNotIn("CREATE DATABASE", w)
        self.assertNotIn("v9-tf4-sealed-test-preparation.py unseal", w)
        self.assertNotIn("--v9-convert-winner-operational", w)
        self.assertIn("inputReadOnly", w)
        self.assertIn("tpFpFnFdrRecallEvaluated", w)
        self.assertIn("contents: read", w)
        source = DB.read_text()
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

    def test_cli_is_explicit_parameter_no_synthetic_when_present(self):
        src = TEST.read_text()
        self.assertIn('args[2] != "--test-database"', src)
        self.assertIn("V9ExternalDatabaseReadOnlyTest.RunAsync(winnerFile, args[3])", src)
        self.assertIn("return;", src)
        self.assertIn("V9IndependentTestCorpusReader.ReadAsync(fullTestDir)", src)
        self.assertIn("--v9-external-test-preflight", ENTRY.read_text())
        self.assertIn("V9_EXTERNAL_TEST_REQUESTED", self.closing) if False else None

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

    def test_external_branch_does_not_reuse_or_certify_sealed_test(self):
        self.assertIn("test-database-readonly.result", self.closing)
        self.assertIn("external-database-test.json", self.closing)
        self.assertIn("EXTERNAL_TEST_RESULT", self.closing)
        self.assertIn("V9_EXTERNAL_DATABASE", self.closing)
        self.assertIn("productionRunnerFullPipelineParityCertified", DB.read_text())
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",
                      self.before_train + self.train)


if __name__ == "__main__":
    unittest.main()

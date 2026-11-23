"""Combined DEV evidence tests: actual ZIP fixture bytes, simulated SQL/HTTP.

Never equates these 1001 offline observations to actual 1k Runner execution.
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


COMPOSITE = load("v9_dev_composite", ROOT / "scripts/v9-e2e-dev-composite-reconcile.py")
FIXTURES = load("v9_run_http_fixture", ROOT / "tests/scripts/test_v9_e2e_run_http_receipt_compare.py")
COMMIT = FIXTURES.COMMIT
WINNER = FIXTURES.WINNER


class DevCompositeReceiptsTests(unittest.TestCase):
    def setUp(self):
        self.fixture = FIXTURES.RunHttpReceiptComparisonTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.fixture.root
        self.stage = self.fixture.fixture.stage
        self.stage.write_text(json.dumps(self.fixture.stage) + "\n", encoding="utf-8")
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        directory = Path(self.tmp.name)
        self.http = directory / "http.json"
        self.sql = directory / "sql-ledger.json"
        self.http.write_text(json.dumps(self.fixture.http) + "\n", encoding="utf-8")
        self.sql.write_text(json.dumps(self.fixture.sql) + "\n", encoding="utf-8")

    def evaluate(self):
        return COMPOSITE.combine(
            self.root, self.http, self.sql, self.stage, COMMIT, WINNER)

    def test_real_zip_bridge_truth_and_mock_run_receipts_match_without_certifying(self):
        receipt = self.evaluate()
        self.assertEqual(receipt["schema"], COMPOSITE.SCHEMA)
        self.assertEqual(
            receipt["status"], "CONSISTENT_RECEIPTS_OFFLINE_NOT_AUTHENTICATED")
        self.assertEqual(receipt["sourceObservations"], 1001)
        self.assertEqual(receipt["baseIdentities"], 1000)
        self.assertEqual(receipt["zipPackagesVerified"], 4)
        self.assertEqual(receipt["sqlHttpDeliveryReceiptsMatched"], 4)
        self.assertEqual(receipt["groupingPairCounts"]["truePositive"], 1)
        self.assertEqual(receipt["groupingPairCounts"]["falsePositive"], 0)
        self.assertEqual(receipt["groupingPairCounts"]["falseNegative"], 0)
        self.assertTrue(receipt["zipBridgeAndIndependentOracleChecked"])
        self.assertTrue(receipt["sqlHttpRunReceiptFieldsChecked"])
        for key in ("sourceSqlRuntimeAuthenticatedExternally",
                    "httpRunExecutionAuthenticatedExternally",
                    "goldIdentityStatesAvailable", "scientificValidation",
                    "operationalParityCertified", "activated",
                    "E2E_NUCLEO_PASS", "E2E_INTEGRAL_PASS"):
            self.assertFalse(receipt[key])
        encoded = json.dumps(receipt)
        for prohibited in ("SYNTH-", "BasePersonId", "OpaquePersonId",
                           "groupId", "cpf", "idPessoaEntrega",
                           "codigoPessoaOrigem", "bridge-truth.jsonl"):
            self.assertNotIn(prohibited, encoded)

    def test_composite_rejects_mutated_staging_run_sql_or_http_claims(self):
        original = json.loads(self.stage.read_text(encoding="utf-8"))
        original_sql = json.loads(self.sql.read_text(encoding="utf-8"))
        original_http = json.loads(self.http.read_text(encoding="utf-8"))
        for mutation in ("model", "run", "source", "fake_gold",
                         "false_sql", "delivery_id", "missing_package",
                         "missing_sql_bronze", "falsely_http_proven",
                         "http_source", "one_staging_row_removed"):
            with self.subTest(mutation=mutation):
                stage = json.loads(json.dumps(original))
                sql = json.loads(json.dumps(original_sql))
                http = json.loads(json.dumps(original_http))
                if mutation == "model":
                    stage["modelWinnerSha256"] = "0" * 64
                elif mutation == "run":
                    sql["runId"] = str(uuid.UUID(int=8))
                elif mutation == "source":
                    sql["sourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "fake_gold":
                    stage["rows"][0]["identityState"] = "REFERENCIA"
                elif mutation == "false_sql":
                    sql["E2E_NUCLEO_PASS"] = True
                elif mutation == "delivery_id":
                    sql["deliveryProofs"][0]["deliveryIdSha256"] = "0" * 64
                elif mutation == "missing_package":
                    http["deliveryProofs"].pop()
                elif mutation == "missing_sql_bronze":
                    sql["deliveryProofs"][0]["sqlPayloadMetadataVerified"] = False
                elif mutation == "falsely_http_proven":
                    sql["httpDeliveryProven"] = True
                elif mutation == "http_source":
                    http["deliveryProofs"][0]["silverSourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "one_staging_row_removed":
                    stage["rows"].pop()
                self.stage.write_text(json.dumps(stage) + "\n", encoding="utf-8")
                self.sql.write_text(json.dumps(sql) + "\n", encoding="utf-8")
                self.http.write_text(json.dumps(http) + "\n", encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.evaluate()
        self.stage.write_text(json.dumps(original) + "\n", encoding="utf-8")
        self.sql.write_text(json.dumps(original_sql) + "\n", encoding="utf-8")
        self.http.write_text(json.dumps(original_http) + "\n", encoding="utf-8")
        self.assertEqual(self.evaluate()["sqlHttpDeliveryReceiptsMatched"], 4)

    def test_composite_rejects_inconsistent_pair_rates(self):
        original_reconcile = COMPOSITE.BRIDGE.reconcile
        baseline = original_reconcile(self.root, self.stage, COMMIT, WINNER)
        for mutation in ("wrong_recall", "wrong_fdr", "null_fdr",
                         "nonfinite_recall", "boolean_fdr", "no_true_pairs"):
            with self.subTest(mutation=mutation):
                result = json.loads(json.dumps(baseline))
                pairs = result["sourceGroupPairs"]
                if mutation == "wrong_recall":
                    pairs["recallPercent"] = 0
                elif mutation == "wrong_fdr":
                    pairs["observedFdrPercent"] = 25
                elif mutation == "null_fdr":
                    pairs["observedFdrPercent"] = None
                elif mutation == "nonfinite_recall":
                    pairs["recallPercent"] = float("nan")
                elif mutation == "boolean_fdr":
                    pairs["observedFdrPercent"] = False
                else:
                    pairs["truePositive"] = 0
                    pairs["falseNegative"] = 0
                    pairs["recallPercent"] = 0
                    pairs["observedFdrPercent"] = None
                with patch.object(COMPOSITE.BRIDGE, "reconcile",
                                  return_value=result):
                    with self.assertRaisesRegex(ValueError, "pair"):
                        self.evaluate()

    def test_composite_requires_independent_receipt_file_owners(self):
        with self.assertRaisesRegex(ValueError, "distinct physical files"):
            COMPOSITE.combine(self.root, self.http, self.http, self.stage,
                              COMMIT, WINNER)
        with self.assertRaisesRegex(ValueError, "outside"):
            COMPOSITE.combine(self.root, self.root / "ingestion/bridge-manifest.json",
                              self.sql, self.stage, COMMIT, WINNER)

    def test_composite_rejects_hardlink_aliases_of_receipts(self):
        alias = Path(self.tmp.name) / "sql-hardlink.json"
        try:
            alias.hardlink_to(self.http)
        except (OSError, NotImplementedError) as exc:
            self.skipTest(f"hardlinks unavailable in test filesystem: {exc}")
        with self.assertRaisesRegex(ValueError, "distinct physical files"):
            COMPOSITE.combine(self.root, self.http, alias, self.stage,
                              COMMIT, WINNER)

    def test_exclusive_aggregate_receipt_without_private_inputs(self):
        dest = Path(self.tmp.name) / "composite.json"
        args = ["composite", "--ibge-root", str(self.root),
                "--http", str(self.http), "--sql-ledger", str(self.sql),
                "--staging", str(self.stage), "--expected-sha", COMMIT,
                "--expected-winner", WINNER, "--receipt", str(dest)]
        with patch("sys.argv", args):
            COMPOSITE.main()
        out = json.loads(dest.read_text(encoding="utf-8"))
        self.assertEqual(out["status"],
                         "CONSISTENT_RECEIPTS_OFFLINE_NOT_AUTHENTICATED")
        with patch("sys.argv", args):
            with self.assertRaisesRegex(ValueError, "overwrite"):
                COMPOSITE.main()


if __name__ == "__main__":
    unittest.main()

"""Offline-only run / HTTP receipt comparison. No actual 1k V9 RunAsync."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[2]


def imported(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


COMPARE = imported("run_http_compare", ROOT / "scripts/v9-e2e-run-http-receipt-compare.py")
FIXTURE = imported("bridge_fixture_existing",
                   ROOT / "tests/scripts/test_e2e_v9_bridge_staging_reconcile.py")
COMMIT = "c" * 40
WINNER = "A" * 64


class RunHttpReceiptComparisonTests(unittest.TestCase):
    def setUp(self):
        fixture = FIXTURE.BridgeGroupingReconciliationTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.fixture = fixture
        self.pre = COMPARE.LINEAGE.load(fixture.root / "ingestion/bridge-manifest.json")
        # Only the canonical ZIP bridge preflight is used to build a synthetic
        # HTTP V2 receipt; no HTTP request or SQL execution occurs here.
        self.pre = FIXTURE.JOIN.PREFLIGHT.verify(fixture.root)
        stage = json.loads(fixture.stage.read_text(encoding="utf-8"))
        stage["modelWinnerSha256"] = WINNER
        self.stage = stage
        bridge = json.loads((fixture.ingestion / "bridge-manifest.json").read_text())
        self.proofs = []
        for i, manager in enumerate(COMPARE.LINEAGE.MANAGERS):
            package = next(p for p in bridge["packages"]
                           if p["GestorCodigo"] == manager)
            self.proofs.append({
                "gestor": manager,
                "deliveryIdSha256": hashlib.sha256(
                    str(uuid.UUID(int=i + 1)).encode("ascii")).hexdigest().upper(),
                "packageSha256": self.pre["packageSha256"][manager],
                "zipBytes": (fixture.ingestion / package["FileName"]).stat().st_size,
                "silverPersons": package["PeopleCount"],
                "silverSourceSetSha256Utf16le":
                    self.pre["sourceIdentitySetSha256Utf16le"][manager],
                "httpStatus": 202,
                "bronzePayloadAndBytesVerified": True,
                "lotesProcessadosVerified": True,
            })
        self.http = COMPARE.LINEAGE.build(self.pre, self.proofs, COMMIT)
        self.sql = {
            "schema": COMPARE.SQL_SCHEMA,
            "status": "SQL_RUN_LEDGER_DIAGNOSTIC_NOT_CERTIFIED",
            "runId": self.stage["runId"],
            "commitSha": COMMIT,
            "modelWinnerSha256": WINNER,
            "observationCount": 1001,
            "sourceSetDigestMethod": COMPARE.LINEAGE.METHOD,
            "sourceSetSha256Utf16le": self.pre["sourceSetSha256Utf16le"],
            "deliveryCount": 4,
            "deliveryProofs": [{
                key: row[key] for key in (
                    "gestor", "deliveryIdSha256", "packageSha256", "zipBytes",
                    "silverPersons", "silverSourceSetSha256Utf16le")
            } | {
                "sqlPayloadMetadataVerified": True,
                "sourceDeliveryIdMatches": True,
            } for row in self.proofs],
            "sqlRunTerminalVerified": True,
            "httpDeliveryProven": False,
            "goldIdentityStatesAvailable": False,
            "certified": False,
            "E2E_NUCLEO_PASS": False,
            "E2E_INTEGRAL_PASS": False,
        }

    def verify(self, pre=None, http=None, sql=None, stage=None):
        return COMPARE.compare(
            pre if pre is not None else self.pre,
            http if http is not None else self.http,
            sql if sql is not None else self.sql,
            stage if stage is not None else self.stage,
            COMMIT, WINNER)

    def test_four_delivery_sha_and_exact_run_membership_only_match_receipts(self):
        out = self.verify()
        self.assertEqual(out["schema"], COMPARE.OUTPUT_SCHEMA)
        self.assertEqual(out["status"], "RECEIPT_FIELDS_MATCHED_NOT_AUTHENTICATED")
        self.assertTrue(out["receiptsFieldConsistencyVerified"])
        self.assertEqual(out["sourceObservations"], 1001)
        self.assertEqual(out["deliveryComparisons"], 4)
        for key in ("sourceSqlRuntimeAuthenticatedExternally",
                    "httpRunExecutionAuthenticatedExternally",
                    "goldIdentityStatesAvailable", "scientificValidation",
                    "operationalParityCertified", "E2E_NUCLEO_PASS",
                    "E2E_INTEGRAL_PASS", "activated"):
            self.assertFalse(out[key])
        text = json.dumps(out)
        for secret in ("SYNTH-", "BasePersonId", "groupId", "deliveryId", "cpf"):
            self.assertNotIn(secret, text)

    def test_bad_sql_run_and_delivery_claims_are_denied(self):
        for mutation in ("wrong_run", "wrong_sha", "missing_delivery",
                         "new_manager", "replaced_delivery", "replaced_payload",
                         "source_drift", "wrong_total", "missing_bronze",
                         "missing_delivery_source_id", "new_field",
                         "wrong_grouping_sha", "false_e2e", "falsely_published",
                         "missing_run_verified", "source_pair_drift"):
            with self.subTest(mutation=mutation):
                sql = json.loads(json.dumps(self.sql))
                stage = json.loads(json.dumps(self.stage))
                proof = sql["deliveryProofs"][0]
                if mutation == "wrong_run":
                    sql["runId"] = "22222222-2222-2222-2222-222222222222"
                elif mutation == "wrong_sha":
                    sql["commitSha"] = "f" * 40
                elif mutation == "missing_delivery":
                    sql["deliveryProofs"].pop()
                    sql["deliveryCount"] = 3
                elif mutation == "new_manager":
                    proof["gestor"] = "UNTRUSTED"
                elif mutation == "replaced_delivery":
                    proof["deliveryIdSha256"] = "0" * 64
                elif mutation == "replaced_payload":
                    proof["packageSha256"] = "0" * 64
                elif mutation == "source_drift":
                    proof["silverSourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "wrong_total":
                    proof["silverPersons"] -= 1
                elif mutation == "missing_bronze":
                    proof["sqlPayloadMetadataVerified"] = False
                elif mutation == "missing_delivery_source_id":
                    proof["sourceDeliveryIdMatches"] = False
                elif mutation == "new_field":
                    proof["identityState"] = "REFERENCIA"
                elif mutation == "wrong_grouping_sha":
                    stage["sourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "false_e2e":
                    sql["E2E_INTEGRAL_PASS"] = True
                elif mutation == "falsely_published":
                    sql["httpDeliveryProven"] = True
                elif mutation == "missing_run_verified":
                    sql["sqlRunTerminalVerified"] = False
                else:
                    stage["rows"][0]["opaquePersonId"] = "SYNTH-" + "F" * 32
                with self.assertRaises(ValueError):
                    self.verify(sql=sql, stage=stage)

    def test_v1_schema_rejected_without_sql_to_http_connection(self):
        stage = json.loads(json.dumps(self.stage))
        stage["schema"] = COMPARE.ORACLE.STAGING_SCHEMA
        stage.pop("sourceSetDigestMethod")
        stage.pop("sourceSetSha256Utf16le")
        with self.assertRaises(ValueError):
            self.verify(stage=stage)

    def test_cli_rejects_hardlinked_http_and_sql_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            pre = temp / "preflight.json"
            http = temp / "http.json"
            sql = temp / "sql-hardlink.json"
            stage = temp / "staging.json"
            pre.write_text(json.dumps(self.pre), encoding="utf-8")
            http.write_text(json.dumps(self.http), encoding="utf-8")
            stage.write_text(json.dumps(self.stage), encoding="utf-8")
            try:
                sql.hardlink_to(http)
            except (OSError, NotImplementedError) as exc:
                self.skipTest(f"hardlinks unavailable in test filesystem: {exc}")
            from unittest import mock
            args = ["run-http", "--preflight", str(pre),
                    "--http", str(http), "--sql-ledger", str(sql),
                    "--staging", str(stage), "--expected-sha", COMMIT,
                    "--expected-winner", WINNER,
                    "--receipt", str(temp / "out.json")]
            with mock.patch("sys.argv", args):
                with self.assertRaisesRegex(ValueError, "cannot overwrite or alias"):
                    COMPARE.main()
            self.assertFalse((temp / "out.json").exists())

    def test_receipt_file_does_not_overwrite_any_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            paths = [temp / (name + ".json") for name in
                     ("preflight", "http", "sql", "staging")]
            for path, obj in zip(paths, (self.pre, self.http, self.sql, self.stage)):
                path.write_text(json.dumps(obj) + "\n", encoding="utf-8")
            out = temp / "reconciled.json"
            from unittest import mock
            args = ["run-http", "--preflight", str(paths[0]),
                    "--http", str(paths[1]), "--sql-ledger", str(paths[2]),
                    "--staging", str(paths[3]), "--expected-sha", COMMIT,
                    "--expected-winner", WINNER, "--receipt", str(out)]
            with mock.patch("sys.argv", args):
                COMPARE.main()
            self.assertEqual(json.loads(out.read_text())["status"],
                             "RECEIPT_FIELDS_MATCHED_NOT_AUTHENTICATED")
            with mock.patch("sys.argv", args):
                with self.assertRaises(ValueError):
                    COMPARE.main()


if __name__ == "__main__":
    unittest.main()

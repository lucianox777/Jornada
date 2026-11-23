"""Offline-only tests for the V2 HTTP->Bronze metadata receipt (#1851).

A fixture of four ZIPs and *simulated SQL proof records* is NOT an HTTP run.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import uuid

ROOT = Path(__file__).resolve().parents[2]


def imported(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


LINEAGE = imported("ibge_http_lineage", ROOT / "scripts/v9-e2e-ibge-http-lineage.py")
PREFLIGHT = imported("ibge_preflight", ROOT / "scripts/v9-e2e-ibge-bridge-preflight.py")
BRIDGE_FIXTURE = imported(
    "ibge_bridge_fixture",
    ROOT / "tests/scripts/test_v9_e2e_ibge_bridge_preflight.py")
COMMIT = "c" * 40


class IbgeHttpLineageContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        bridge = self.root / "ibge"
        bridge.mkdir()
        _, ingestion = BRIDGE_FIXTURE.fixture(bridge)
        self.preflight = PREFLIGHT.verify(bridge)
        self.preflight_path = self.root / "preflight.json"
        self.preflight_path.write_text(
            json.dumps(self.preflight) + "\n", encoding="utf-8")
        self.proofs = []
        for i, manager in enumerate(LINEAGE.MANAGERS):
            entry = next(p for p in
                         json.loads((ingestion / "bridge-manifest.json")
                                    .read_text(encoding="utf-8"))["packages"]
                         if p["GestorCodigo"] == manager)
            payload = ingestion / entry["FileName"]
            self.proofs.append({
                "gestor": manager,
                "deliveryIdSha256": hashlib.sha256(
                    str(uuid.UUID(int=i + 1)).encode("ascii")).hexdigest().upper(),
                "packageSha256": self.preflight["packageSha256"][manager],
                "zipBytes": payload.stat().st_size,
                "silverPersons": 250,
                "silverSourceSetSha256Utf16le":
                    self.preflight["sourceIdentitySetSha256Utf16le"][manager],
                "httpStatus": 202,
                "bronzePayloadAndBytesVerified": True,
                "lotesProcessadosVerified": True,
            })
        self.summary = LINEAGE.build(self.preflight, self.proofs, COMMIT)

    def test_four_sources_bronze_and_delivery_fingerprints_are_consistent(self):
        self.assertTrue(LINEAGE.verify(self.preflight, self.summary, COMMIT))
        self.assertEqual(self.summary["schema"], LINEAGE.SCHEMA)
        self.assertEqual(self.summary["httpZipBronzePayloadsVerified"], 4)
        self.assertEqual(self.summary["bronzeLotesTerminalVerified"], 4)
        self.assertEqual(self.summary["silverPersons"], 1000)
        self.assertEqual(len(self.summary["deliveryProofs"]), 4)
        self.assertFalse(self.summary["v9RunnerVerified"])
        self.assertFalse(self.summary["E2E_NUCLEO_PASS"])
        self.assertFalse(self.summary["E2E_INTEGRAL_PASS"])
        public = json.dumps(self.summary)
        for secret in ("DEV_TRUTH_ONLY", "BasePersonId", "SYNTH-",
                       "observationId", "idPessoaEntrega", "cpf"):
            self.assertNotIn(secret, public)

    def test_fail_closed_on_modified_or_duplicate_sql_metadata(self):
        for mutation in ("payload", "source", "delivery_duplicate",
                         "manager_duplicate", "silver_count", "bytes_zero",
                         "byte_bool", "no_bronze", "no_lotes", "http_failure",
                         "extra_field", "missing_manager", "bad_delivery_digest",
                         "bad_sha", "false_v9", "false_e2e",
                         "false_scientific", "aggregate_count"):
            with self.subTest(mutation=mutation):
                proof = json.loads(json.dumps(self.summary))
                rows = proof["deliveryProofs"]
                if mutation == "payload":
                    rows[0]["packageSha256"] = "0" * 64
                elif mutation == "source":
                    rows[0]["silverSourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "delivery_duplicate":
                    rows[1]["deliveryIdSha256"] = rows[0]["deliveryIdSha256"]
                elif mutation == "manager_duplicate":
                    rows[1]["gestor"] = rows[0]["gestor"]
                elif mutation == "silver_count":
                    rows[0]["silverPersons"] -= 1
                elif mutation == "bytes_zero":
                    rows[0]["zipBytes"] = 0
                elif mutation == "byte_bool":
                    rows[0]["zipBytes"] = True
                elif mutation == "no_bronze":
                    rows[0]["bronzePayloadAndBytesVerified"] = False
                elif mutation == "no_lotes":
                    rows[0]["lotesProcessadosVerified"] = False
                elif mutation == "http_failure":
                    rows[0]["httpStatus"] = 500
                elif mutation == "extra_field":
                    rows[0]["identityState"] = "REFERENCIA"
                elif mutation == "missing_manager":
                    rows.pop()
                elif mutation == "bad_delivery_digest":
                    rows[0]["deliveryIdSha256"] = "invalid"
                elif mutation == "bad_sha":
                    proof["sourceSetSha256Utf16le"] = "f" * 64
                elif mutation == "false_v9":
                    proof["v9RunnerVerified"] = True
                elif mutation == "false_e2e":
                    proof["E2E_NUCLEO_PASS"] = True
                elif mutation == "false_scientific":
                    proof["scientificValidation"] = True
                else:
                    proof["httpZipBronzePayloadsVerified"] = 3
                with self.assertRaises(ValueError):
                    LINEAGE.verify(self.preflight, proof, COMMIT)

    def test_invalid_preflight_is_never_accepted(self):
        receipt = json.loads(json.dumps(self.preflight))
        receipt["sourceIdentitySetSha256Utf16le"]["SEHAB"] = "0" * 64
        with self.assertRaises(ValueError):
            LINEAGE.verify(receipt, self.summary, COMMIT)
        with self.assertRaises(ValueError):
            LINEAGE.verify(self.preflight, self.summary, "INVALID")

    def test_cli_uses_private_aggregate_proofs_and_exclusive_output(self):
        proof_path = self.root / "proofs.jsonl"
        proof_path.write_text(
            "".join(json.dumps(p) + "\n" for p in self.proofs), encoding="utf-8")
        destination = self.root / "http-summary.json"
        with mock.patch("sys.argv", [
                "lineage", "--preflight", str(self.preflight_path),
                "--proofs", str(proof_path), "--expected-sha", COMMIT,
                "--receipt", str(destination)]):
            LINEAGE.main()
        data = json.loads(destination.read_text(encoding="utf-8"))
        self.assertEqual(data, self.summary)
        with mock.patch("sys.argv", [
                "lineage", "--preflight", str(self.preflight_path),
                "--proofs", str(proof_path), "--expected-sha", COMMIT,
                "--receipt", str(destination)]):
            with self.assertRaisesRegex(ValueError, "already exists"):
                LINEAGE.main()


if __name__ == "__main__":
    unittest.main()

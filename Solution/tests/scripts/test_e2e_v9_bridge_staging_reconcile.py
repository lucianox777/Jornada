"""Adversarial offline join: actual ZIP bridge bytes + synthetic V9 staging.

These fixtures fabricate no SQL provenance; they are only unit-contract input.
No true HTTP, real V9 scorer, real Gold state, scientific TEST or 100k run.
"""
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


JOIN = load("bridge_v9_join_tests", SCRIPTS / "e2e-v9-bridge-staging-reconcile.py")
BRIDGE_TEST = load(
    "original_bridge_test_fixture",
    ROOT / "tests/scripts/test_v9_e2e_ibge_bridge_preflight.py")
COMMIT = "c" * 40
WINNER = "a" * 64


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False) + "\n",
                    encoding="utf-8")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


class BridgeGroupingReconciliationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name) / "ibge"
        self.root.mkdir()
        corpus, ingestion = BRIDGE_TEST.fixture(self.root)
        self.ingestion = ingestion
        self.stage = Path(temp.name) / "sql-staging.json"
        self.build_real_preflight_fixture(corpus, ingestion)

    def build_real_preflight_fixture(self, corpus, ingestion):
        # The original 1k fixture has one observation per 1k people, leaving
        # no positive truth pair and making pairwise recall undefined. Add one
        # additional real ZIP member for the FIRST source identity, leaving all
        # 1,000 base identities present. This is an OFFLINE mock, NOT a 1k run.
        manifest_path = ingestion / "bridge-manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        all_rows = []
        opaque_mapping = {}
        extra_code = f"SYNTH-{1001:032X}"
        for entry in manifest["packages"]:
            path = ingestion / entry["FileName"]
            with zipfile.ZipFile(path) as archive:
                members = {
                    name: archive.read(name)
                    for name in ("manifest.json", "pessoas.jsonl", "registros.jsonl")
                }
            rows = [json.loads(line) for line in members["pessoas.jsonl"].splitlines()]
            for row in rows:
                old = row["codigoPessoaOrigem"]
                new = f"SYNTH-{len(all_rows) + 1:032X}"
                opaque_mapping[old] = new
                row["codigoPessoaOrigem"] = new
                row["idPessoaEntrega"] = new
                all_rows.append(new)
            if entry["GestorCodigo"] == "SEHAB":
                rows.append({
                    "codigoPessoaOrigem": extra_code,
                    "idPessoaEntrega": extra_code,
                })
                entry["PeopleCount"] += 1
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("manifest.json", members["manifest.json"])
                archive.writestr("pessoas.jsonl", "".join(
                    json.dumps(row) + "\n" for row in rows))
                archive.writestr("registros.jsonl", members["registros.jsonl"])
            entry["sha256"] = sha(path)
        self.assertEqual(len(opaque_mapping), 1000)
        truth_path = ingestion / "bridge-truth.jsonl"
        truth = [json.loads(line) for line in truth_path.read_text(
            encoding="utf-8").splitlines()]
        for row in truth:
            row["OpaquePersonId"] = opaque_mapping[row["OpaquePersonId"]]
        first = next(row for row in truth if row["GestorCodigo"] == "SEHAB")
        additional = dict(first, OpaquePersonId=extra_code,
                          ObservationId="OBS-SEHAB-250")
        truth.append(additional)
        truth_path.write_text("".join(json.dumps(row) + "\n"
                                      for row in truth), encoding="utf-8")
        manifest["truthSidecar"]["sha256"] = sha(truth_path)
        manifest["sourceObservationCount"] += 1
        manifest["materializedObservationCount"] += 1
        write_json(manifest_path, manifest)
        with (corpus / "observacoes.csv").open("a", newline="",
                                                encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow((additional["ObservationId"], additional["BasePersonId"],
                             additional["Partition"], additional["SyntheticGestor"]))
        ground = json.loads((corpus / "gabarito.json").read_text(
            encoding="utf-8"))
        ground["observations"] += 1
        write_json(corpus / "gabarito.json", ground)
        source_manifest_path = corpus / "generation-manifest.json"
        source_manifest = json.loads(source_manifest_path.read_text(
            encoding="utf-8"))
        for item in source_manifest["outputs"]:
            item["sha256"] = sha(corpus / item["path"])
        write_json(source_manifest_path, source_manifest)

        groups = {row["OpaquePersonId"]: str(uuid.UUID(int=i + 1))
                  for i, row in enumerate(truth)}
        groups[extra_code] = groups[first["OpaquePersonId"]]
        all_ids = sorted(row["OpaquePersonId"] for row in truth)
        source_digest = hashlib.sha256(
            "".join(opaque + "\n" for opaque in all_ids).encode("utf-16le")
        ).hexdigest().upper()
        self.staging = {
            "schema": JOIN.ORACLE.STAGING_SCHEMA_V2,
            "sourceSetDigestMethod": JOIN.ORACLE.SOURCE_SET_DIGEST_METHOD,
            "sourceSetSha256Utf16le": source_digest,
            "status": "STAGING_NOT_CERTIFIED",
            "runId": "11111111-1111-1111-1111-111111111111",
            "commitSha": COMMIT,
            "modelWinnerSha256": WINNER,
            "observationCount": len(truth),
            "identityStatesAvailable": False,
            "certified": False,
            "E2E_NUCLEO_PASS": False,
            "E2E_INTEGRAL_PASS": False,
            "rows": [
                {"opaquePersonId": row["OpaquePersonId"],
                 "groupId": groups[row["OpaquePersonId"]]}
                for row in truth
            ],
        }
        write_json(self.stage, self.staging)

    def evaluate(self):
        return JOIN.reconcile(self.root, self.stage, COMMIT, WINNER)

    def test_actual_zip_bytes_source_set_and_staging_reconcile_noncertifying(self):
        result = self.evaluate()
        self.assertEqual(result["status"], "RECONCILED_OFFLINE_NOT_CERTIFIED")
        self.assertTrue(result["bridgeZipBytesAndSourceSetVerified"])
        self.assertTrue(result["independentSqlToZipSourceSetDigestMatched"])
        self.assertEqual(result["sourceSetDigestMethod"],
                         JOIN.ORACLE.SOURCE_SET_DIGEST_METHOD)
        self.assertEqual(result["sourceObservations"], 1001)
        self.assertEqual(result["baseIdentities"], 1000)
        self.assertEqual(result["packages"], 4)
        self.assertEqual(result["sourceGroupPairs"]["truePositive"], 1)
        self.assertEqual(result["sourceGroupPairs"]["falsePositive"], 0)
        self.assertEqual(result["sourceGroupPairs"]["falseNegative"], 0)
        self.assertFalse(result["linkageSqlRunProvenanceCertified"])
        self.assertFalse(result["bridgeToSqlHttpDeliveryProven"])
        self.assertFalse(result["identityStatesAvailable"])
        self.assertFalse(result["scientificValidation"])
        self.assertFalse(result["E2E_NUCLEO_PASS"])
        self.assertFalse(result["E2E_INTEGRAL_PASS"])
        encoded = json.dumps(result)
        for secret in ("DEV_TRUTH_ONLY", "SYNTH-", "BASE-", "groupId",
                       "codigoPessoaOrigem", "idPessoaEntrega"):
            self.assertNotIn(secret, encoded)

    def test_staging_drift_or_attempt_to_forge_gold_or_certification_fails(self):
        for mutation in ("missing", "unknown", "fake_state", "certified",
                         "wrong_model", "full_schema", "v1_downgrade",
                         "digest_drift", "digest_method", "missing_digest"):
            with self.subTest(mutation=mutation):
                data = json.loads(json.dumps(self.staging))
                if mutation == "missing":
                    data["rows"].pop()
                    data["observationCount"] -= 1
                elif mutation == "unknown":
                    data["rows"][0]["opaquePersonId"] = f"SYNTH-{999999:032X}"
                elif mutation == "fake_state":
                    data["rows"][0]["identityState"] = "REFERENCIA"
                elif mutation == "certified":
                    data["E2E_NUCLEO_PASS"] = True
                elif mutation == "wrong_model":
                    data["modelWinnerSha256"] = "b" * 64
                elif mutation == "v1_downgrade":
                    data["schema"] = JOIN.ORACLE.STAGING_SCHEMA
                    data.pop("sourceSetSha256Utf16le")
                    data.pop("sourceSetDigestMethod")
                elif mutation == "digest_drift":
                    data["sourceSetSha256Utf16le"] = "0" * 64
                elif mutation == "digest_method":
                    data["sourceSetDigestMethod"] = "UNVERIFIED"
                elif mutation == "missing_digest":
                    data.pop("sourceSetSha256Utf16le")
                else:
                    data["schema"] = JOIN.ORACLE.SCHEMA
                write_json(self.stage, data)
                with self.assertRaises(ValueError):
                    self.evaluate()
                write_json(self.stage, self.staging)

    def test_package_changed_despite_rehashed_manifest_is_denied(self):
        manifest_path = self.ingestion / "bridge-manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        item = manifest["packages"][0]
        path = self.ingestion / item["FileName"]
        with zipfile.ZipFile(path) as archive:
            contents = {name: archive.read(name)
                        for name in ("manifest.json", "pessoas.jsonl",
                                     "registros.jsonl")}
        rows = [json.loads(line) for line in contents["pessoas.jsonl"].splitlines()]
        rows[0]["codigoPessoaOrigem"] = f"SYNTH-{999999:032X}"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("manifest.json", contents["manifest.json"])
            archive.writestr("pessoas.jsonl", "".join(
                json.dumps(row) + "\n" for row in rows))
            archive.writestr("registros.jsonl", contents["registros.jsonl"])
        item["sha256"] = sha(path)
        write_json(manifest_path, manifest)
        with self.assertRaisesRegex(ValueError, "Person ZIP does not match"):
            self.evaluate()

    def test_output_must_be_outside_bridge_and_preexisting_is_refused(self):
        copied = self.ingestion / "sql-staging.json"
        copied.write_bytes(self.stage.read_bytes())
        with self.assertRaisesRegex(ValueError, "outside"):
            JOIN.reconcile(self.root, copied, COMMIT, WINNER)


if __name__ == "__main__":
    unittest.main()

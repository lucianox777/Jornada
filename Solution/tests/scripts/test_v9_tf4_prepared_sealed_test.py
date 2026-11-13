#!/usr/bin/env python3
"""Pre-generated V9 TF4 TEST remains cryptographically sealed and out of TRAIN."""
from pathlib import Path
import hashlib
import importlib.util
import json
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "scripts/v9-tf4-sealed-test-preparation.py"
LOCK = ROOT / "config/calibration/v9-tf4-100k-test-preparation-lock.json"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
spec = importlib.util.spec_from_file_location("seal_v9_test", TOOL)
seal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(seal)
SHA = "f" * 40
RUN = 43499900123


class SealedTestPreparationContract(unittest.TestCase):
    def test_sha_locked_seed_and_inputs(self):
        self.assertEqual(seal.sha_file(LOCK), seal.LOCK_DEFAULT_SHA)
        data = seal.inspect_lock(LOCK, seal.LOCK_DEFAULT_SHA)
        self.assertEqual(data["testSeed"], 20261113001)
        self.assertEqual(len({data["trainSeed"],data["validationSeed"],data["testSeed"]}),3)
        self.assertIn("NOT_SCIENTIFIC_TEST_EXECUTION", data["scope"])
        changed = dict(data, testSeed=42)
        with tempfile.TemporaryDirectory() as temp:
            file = Path(temp)/"drift.json"
            file.write_text(json.dumps(changed))
            with self.assertRaisesRegex(ValueError, "LOCK SHA diverges"):
                seal.inspect_lock(file, seal.LOCK_DEFAULT_SHA)

    def test_real_aes_256_gcm_encryption_decryption_and_integrity(self):
        if not __import__("shutil").which("openssl"):
            self.skipTest("OpenSSL unavailable on local test machine")
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            key,cert=root/"private-key.pem",root/"public-cert.pem"
            subprocess.run(["openssl","req","-x509","-newkey","rsa:2048",
                            "-nodes","-keyout",str(key),"-out",str(cert),
                            "-days","1","-subj","/CN=v9-test"],
                           stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=True)
            raw=root/"raw"
            raw.mkdir()
            for name in seal.NAMES:
                if name != "generation-manifest.json":
                    (raw/name).write_text(name+" deterministic synthetic fixture\n")
            outputs=[{"path":name,"sha256":seal.sha_file(raw/name)}
                     for name in seal.NAMES if name!="generation-manifest.json"]
            (raw/"generation-manifest.json").write_text(json.dumps({
                "schema_version":1,"seed":20261113001,
                "population_profile":"DEMOGRAPHIC_PRIMARY_V1",
                "error_profile":"clean",
                "reference_code":"CENSO2022_NOMES_BRASIL_V1",
                "outputs":outputs,
            }))
            encrypted=root/"encrypted"
            r=seal.seal(LOCK,seal.LOCK_DEFAULT_SHA,raw,cert,seal.sha_file(cert),
                        encrypted,SHA,RUN)
            self.assertEqual(r["testPopulation"],100000)
            self.assertFalse(r["tested"])
            self.assertNotIn(b"deterministic synthetic fixture",
                             (encrypted/"test-corpus.cms").read_bytes())
            self.assertEqual(set(p.name for p in encrypted.iterdir()),
                             {"test-corpus.cms","test-seal-receipt.json"})
            self.assertEqual(seal.verify_sealed(encrypted,LOCK,seal.LOCK_DEFAULT_SHA,SHA,RUN),r)
            output=root/"decrypted-only-after-lock"
            self.assertEqual(seal.unseal(encrypted,LOCK,seal.LOCK_DEFAULT_SHA,SHA,RUN,
                                         cert,key,output)["status"],
                             "TEST_CORPUS_OPENED_AFTER_EXTERNAL_LOCK_NOT_TESTED")
            self.assertEqual({p.name for p in output.iterdir()},set(seal.NAMES))
            self.assertEqual((output/"gabarito.json").read_bytes(),
                             (raw/"gabarito.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "run ID|manifest"):
                seal.verify_sealed(encrypted,LOCK,seal.LOCK_DEFAULT_SHA,SHA,RUN+1)
            cipher=encrypted/"test-corpus.cms"
            with cipher.open("ab") as f:
                f.write(b"corrupted")
            with self.assertRaisesRegex(ValueError, "ciphertext"):
                seal.verify_sealed(encrypted,LOCK,seal.LOCK_DEFAULT_SHA,SHA,RUN)

    def test_preparation_is_isolated_from_train_and_validation(self):
        workflow=WF.read_text(encoding="utf-8")
        pre, tail=workflow.split("\n  physical-100k:\n",1)
        train, tail=tail.split("\n  validation-100k:\n",1)
        validation, test=tail.split("\n  test-100k:\n",1)
        self.assertIn("  prepare-test-100k:",pre)
        self.assertIn("timeout-minutes: 60",pre)
        self.assertIn("prepare_sealed_test:",pre)
        self.assertIn("expected_test_cert_sha256:",pre)
        self.assertIn("v9-tf4-sealed-test-preparation.py verify-lock",pre)
        self.assertIn("v9-tf4-sealed-test-preparation.py seal",pre)
        self.assertIn("v9-tf4-sealed-test-preparation.py verify",pre)
        self.assertIn('TEST_PUBLIC_CERT_B64: ${{ secrets.V9_TF4_TEST_PUBLIC_CERT_B64 }}',pre)
        self.assertIn("--seed 20261113001",pre)
        self.assertIn("v9-tf4-test-sealed-",pre)
        self.assertIn("if-no-files-found: error",pre)
        self.assertIn("rm -rf --",pre)
        self.assertIn("needs: prepare-test-100k",train)
        self.assertIn("inputs.prepare_sealed_test != true",train)
        self.assertIn("needs.prepare-test-100k.result == 'success'",train)
        self.assertIn("needs: physical-100k",validation)
        self.assertIn("inputs.test_required == true",test)
        self.assertNotIn("if: ${{ false }}",test)
        self.assertIn("needs: [validation-100k, prepare-test-100k]",test)
        for unprivileged in (train,validation):
            self.assertNotIn("TEST_PUBLIC_CERT_B64",unprivileged)
            self.assertNotIn("V9_TF4_TEST_PRIVATE",unprivileged)
            self.assertNotIn("test-corpus.cms",unprivileged)
            self.assertNotIn("v9-tf4-test-sealed-",unprivileged)
            self.assertNotIn("20261113001",unprivileged)
        self.assertIn("test_required }}",train)
        self.assertIn("test_required }}",validation)

    def test_no_plaintext_or_key_in_upload_artifact(self):
        source=WF.read_text()
        prep=source.split("\n  physical-100k:\n",1)[0]
        upload=prep.split("name: Upload only encrypted TEST and immutable receipt",1)[1].split(
            "- name: Dispose any transient TEST plaintext",1)[0]
        self.assertIn("path: Solution/.local/v9-test-prep/sealed/",upload)
        for file in ("raw/", "gabarito.json", "observacoes.csv", "pessoas_verdade.csv",
                     "public.pem", "generation.log", "private-key.pem"):
            self.assertNotIn(file,upload)
        self.assertNotIn('test "${{ inputs.test_required }}" = "false"',prep)


if __name__ == "__main__":
    unittest.main()

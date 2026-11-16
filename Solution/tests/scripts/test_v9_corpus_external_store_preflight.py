import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-external-store-preflight.py"
spec = importlib.util.spec_from_file_location("external_preflight", SCRIPT)
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


class ExternalStorePreflightTests(unittest.TestCase):
    def test_unprovisioned_local_directory_never_passes(self):
        with tempfile.TemporaryDirectory() as temp:
            attestation = {
                "schema": "v9-external-store-attestation-v1",
                "repository": "lucianox777/Jornada",
                "provider_type": "external-posix-shared",
                "cross_run_persistence_verified": True,
                "evidence_run_ids": [101, 102],
            }
            with self.assertRaisesRegex(ValueError, "mounted filesystem"):
                preflight.validate(Path(temp), attestation, "lucianox777/Jornada")

    def test_unverified_or_single_run_attestation_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            a = {
                "schema": "v9-external-store-attestation-v1",
                "repository": "lucianox777/Jornada",
                "provider_type": "external-posix-shared",
                "cross_run_persistence_verified": False,
                "evidence_run_ids": [101, 102],
            }
            with self.assertRaisesRegex(ValueError, "not been independently verified"):
                preflight.validate(Path(temp), a, "lucianox777/Jornada")
            a["cross_run_persistence_verified"] = True
            a["evidence_run_ids"] = [101, 101]
            with self.assertRaisesRegex(ValueError, "distinct run IDs"):
                preflight.validate(Path(temp), a, "lucianox777/Jornada")
            a["evidence_run_ids"] = [True, 102]
            with self.assertRaisesRegex(ValueError, "Invalid cross-run evidence IDs"):
                preflight.validate(Path(temp), a, "lucianox777/Jornada")
            a["evidence_run_ids"] = [101, 102]
            a["repository"] = "other/repo"
            with self.assertRaisesRegex(ValueError, "repository binding"):
                preflight.validate(Path(temp), a, "lucianox777/Jornada")


    def test_filesystem_root_cannot_be_claimed_as_external_store(self):
        attestation = {
            "schema": "v9-external-store-attestation-v1",
            "repository": "lucianox777/Jornada",
            "provider_type": "external-posix-shared",
            "cross_run_persistence_verified": True,
            "evidence_run_ids": [101, 102],
        }
        with self.assertRaisesRegex(ValueError, "Filesystem root"):
            preflight.validate(Path("/"), attestation, "lucianox777/Jornada")

if __name__ == "__main__":
    unittest.main()

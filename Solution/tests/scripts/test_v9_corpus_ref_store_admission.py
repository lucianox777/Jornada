"""REF durable-store admission must fail closed on local or unattested stores."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

script = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-ref-store-admission.py"
spec = importlib.util.spec_from_file_location("ref_admission", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RefAdmissionTests(unittest.TestCase):
    def test_local_ref_directory_cannot_claim_durable_cross_run_store(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            official = root / "official"
            official.mkdir()
            attestation = root / "attestation.json"
            attestation.write_text(json.dumps({
                "schema": "v9-external-store-attestation-v1",
                "repository": "lucianox777/Jornada",
                "provider_type": "external-posix-shared",
                "cross_run_persistence_verified": True,
                "evidence_run_ids": [1, 2],
            }))
            with self.assertRaisesRegex(ValueError, "mounted filesystem"):
                module.admit(root, official, root / "synthetic" / "v9",
                             attestation, "lucianox777/Jornada")
            attestation.unlink()
            with self.assertRaisesRegex(ValueError, "attestation"):
                module.admit(root, official, root / "synthetic" / "v9",
                             attestation, "lucianox777/Jornada")


if __name__ == "__main__":
    unittest.main()

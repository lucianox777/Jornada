"""ZIP member CRC must be checked even when no member matches truth size."""
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("e2e_truth_crc", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ExternalTruthZipCrcTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.zip_path = self.root / "payload.zip"
        self.truth_path = self.root / "truth.json"

    def fixture(self, corrupt=False):
        with zipfile.ZipFile(self.zip_path, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("payload.bin", b"synthetic-payload-bytes")
        if corrupt:
            data = bytearray(self.zip_path.read_bytes())
            # Local file header: 30 bytes + filename + extra field.
            name_length, extra_length = struct.unpack_from("<HH", data, 26)
            offset = 30 + name_length + extra_length
            data[offset] ^= 0x01
            self.zip_path.write_bytes(data)
        self.truth_path.write_text(json.dumps({
            "zipSha256": hashlib.sha256(self.zip_path.read_bytes()).hexdigest(),
            "expectedIdentities": ["synthetic-A"],
        }))

    def test_corrupt_member_fails_even_with_matching_zip_sha_binding(self):
        self.fixture(corrupt=True)
        with self.assertRaisesRegex(zipfile.BadZipFile, "CRC"):
            module.validate(self.zip_path, self.truth_path)

    def test_intact_member_passes(self):
        self.fixture()
        self.assertEqual(module.validate(self.zip_path, self.truth_path)["status"], "PASS")


if __name__ == "__main__":
    unittest.main()

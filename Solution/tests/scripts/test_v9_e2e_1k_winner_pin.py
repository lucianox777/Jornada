import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SOURCE = Path(__file__).resolve().parents[2] / "scripts/v9-e2e-1k-winner-pin.py"
SPEC = importlib.util.spec_from_file_location("winner_pin", SOURCE)
PIN = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PIN)


class WinnerPinTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.archive = self.root / "winner.zip"
        self.out = self.root / "selection"
        self.model = b'{"synthetic_fixture":true}'
        self.sha = hashlib.sha256(self.model).hexdigest()
        self.filename = self.sha + ".operational.json"

    def package(self, entries):
        with zipfile.ZipFile(self.archive, "w") as z:
            for name, value in entries:
                z.writestr(name, value)
        raw = self.archive.read_bytes()
        return patch.multiple(PIN, ZIP_SHA=hashlib.sha256(raw).hexdigest(),
                              ZIP_BYTES=len(raw), WINNER_SHA=self.sha,
                              WINNER_BYTES=len(self.model))

    def test_exact_bytes_selection_and_no_overwrite(self):
        with self.package([(self.filename, self.model)]):
            result = PIN.verify(self.archive, self.out)
            self.assertEqual(result["winnerSha256"], self.sha)
            self.assertEqual((self.out / self.filename).read_bytes(), self.model)
            self.assertEqual(json.loads((self.out / "selection.json").read_text()),
                             {"ArtifactPath": self.filename, "WinnerSha256": self.sha})
            with self.assertRaises(FileExistsError):
                PIN.verify(self.archive, self.out)

    def test_zip_digest_mismatch(self):
        with self.package([(self.filename, self.model)]):
            raw = self.archive.read_bytes()
            self.archive.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
            with self.assertRaisesRegex(ValueError, "ZIP_SHA_MISMATCH"):
                PIN.verify(self.archive, self.out)
        self.assertFalse(self.out.exists())

    def test_wrong_inner_digest(self):
        with self.package([(self.filename, b"x" * len(self.model))]):
            with self.assertRaisesRegex(ValueError, "OPERATIONAL_SHA_MISMATCH"):
                PIN.verify(self.archive, self.out)
        self.assertFalse(self.out.exists())

    def test_wrong_path_or_extra_member(self):
        for entries in ([("../" + self.filename, self.model)],
                        [(self.filename, self.model), ("extra", b"x")]):
            with self.subTest(entries=len(entries)), self.package(entries):
                with self.assertRaises(ValueError):
                    PIN.verify(self.archive, self.out)
                self.assertFalse(self.out.exists())

    def test_fixed_real_pin(self):
        self.assertEqual(PIN.ARTIFACT_ID, 14742452561)
        self.assertEqual(PIN.RUN_ID, 44970012048)
        self.assertEqual(PIN.WINNER_SHA, "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee")
        self.assertEqual(PIN.ZIP_SHA, "24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7")


if __name__ == "__main__":
    unittest.main()

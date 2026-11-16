import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-ref-zip.py"
spec = importlib.util.spec_from_file_location("ref_zip", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class RefZipTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "source"
        self.source.mkdir()
        for name in module.REQUIRED:
            (self.source / name).write_bytes(("sample:" + name).encode())
        self.archive = self.root / "ref.zip"
        self.manifest = self.root / "manifest.json"

    def test_pack_verify_and_create_only(self):
        record = module.pack(self.source, self.archive, self.manifest)
        self.assertEqual(module.inspect(self.archive, self.manifest), record)
        with self.assertRaises(ValueError):
            module.pack(self.source, self.archive, self.manifest)

    def test_tampered_archive_fails(self):
        module.pack(self.source, self.archive, self.manifest)
        with self.archive.open("ab") as f:
            f.write(b"tamper")
        with self.assertRaises(ValueError):
            module.inspect(self.archive, self.manifest)

    def test_extra_entry_fails(self):
        module.pack(self.source, self.archive, self.manifest)
        with zipfile.ZipFile(self.archive, "a") as z:
            z.writestr("../evil", b"evil")
        record = json.loads(self.manifest.read_text())
        record["zip_sha256"] = module.sha(self.archive)
        record["zip_size"] = self.archive.stat().st_size
        self.manifest.write_text(json.dumps(record))
        with self.assertRaises(ValueError):
            module.inspect(self.archive, self.manifest)

    def test_missing_source_fails(self):
        (self.source / module.REQUIRED[0]).unlink()
        with self.assertRaises(ValueError):
            module.pack(self.source, self.archive, self.manifest)

if __name__ == "__main__":
    unittest.main()

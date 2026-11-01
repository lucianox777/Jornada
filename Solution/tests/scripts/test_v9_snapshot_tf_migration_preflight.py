#!/usr/bin/env python3
"""Archival checksum/TF0 preflight. Does not rewrite historical snapshots."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "v9_snapshot_preflight", ROOT / "scripts/v9-snapshot-tf-migration-preflight.py")
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)


class V9SnapshotArchivePreflightTests(unittest.TestCase):
    def test_all_committed_archives_remain_byte_pinned_and_tf0(self):
        root = ROOT / "data/calibration/v9"
        records = [MOD.inspect_archive(p) for p in MOD.archives(root)]
        self.assertEqual(len(records), 5)  # baseline v0, 2 integral, 2 support
        self.assertTrue(all(r["legacy_tf0"] and not r["rewritten"]
                            and r["new_sha256"] is None for r in records))
        self.assertEqual(len({r["original_sha256"] for r in records}), 5)

    def test_tampered_legacy_archive_fails_closed(self):
        original = MOD.archives(ROOT / "data/calibration/v9")[0]
        with tempfile.TemporaryDirectory() as temp:
            destination = Path(temp) / original.name
            destination.write_bytes(original.read_bytes() + b" ")
            with self.assertRaises(ValueError):
                MOD.inspect_archive(destination)

    def test_positive_legacy_snapshot_requires_separate_migration(self):
        original = MOD.archives(ROOT / "data/calibration/v9")[0]
        with tempfile.TemporaryDirectory() as temp:
            payload = json.loads(original.read_bytes())
            payload["TfWeight"] = 1
            encoded = json.dumps(payload, separators=(",", ":")).encode()
            import hashlib
            sha = hashlib.sha256(encoded).hexdigest()
            path = Path(temp) / (sha + ".fitted.json")
            path.write_bytes(encoded)
            with self.assertRaises(ValueError):
                MOD.inspect_archive(path)


if __name__ == "__main__":
    unittest.main()

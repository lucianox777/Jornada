#!/usr/bin/env python3
"""Offline regression tests for read-only P0-A LOCKED generator diagnosis."""
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "diagnose-v9-locked-generator.py"
spec = importlib.util.spec_from_file_location("locked_generator_diagnosis", SCRIPT)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class DiagnoseLockedGeneratorTests(unittest.TestCase):
    def test_matching_and_mismatching_manifests_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            gen = root / gate.GEN
            gen.mkdir(parents=True)
            (gen / "SyntheticCorpusGenerator.cs").write_bytes(b"frozen generator\n")
            files = [f"{gate.GEN}/SyntheticCorpusGenerator.cs"]
            files.extend(f"{gate.GEN}/File{i:02}.cs" for i in range(24))
            for name in files[1:]:
                (root / name).write_bytes(name.encode())
            digest = gate.sha256("".join(
                f"{gate.sha256((root / p).read_bytes())}  {p}\n"
                for p in sorted(files)
            ).encode())
            partition = gate.sha256((gen / "SyntheticCorpusGenerator.cs").read_bytes())
            for name in gate.LOCKS:
                file = root / name
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text(json.dumps({
                    "status": "LOCKED",
                    "generation": {
                        "generator_sha256": digest,
                        "partition_contract": {"generator_file_sha256": partition},
                    },
                }))
            class GitResult:
                stdout = "\n".join(files) + "\n"
            with patch.object(gate, "ROOT", root), patch.object(
                gate.subprocess, "run", return_value=GitResult()
            ):
                good = gate.diagnose()
                self.assertEqual(2, len(good))
                self.assertTrue(all(
                    x["generator_tree_matches"] and x["partition_file_matches"]
                    and x["file_count_matches"] for x in good
                ))
                (gen / "SyntheticCorpusGenerator.cs").write_bytes(b"changed\n")
                bad = gate.diagnose()
                self.assertTrue(all(
                    not x["generator_tree_matches"] and not x["partition_file_matches"]
                    for x in bad
                ))

    def test_missing_tracked_tree_is_rejected(self):
        class GitResult:
            stdout = ""
        with patch.object(gate.subprocess, "run", return_value=GitResult()):
            with self.assertRaisesRegex(RuntimeError, "not tracked"):
                gate.diagnose()


if __name__ == "__main__":
    unittest.main()

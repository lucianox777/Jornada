#!/usr/bin/env python3
"""Offline tests for the read-only V9 LOCK generator provenance diagnostic."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-generator-lock-provenance-diagnostic.py"
spec = importlib.util.spec_from_file_location("v9_generator_provenance", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GeneratorProvenanceTests(unittest.TestCase):
    def test_digest_uses_sorted_paths_and_blob_bytes(self):
        files = {"Solution/src/Jornada.Linkage.SyntheticCorpus/Z.cs": b"z\n",
                 "Solution/src/Jornada.Linkage.SyntheticCorpus/A.cs": b"a\r\n"}
        def fake_git(repo, *args):
            if args[0] == "ls-tree":
                return ("\n".join(reversed(list(files))) + "\n").encode()
            if args[0] == "show":
                return files[args[1].split(":", 1)[1]]
            raise AssertionError(args)
        with patch.object(module, "git", side_effect=fake_git):
            actual, paths = module.digest(Path("."), "historical")
        expected_lines = "".join(f"{hashlib.sha256(files[p]).hexdigest()}  {p}\n" for p in sorted(files))
        self.assertEqual(actual, hashlib.sha256(expected_lines.encode()).hexdigest())
        self.assertEqual(paths, sorted(files))

    def test_reports_changes_without_claiming_corpus_equivalence(self):
        path = module.LOCKS[0]
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            (repo / path).parent.mkdir(parents=True)
            (repo / path).write_text(json.dumps({"generation": {
                "generator_commit": "historical",
                "generator_sha256": "historic-digest"
            }}))
            def fake_digest(repo, ref):
                if ref == "historical":
                    return "historic-digest", ["a.cs", "b.cs"]
                return "new-digest", ["a.cs", "c.cs"]
            def fake_git(repo, *args):
                return b"old" if args[1].startswith("historical:") else b"new"
            with patch.object(module, "LOCKS", (path,)), \
                 patch.object(module, "digest", side_effect=fake_digest), \
                 patch.object(module, "git", side_effect=fake_git):
                result = module.compare(repo)[0]
            self.assertTrue(result["historical_matches_lock"])
            self.assertFalse(result["current_matches_lock"])
            self.assertEqual(result["added_paths"], ["c.cs"])
            self.assertEqual(result["removed_paths"], ["b.cs"])
            self.assertEqual(result["modified_paths"], ["a.cs"])
            self.assertEqual(result["corpus_byte_equivalence"], "NOT_TESTED")


if __name__ == "__main__":
    unittest.main()

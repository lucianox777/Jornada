#!/usr/bin/env python3
"""Offline tests for the seeds 44-47 LOCKED manifest gate (no corpus, SQL or TEST)."""
import hashlib
import importlib.util
import json
import unittest
from pathlib import Path

SOLUTION = Path(__file__).resolve().parents[2]
REPO = SOLUTION.parent
spec = importlib.util.spec_from_file_location("v9_lock_gate", SOLUTION / "scripts" / "v9-tf-frozen-transfer-lock-gate.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
LOCK = REPO / "Solution" / "docs" / "V9_TF_SEEDS_44_47_LOCK.json"
WORKFLOW = REPO / ".github" / "workflows" / "v9-tf-seed43-frozen-transfer.yml"


def lock_sha():
    return hashlib.sha256(LOCK.read_bytes()).hexdigest()


class LockGateTests(unittest.TestCase):
    def test_workflow_references_the_committed_lock_digest(self):
        self.assertIn("SEEDS_44_47_LOCK_SHA256: " + lock_sha(), WORKFLOW.read_text(encoding="utf-8"))

    def test_each_authorized_seed_passes_against_checked_out_generator(self):
        for seed in (44, 45, 46, 47):
            report = gate.check(REPO, seed, lock_sha())
            self.assertEqual(report["seed"], seed)
            self.assertEqual(report["status"], "LOCK_GATE_PASS_NO_CORPUS_YET")

    def test_rejects_unlisted_seed_or_wrong_digest(self):
        for seed in (42, 43, 48):
            with self.assertRaisesRegex(ValueError, "not authorized"):
                gate.check(REPO, seed, lock_sha())
        with self.assertRaisesRegex(ValueError, "digest"):
            gate.check(REPO, 44, "0" * 64)

    def test_lock_contains_no_future_corpus_hashes_or_results(self):
        text = LOCK.read_text(encoding="utf-8")
        lock = json.loads(text)
        self.assertEqual(lock["status"], "LOCKED")
        self.assertNotIn("null", text)
        for forbidden in ("TrueAccepted", "Recall", "observacoes_sha256", "run_id_44"):
            self.assertNotIn(forbidden, text)
        self.assertFalse(lock["evaluation"]["refit_allowed"])
        self.assertTrue(lock["evaluation"]["test_sealed"])
        self.assertTrue(lock["seed_exposure"]["44"].startswith("EXPOSED"))


if __name__ == "__main__":
    unittest.main()

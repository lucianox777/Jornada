#!/usr/bin/env python3
"""Offline tests for the pre-registered 3-sigma campaign sign test and its LOCK (no SQL, no TEST)."""
import hashlib
import importlib.util
import json
import unittest
from pathlib import Path

SOLUTION = Path(__file__).resolve().parents[2]
REPO = SOLUTION.parent
spec = importlib.util.spec_from_file_location("v9_transfer_sign", SOLUTION / "scripts" / "v9-tf-seed43-frozen-transfer-physical.py")
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)
gspec = importlib.util.spec_from_file_location("v9_lock_gate_3s", SOLUTION / "scripts" / "v9-tf-frozen-transfer-lock-gate.py")
gate = importlib.util.module_from_spec(gspec)
gspec.loader.exec_module(gate)
LOCK = REPO / "Solution" / "docs" / "V9_TF_SEEDS_48_57_LOCK.json"
WORKFLOW = REPO / ".github" / "workflows" / "v9-tf-seed43-frozen-transfer.yml"


def analysis(seed, r0, r1, fp0=0, fp1=0):
    arm = lambda o, r, fp: {"OptionId": o, "ConflictMargin": 0.5, "Recall": r, "FP": fp}
    return {"TargetSeed": seed, "Refitted": False, "TestUsed": False,
            "Arms": [arm(tool.CONTROL, r0, fp0), arm(tool.TREATMENT, r1, fp1)]}


class SignTestTests(unittest.TestCase):
    def test_ten_of_ten_meets_three_sigma(self):
        r = tool.campaign_sign_test([analysis(s, .960, .964) for s in range(48, 58)])
        self.assertEqual(r["Positive"], 10)
        self.assertAlmostEqual(r["PTwoSided"], 2 / 1024)
        self.assertTrue(r["Met"])
        self.assertGreater(r["SigmaEquivalentTwoSided"], 3)

    def test_nine_of_ten_does_not_meet_and_ties_are_non_positive(self):
        rows = [analysis(s, .960, .964) for s in range(48, 57)] + [analysis(57, .960, .960)]
        r = tool.campaign_sign_test(rows)
        self.assertEqual((r["Positive"], r["NonPositive"]), (9, 1))
        self.assertAlmostEqual(r["PTwoSided"], 22 / 1024)
        self.assertFalse(r["Met"])

    def test_rejects_wrong_campaign_count_or_duplicates(self):
        with self.assertRaises(ValueError):
            tool.campaign_sign_test([analysis(s, .96, .97) for s in range(48, 57)])
        with self.assertRaises(ValueError):
            tool.campaign_sign_test([analysis(48, .96, .97)] * 10)


class ThreeSigmaLockTests(unittest.TestCase):
    def sha(self):
        return hashlib.sha256(LOCK.read_bytes()).hexdigest()

    def test_workflow_pins_the_lock_and_every_seed_passes(self):
        self.assertIn("SEEDS_48_57_LOCK_SHA256: " + self.sha(), WORKFLOW.read_text(encoding="utf-8"))
        for seed in range(48, 58):
            self.assertEqual(gate.check(REPO, seed, self.sha(), gate.LOCK_3SIGMA)["seed"], seed)
        for seed in (43, 47, 58):
            with self.assertRaises(ValueError):
                gate.check(REPO, seed, self.sha(), gate.LOCK_3SIGMA)

    def test_preregistration_is_complete_and_has_no_results(self):
        lock = json.loads(LOCK.read_bytes())
        st = lock["statistics"]
        self.assertEqual((st["alpha_two_sided"], st["planned_campaigns"]), (0.0026998, 10))
        self.assertEqual(lock["execution"]["seeds"], list(range(48, 58)))
        self.assertFalse(lock["evaluation"]["refit_allowed"])
        text = LOCK.read_text(encoding="utf-8")
        for forbidden in ("TrueAccepted", "RecallTF0", "observacoes_sha256", "41942914164_result"):
            self.assertNotIn(forbidden, text)


if __name__ == "__main__":
    unittest.main()

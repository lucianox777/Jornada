#!/usr/bin/env python3
"""Negative contract tests for the frozen seed-43 transfer preflight.

These tests never access SQL, corpus truth, or TEST outcomes.
"""
import importlib.util
import pathlib
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = pathlib.Path(__file__).resolve().parent / "v9-tf-seed43-frozen-transfer-preflight.py"
spec = importlib.util.spec_from_file_location("seed43_preflight_under_test", SCRIPT)
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)

HASH0 = "a" * 64
HASH1 = "b" * 64


def valid_plan():
    return {
        "schema_version": preflight.VERSION,
        "source_seed": 42,
        "target_seed": 43,
        "transfer_mode": "FROZEN_NO_REFIT",
        "source_commit_sha": "c" * 40,
        "source_study_sha256": "d" * 64,
        "source_run_id": 1,
        "frozen_models": [
            {"option_id": "T0.95_TF0", "snapshot_sha256": HASH0},
            {"option_id": "TF_ONE_MF_W1_M0_A0.5_T0.95", "snapshot_sha256": HASH1},
        ],
        "target_fdr_max": 0.01,
        "test_sealed": True,
        "margins": [0.5, 2, 4],
    }


class FrozenTransferContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.snapshots = pathlib.Path(self.tmp.name)
        for value in (HASH0, HASH1):
            (self.snapshots / (value + ".fitted.json")).write_text("{}", encoding="utf-8")
        self.hash_patch = patch.object(
            preflight, "sha256", side_effect=lambda path: pathlib.Path(path).name.split(".")[0]
        )
        self.hash_patch.start()
        self.addCleanup(self.hash_patch.stop)

    def test_valid_frozen_plan(self):
        preflight.check_plan(valid_plan(), self.snapshots)

    def test_rejects_protocol_mutations(self):
        mutations = [
            ("target_seed", 42),
            ("source_seed", 43),
            ("transfer_mode", "REFIT"),
            ("test_sealed", False),
            ("target_fdr_max", 0.02),
            ("target_fdr_max", 1),
            ("margins", [0.5, 2]),
            ("source_run_id", 0),
            ("source_commit_sha", "invalid"),
        ]
        for field, value in mutations:
            with self.subTest(field=field, value=value):
                plan = valid_plan()
                plan[field] = value
                with self.assertRaises(ValueError):
                    preflight.check_plan(plan, self.snapshots)

    def test_rejects_missing_extra_or_duplicate_models(self):
        for mutate in (
            lambda p: p.pop("test_sealed"),
            lambda p: p.update({"unexpected": True}),
            lambda p: p["frozen_models"].append(p["frozen_models"][0]),
            lambda p: p["frozen_models"][1].update({"option_id": "T0.95_TF0"}),
        ):
            plan = valid_plan()
            mutate(plan)
            with self.assertRaises(ValueError):
                preflight.check_plan(plan, self.snapshots)

    def test_rejects_missing_or_modified_snapshot(self):
        (self.snapshots / (HASH1 + ".fitted.json")).unlink()
        with self.assertRaises(ValueError):
            preflight.check_plan(valid_plan(), self.snapshots)
        (self.snapshots / (HASH1 + ".fitted.json")).write_text("{}", encoding="utf-8")
        with patch.object(preflight, "sha256", return_value="0" * 64):
            with self.assertRaises(ValueError):
                preflight.check_plan(valid_plan(), self.snapshots)


if __name__ == "__main__":
    unittest.main()

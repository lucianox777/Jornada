#!/usr/bin/env python3
"""Reproduce aggregate seed43 paired Hoeffding gate; diagnostic, NOT certification.

No TEST, CSV, SQL, model selection, or inferential substitution.
"""
import argparse
import math
import unittest
import hashlib
import json
import shutil
import tempfile
from pathlib import Path

CAP = 4
ALPHA = 0.025
IDENTITIES = 5492
ELIGIBLE = 7704
DELTA_TP = 35


def hoeffding_lower(n, delta_tp, cap=CAP, alpha=ALPHA):
    if type(n) is not int or n < 1 or type(delta_tp) is not int:
        raise ValueError("n must be positive integer and delta_tp integer")
    if type(cap) is not int or cap < 1 or not 0 < alpha < 1:
        raise ValueError("invalid cap or alpha")
    if abs(delta_tp) > n * cap:
        raise ValueError("difference outside declared per-identity range")
    mean = delta_tp / n
    penalty = 2 * cap * math.sqrt(math.log(1 / alpha) / (2 * n))
    return mean - penalty


def planning_n(effect_per_identity, cap=CAP, alpha=ALPHA):
    """Conditional lower bound n for Hoeffding > 0 at a FIXED effect assumption."""
    if not math.isfinite(effect_per_identity) or not 0 < effect_per_identity <= cap:
        raise ValueError("positive finite effect <= cap required")
    return math.floor(2 * cap * cap * math.log(1 / alpha) / (effect_per_identity ** 2)) + 1



# Archived 20/10 V9 TF0 evidence is a *different* frozen experiment from
# TF0 x ONE_MF seed43. This is a metadata/hash audit, not a physical replay.
BASELINE_FIT = "0a3459585fcf3d20fbd0854e977b84a769127a9ab01ab0d72417dff42d0b3e5b"
BASELINE_SELECTION = "b98121f9b4854e0db56026df1ddaef30e0f8c32717baa88a68549bd0ad08d75a"
BASELINE_SOURCE_CSV = "28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17"
BASELINE_DIR = Path(__file__).resolve().parents[1] / "data/calibration/v9/mass-support-20261020"
BASELINE_AUDIT_FILES = ("final-study-report.json", "selection-reference.json",
                        BASELINE_FIT + ".fitted.json")


def verify_historical_v9_tf0_artifact(root=BASELINE_DIR):
    """Read only archived aggregate evidence and verify SHA/metadata.

    No raw corpus, SQL, TEST, re-calibration, selection or certification.
    This does *not* reproduce the 97.4754% with the current V9 code.
    """
    root = Path(root)
    manifest = json.loads((root / "evidence-manifest.json").read_bytes())
    if manifest.get("certified") is not False or manifest.get("activated") is not False \
            or manifest.get("test_used") is not False \
            or manifest.get("selection_sha256") != BASELINE_SELECTION:
        raise ValueError("historical baseline manifest no longer matches archived gate")

    loaded = {}
    for filename in BASELINE_AUDIT_FILES:
        expected = manifest["files"][filename]
        data = (root / filename).read_bytes()
        if len(data) != expected["bytes"] \
                or hashlib.sha256(data).hexdigest() != expected["sha256"]:
            raise ValueError("archived V9 TF0 evidence integrity mismatch: " + filename)
        loaded[filename] = json.loads(data)

    report = loaded["final-study-report.json"]
    reference = loaded["selection-reference.json"]
    snapshot = loaded[BASELINE_FIT + ".fitted.json"]
    finalists = report.get("finalists", [])
    if len(finalists) != 1:
        raise ValueError("historical V9 TF0 has no unique final candidate")
    winner = finalists[0]
    if (report.get("scope") != "TRAIN_SELECTION_COMPARISON_NOT_CERTIFICATION"
            or report.get("source_csv_sha256") != BASELINE_SOURCE_CSV
            or report.get("selection_sha256") != BASELINE_SELECTION
            or report.get("evaluations") != 926
            or report.get("identity_split_counts") != {
                "TRAIN_ADJUST": 11673, "TRAIN_SELECTION": 5003}
            or report.get("certified") is not False
            or report.get("test_used") is not False
            or report.get("activated") is not False
            or reference.get("envelopeSha256") != BASELINE_SELECTION
            or reference.get("FinalistSnapshotShas") != [BASELINE_FIT]
            or reference.get("activated") is not False
            or winner.get("snapshot_sha256") != BASELINE_FIT
            or winner.get("eligible_sources") != 7011
            or winner.get("tp") != 6834
            or winner.get("fp") != 0
            or len(winner.get("passes", [])) != 8
            or not math.isclose(winner.get("recall", -1), 6834 / 7011,
                                rel_tol=0, abs_tol=1e-14)
            or snapshot.get("SchemaVersion") != "V9_FITTED_SNAPSHOT_V3"
            or snapshot.get("TfWeight") != 0
            or snapshot.get("TfCalibration") is not None
            or snapshot.get("Threshold") != 0.8671875
            or snapshot.get("ConflictMargin") != 0.609375
            or len(snapshot.get("BlockingPasses", [])) != 8
            or len(snapshot.get("JointCells", [])) != 81
            or snapshot.get("CandidateGrouping", {}).get("CandidateGrouping")
                != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            or snapshot.get("JointSupport", {}).get("TrainCorpusSha256")
                != snapshot.get("TrainCorpusSha256")):
        raise ValueError("historical V9 TF0 winner metadata mismatch")
    return {
        "status": "ARCHIVED_V9_TF0_EVIDENCE_AUTHENTICATED_NOT_REPLAYED",
        "snapshot_sha256": BASELINE_FIT,
        "selection_sha256": BASELINE_SELECTION,
        "source_csv_sha256": BASELINE_SOURCE_CSV,
        "train_selection_tp": 6834,
        "train_selection_eligible": 7011,
        "train_selection_fp": 0,
        "recall": 6834 / 7011,
        "blocking_passes": 8,
        "tf_weight": 0,
        "test_used": False,
        "replayed_by_current_v9": False,
        "v8_compatibility_verified": False,
        "certified": False,
    }

class AggregateAuditTests(unittest.TestCase):
    def test_seed43_predeclared_gate_fails(self):
        self.assertLess(hoeffding_lower(IDENTITIES, DELTA_TP), 0)

    def test_delta_recall_is_descriptive(self):
        self.assertAlmostEqual(DELTA_TP / ELIGIBLE, 0.004543094496365524, places=12)

    def test_planning_requires_millions_under_observed_effect(self):
        n = planning_n(DELTA_TP / IDENTITIES)
        self.assertEqual(n, 2_906_494)
        # Keep the prospective mean fixed; rounding future TP to integers can
        # flip this near-zero boundary and is not the planning calculation.
        effect = DELTA_TP / IDENTITIES
        self.assertLessEqual(effect - 2 * CAP * math.sqrt(math.log(1 / ALPHA) / (2 * (n - 1))), 0)
        self.assertGreater(effect - 2 * CAP * math.sqrt(math.log(1 / ALPHA) / (2 * n)), 0)

    def test_archived_v9_tf0_baseline_hashes_and_counts(self):
        result = verify_historical_v9_tf0_artifact()
        self.assertEqual(result["train_selection_tp"], 6834)
        self.assertEqual(result["blocking_passes"], 8)
        self.assertFalse(result["replayed_by_current_v9"])
        self.assertFalse(result["v8_compatibility_verified"])

    def test_archived_v9_tf0_baseline_tamper_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copy2(BASELINE_DIR / "evidence-manifest.json", root)
            for filename in BASELINE_AUDIT_FILES:
                shutil.copy2(BASELINE_DIR / filename, root)
            path = root / "final-study-report.json"
            data = path.read_bytes()
            path.write_bytes(data[:-1] + bytes([data[-1] ^ 1]))
            with self.assertRaisesRegex(ValueError, "integrity mismatch"):
                verify_historical_v9_tf0_artifact(root)

    def test_rejects_invalid_inputs(self):
        for n, d in ((0, 0), (10, 41), (10, 1.5), (True, 1)):
            with self.subTest(n=n, d=d), self.assertRaises(ValueError):
                hoeffding_lower(n, d)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--verify-v9-tf0-baseline", action="store_true")
    args = parser.parse_args()
    if args.verify_v9_tf0_baseline:
        print(json.dumps(verify_historical_v9_tf0_artifact(), sort_keys=True))
        return
    if args.self_test:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(AggregateAuditTests)
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        raise SystemExit(0 if result.wasSuccessful() else 1)
    effect = DELTA_TP / IDENTITIES
    print("SOURCE: run 41925355648, documented VALIDATION aggregate only")
    print(f"n={IDENTITIES}, eligible={ELIGIBLE}, delta_tp={DELTA_TP}, cap={CAP}, alpha={ALPHA}")
    print(f"delta_recall={DELTA_TP / ELIGIBLE:.9f}")
    print(f"hoeffding_lower_mean_per_identity={hoeffding_lower(IDENTITIES, DELTA_TP):.9f}")
    print(f"conditional_planning_n_at_fixed_observed_effect={planning_n(effect)}")
    print("NOT_CERTIFIED: IID across identities unvalidated; historical seed43 not fresh; Gold incomplete")


if __name__ == "__main__":
    main()

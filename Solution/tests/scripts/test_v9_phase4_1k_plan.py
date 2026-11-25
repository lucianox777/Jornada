import importlib.util
import itertools
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location(
    "plan", Path(__file__).resolve().parents[2] / "scripts/v9-phase4-1k-plan.py")
P = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(P)
ATTEMPT = "ad8f4b7a-b521-4cba-b58a-3025d48bc912"
BASE = {"commit": P.BASE_SHA, "winnerSha256": P.WINNER, "counts": P.COUNTS.copy()}


class Phase4PlanTests(unittest.TestCase):
    def test_complete_factorial_and_independent_trajectories(self):
        plan = P.build_plan(BASE, ATTEMPT)
        cells = plan["cells"]
        self.assertEqual(len(cells), 24)
        self.assertEqual(len({c["cell"] for c in cells}), 24)
        self.assertEqual(sum(c["officialBaseline"] for c in cells), 1)
        trajectories = [t for c in cells for t in c["trajectories"]]
        self.assertEqual(len({t["trajectoryId"] for t in trajectories}), 48)
        states = [s for t in trajectories for s in t["rounds"]]
        self.assertEqual(len({s["stateId"] for s in states}), 72)
        for t in trajectories:
            self.assertIsNone(t["rounds"][0]["previousStateId"])
            if len(t["rounds"]) == 2:
                self.assertEqual(t["rounds"][1]["previousStateId"], t["rounds"][0]["stateId"])
        self.assertEqual(plan["executedCells"], 0)
        self.assertFalse(plan["publication"])
        self.assertFalse(plan["executionAuthorizedByThisPlan"])
        self.assertTrue(plan["r2RulesPendingSpecification"])

    def test_shared_input_binding_and_drift(self):
        fixture = {"counts": P.COUNTS.copy(), "waveCount": 2,
                   "inputFingerprint": "a" * 64, "orderedPackagesSha256": "b" * 64,
                   "truthSha256ByWave": ["c" * 64, "d" * 64], "preflightSha256": "e" * 64}
        plan = P.build_plan(BASE, ATTEMPT, fixture)
        self.assertEqual({t["fixtureBinding"] for c in plan["cells"] for t in c["trajectories"]},
                         {plan["fixtureBinding"]})
        fixture["counts"]["sourceVersions"] -= 1
        with self.assertRaises(ValueError):
            P.build_plan(BASE, ATTEMPT, fixture)

    def test_reject_unapproved_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "unapproved.zip"
            path.write_bytes(b"other")
            with self.assertRaises(ValueError):
                P.load_baseline(path)
        with self.assertRaises(ValueError):
            P.build_plan({**BASE, "winnerSha256": "0" * 64}, ATTEMPT)


if __name__ == "__main__":
    unittest.main()

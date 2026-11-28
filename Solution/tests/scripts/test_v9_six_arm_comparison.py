"""Directed tests of the independent measurement oracle."""
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
import unittest

spec = spec_from_file_location("six", Path(__file__).resolve().parents[2] / "scripts/v9-six-arm-comparison.py")
six = module_from_spec(spec)
spec.loader.exec_module(six)


class SixArmTrajectoryTests(unittest.TestCase):
    def test_direct_mask_uses_unmasked_state_for_the_next_round(self):
        state = {1: "gold", 2: "gold", 3: "protected"}
        visible = state.copy()
        visible[1] = None
        row = {"EligibleLeaders": [1], "SuppressedFollowers": []}
        self.assertEqual(six.validate_state("Direct", row, state, visible), ({1}, {1}))
        with self.assertRaises(ValueError):
            six.validate_state("Direct", row, visible, visible)
        # The previously published reference is protected in the next cycle.
        self.assertEqual(six.validate_state("Direct", {"EligibleLeaders": [], "SuppressedFollowers": []},
                                            state, state), (set(), set()))

    def test_origin_veto_reaches_the_next_state_only_for_its_own_origin(self):
        row = {"EligibleLeaders": [1], "SuppressedFollowers": [2], "NewLinks": [
            {"Leader": 1, "Destination": "gold", "Method": "FS_ASSOCIATION", "Versions": [1, 2], "SecureRecords": [3]}]}
        truth = {1: {"base": "b", "origin": 10}, 2: {"base": "b", "origin": 10},
                 3: {"base": "a", "origin": 11}}
        state = {1: None, 2: None, 3: "gold"}
        self.assertEqual(six.validate_state("Origin", row, state, state), ({1}, {1, 2}))
        costs, _ = six.link_cost(row, truth, "Origin")
        self.assertEqual(costs["direct"]["wrong"], 1)
        self.assertEqual(costs["propagated"]["wrong"], 1)
        for i in (1, 2):
            corrupted = state.copy()
            corrupted[i] = "gold"
            with self.assertRaises(ValueError):
                six.validate_state("Origin", row, corrupted, state)
        with self.assertRaises(ValueError):
            six.link_cost(dict(row, SuppressedFollowers=[3]), truth, "Origin")

    def test_pair_delta_retains_offsetting_false_pairs_and_new_wave(self):
        bases = {1: "a", 2: "b", 3: "b", 4: "a"}
        previous = {1: "x", 2: "x", 3: None}
        current = {1: "z", 2: None, 3: "z", 4: "z"}
        self.assertEqual(six.pair_delta(previous, current, bases),
                         {"tpGained": 1, "tpLost": 0, "fpIntroduced": 2, "fpAvoided": 1})
        self.assertNotEqual(six.partition(previous), six.partition(current))

    def test_exact_predecessor_and_original_census_coverage(self):
        scripts = Path(six.__file__).parent
        original = (scripts / "v9-phase4-identity-census.sql").read_text()
        multi = (scripts / "v9-six-arm-round-census.sql").read_text()
        start = "-- The whole SQL universe is intentional"
        self.assertEqual(original.split(start)[1], multi.split(start)[1])
        self.assertIn("p.linkage_run_id=@previous", multi)
        self.assertIn("@previous=@run", multi)
        self.assertIn("THEN 1 ELSE 2 END", multi)
        self.assertIn("identidade.linkage_run)<>1", original)

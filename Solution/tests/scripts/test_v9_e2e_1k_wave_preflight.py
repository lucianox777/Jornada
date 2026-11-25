import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location(
    "wave", Path(__file__).resolve().parents[2] / "scripts/v9-e2e-1k-wave-preflight.py")
WAVE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(WAVE)


def fixture():
    return [[("SEHAB", "A", "P1", "D1", True, "H1"),
             ("SMADS", "B", "P1", "D2", False, "H2"),
             ("SMS", "C", "P2", "D3", False, "H3")],
            [("SEHAB", "A", "P1", "D4", True, "H4")]]


class WavePreflightTests(unittest.TestCase):
    def test_stable_cpf_multiversion_and_noncpf_other_origin(self):
        result = WAVE.characterize(fixture(), {"P1", "P2"})
        self.assertEqual(result["cpfMultiversionOrigins"], 1)
        self.assertEqual(result["nonCpfOriginsWithOtherCpfOrigin"], 1)
        self.assertEqual(result["sourceVersions"], 4)
        self.assertEqual(result["persistentOrigins"], 3)

    def test_no_change_is_not_a_new_version(self):
        rows = fixture()
        rows[1][0] = ("SEHAB", "A", "P1", "D4", True, "H1")
        with self.assertRaisesRegex(ValueError, "unchanged"):
            WAVE.characterize(rows, {"P1", "P2"})

    def test_missing_required_cpf_multiversions(self):
        rows = fixture()
        rows[0][0] = ("SEHAB", "A", "P1", "D1", False, "H1")
        with self.assertRaisesRegex(ValueError, "Missing CPF"):
            WAVE.characterize(rows, {"P1", "P2"})

    def test_origin_drift(self):
        rows = fixture()
        rows[1][0] = ("SEHAB", "NEW", "P1", "D4", True, "H4")
        with self.assertRaisesRegex(ValueError, "identity drift"):
            WAVE.characterize(rows, {"P1", "P2"})

    def test_population_census(self):
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            WAVE.characterize(fixture(), {"P1", "P2", "P3"})


if __name__ == "__main__":
    unittest.main()

"""Deterministic derivation and global one-use TEST seed reservation regression."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
import uuid
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("seed_reservation", ROOT / "scripts/v9-reserve-test-seed.py")
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)
GUID = uuid.UUID("b5947f2c-17b8-46b3-9e17-71653d774a09")
SHA = "a" * 40


class SeedReservationTests(unittest.TestCase):
    def test_deterministic_64_bit_seed(self):
        seed = mod.derive_seed(GUID)
        self.assertEqual(seed, mod.derive_seed(GUID))
        self.assertTrue(0 < seed < 2**64)
        self.assertNotIn(seed, mod.RESERVED)

    @patch.object(mod.uuid, "uuid4", return_value=GUID)
    @patch.object(mod, "gh", return_value=subprocess.CompletedProcess([], 0, "{}", ""))
    def test_successful_atomic_reservation(self, call, _guid):
        receipt = mod.reserve("lucianox777/Jornada", SHA, "123", "1")
        self.assertEqual(receipt["seed"], mod.derive_seed(GUID))
        self.assertEqual(receipt["executionGuid"], str(GUID))
        self.assertIn(str(receipt["seed"]), receipt["exposureRef"])
        self.assertEqual(call.call_count, 1)

    @patch.object(mod.uuid, "uuid4", return_value=GUID)
    @patch.object(mod, "gh")
    def test_permission_error_never_treated_as_collision(self, call, _guid):
        call.side_effect = [subprocess.CompletedProcess([], 1, "", "forbidden"),
                            subprocess.CompletedProcess([], 1, "", "not found")]
        with self.assertRaisesRegex(RuntimeError, "NOT_COLLISION"):
            mod.reserve("lucianox777/Jornada", SHA, "123", "1")

    def test_rerun_rejected(self):
        with self.assertRaisesRegex(RuntimeError, "ATTEMPT"):
            mod.reserve("lucianox777/Jornada", SHA, "123", "2")


if __name__ == "__main__":
    unittest.main()

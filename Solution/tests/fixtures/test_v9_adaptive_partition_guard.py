import importlib.util
import pathlib
import unittest

PATH = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-adaptive-frequency-planner.py"
spec = importlib.util.spec_from_file_location("v9_adaptive_guard", PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def row(partition, identity):
    return {"particao": partition, "base_person_id": identity,
            "nome_normalizado": "ANA", "nome_fonetico": "AN",
            "nome_mae_normalizado": "LIA", "nome_mae_fonetico": "L",
            "data_nascimento": "2000-01-01"}

class AdaptivePartitionGuardTests(unittest.TestCase):
    def test_identity_cannot_cross_train_and_test(self):
        with self.assertRaisesRegex(ValueError, "leaked"):
            module.train_counts([row("TRAIN", "p1"), row("TEST", "p1")])

    def test_identity_cannot_cross_validation_and_test(self):
        with self.assertRaisesRegex(ValueError, "leaked"):
            module.train_counts([row("VALIDATION", "p1"), row("TEST", "p1")])

    def test_invalid_partition_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "partition"):
            module.train_counts([row("UNKNOWN", "p1")])

    def test_missing_identity_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "base_person_id"):
            module.train_counts([row("TRAIN", "")])

    def test_distinct_train_identities_and_no_test_training(self):
        counts = module.train_counts([row("TRAIN", "p1"), row("TRAIN", "p1"),
                                      row("TRAIN", "p2"), row("TEST", "p3")])
        self.assertEqual(counts[("nome_normalizado", "data_nascimento")][("ANA", "2000-01-01")], 2)

if __name__ == "__main__":
    unittest.main()

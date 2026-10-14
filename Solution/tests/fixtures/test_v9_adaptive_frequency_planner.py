import importlib.util
import pathlib
import unittest

PATH = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-adaptive-frequency-planner.py"
spec = importlib.util.spec_from_file_location("v9_adaptive", PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def row(split, truth, name="ANA", mother="LIA", birth="2000"):
    return {"particao": split, "base_person_id": truth, "observacao_id": str(truth) + "-" + split,
            "nome_normalizado": name, "nome_fonetico": "AN",
            "nome_mae_normalizado": mother, "nome_mae_fonetico": "L",
            "data_nascimento": birth}

class AdaptivePlannerTests(unittest.TestCase):
    def test_train_only_and_deduplicated_truth(self):
        rows = [row("TRAIN", "a"), row("TRAIN", "a"), row("TEST", "b")]
        counts = module.train_counts(rows)
        self.assertEqual(counts[("nome_normalizado", "data_nascimento")][("ANA", "2000")], 1)
        self.assertEqual(len(module.plan(rows[2], counts)), 3)

    def test_missing_independent_fields_never_create_single_field_pass(self):
        rows = [dict(row("TRAIN", "a", mother="", birth=""), nome_mae_fonetico="")]
        counts = module.train_counts(rows)
        self.assertEqual(module.plan(rows[0], counts), [])

    def test_rare_joint_key_precedes_frequent(self):
        rows = [row("TRAIN", "a", birth="2000"), row("TRAIN", "b", birth="2001")]
        counts = module.train_counts(rows)
        selected = module.plan(rows[0], counts)
        self.assertEqual(selected[0]["train_identity_frequency"], 1)
        self.assertTrue(all(item["train_identity_frequency"] == 1 for item in selected))
        self.assertEqual(counts[("nome_normalizado", "nome_mae_normalizado")][("ANA", "LIA")], 2)

    def test_reject_raw_only_input(self):
        with self.assertRaisesRegex(ValueError, "canonical"):
            module.train_counts([{"particao": "TRAIN", "base_person_id": "a", "person_full_raw": "ANA"}])

if __name__ == "__main__":
    unittest.main()

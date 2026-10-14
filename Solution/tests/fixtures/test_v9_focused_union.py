import importlib.util
import pathlib
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-focused-union-comparison.py"
spec = importlib.util.spec_from_file_location("focused_union", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def row(oid, person, manager, name="ANA SILVA", mother="MARIA SOUZA", split="TRAIN"):
    return dict(observacao_id=oid, base_person_id=person, particao=split,
                gestor=manager, cpf="", nome_normalizado=name,
                nome_fonetico=name, nome_mae_normalizado=mother,
                nome_mae_fonetico=mother, data_nascimento="1990-01-01")

class FocusedUnionTests(unittest.TestCase):
    def test_union_retrieves_cross_manager_truth(self):
        report=module.evaluate([row("a","p","A"),row("b","p","B")])
        metrics=report["TRAIN"]["metrics"]
        self.assertEqual(metrics["UNIAO_5"]["true_pairs_retrieved"],1)
        self.assertEqual(metrics["UNIAO_5"]["recall"],1.0)
        self.assertIsNone(metrics["UNIAO_5"]["fdr"])

    def test_partition_leakage_rejected(self):
        with self.assertRaisesRegex(ValueError,"leakage"):
            module.evaluate([row("a","p","A"),row("b","p","B",split="TEST")])

    def test_p4_any_maternal_surname(self):
        rows=[row("a","p","A",mother="MARIA SOUZA LIMA"),
              row("b","p","B",mother="MARIA SOUZA COSTA")]
        self.assertEqual(module.evaluate(rows)["TRAIN"]["metrics"]["P4"]["true_pairs_retrieved"],1)

    def test_person_name_is_mandatory_even_when_mother_and_birth_are_present(self):
        rows = [row("a", "p", "A", name="NÃO INFORMADO"),
                row("b", "p", "B", name="NÃO INFORMADO")]
        actual = module.evaluate(rows)["TRAIN"]
        self.assertEqual(actual["all_no_cpf_true_pairs"], 1)
        self.assertEqual(actual["name_eligible_true_pairs"], 0)
        self.assertEqual(actual["abstaining_observations"], 2)
        self.assertIsNone(actual["metrics"]["UNIAO_5"]["recall"])
        self.assertEqual(actual["metrics"]["UNIAO_5"]["candidate_pairs"], 0)

    def test_person_and_mother_are_eligible_even_when_birth_is_absent(self):
        rows = [row("a", "p", "A"), row("b", "p", "B")]
        for r in rows:
            r["data_nascimento"] = ""
        actual = module.evaluate(rows)["TRAIN"]
        self.assertEqual(actual["name_eligible_true_pairs"], 1)
        self.assertEqual(actual["metrics"]["P4"]["true_pairs_retrieved"], 1)
        self.assertEqual(actual["metrics"]["P2"]["candidate_pairs"], 0)
        self.assertEqual(actual["metrics"]["P5"]["candidate_pairs"], 0)

    def test_sentinel_birth_not_evidence_and_cpf_route_separate(self):
        rows = [row("a", "p", "A", mother=""), row("b", "p", "B", mother="")]
        for r in rows:
            r["data_nascimento"] = "1900-01-01"
        self.assertEqual(module.evaluate(rows)["TRAIN"]["name_eligible_true_pairs"], 0)
        rows[1]["cpf"] = "12345678909"
        self.assertEqual(module.evaluate(rows)["TRAIN"]["observations_without_cpf"], 1)

    def test_any_subset_claim_is_disclosed_on_complete_truth_denominator(self):
        rows = [row("a", "p", "A"), row("b", "p", "B", name="")]
        actual = module.evaluate(rows)["TRAIN"]
        self.assertEqual(actual["all_no_cpf_true_pairs"], 1)
        self.assertEqual(actual["name_eligible_true_pairs"], 0)
        self.assertEqual(actual["eligibility_ceiling_over_all_truth"], 0)

if __name__=="__main__": unittest.main()

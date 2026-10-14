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

if __name__=="__main__": unittest.main()

"""Trainer discovers NAME-prefix parameters from actual TRAIN pair overlap."""
import importlib.util
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/"scripts"
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,ROOT/path)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

p=load("v9_prefix_parameter_planner_test","v9-prefix-parameter-planner.py")
base=load("v9_canonical_memory_baseline_test","v9-sqlite-memory-cells.py")
SHA="a"*64

def row(i,b,part,gestor):
    return {"observacao_id":i,"base_person_id":b,"particao":part,
        "gestor":gestor,"cpf":"","nome_normalizado":"MARIA SILVA",
        "nome_mae_normalizado":"ANA LIMA","data_nascimento":"1990-02-11",
        "nome_fonetico":"MR","nome_mae_fonetico":"AN"}

def setup_fixture():
    rows=[]
    for part in ("TRAIN","VALIDATION"):
        for id,person,owner in (("a","p","G1"),("b","p","G2"),
                                ("c","q","G1"),("d","q","G2"),
                                ("x","nonmatch1","G1"),
                                ("y","nonmatch2","G2")):
            rows.append(row(part+id,part+person,part,owner))
    baseline = {(split,c):set() for split in ("TRAIN","VALIDATION")
                for c in base.EXPANDED}
    for split in ("TRAIN","VALIDATION"):
        baseline[split,"P_PHON_BIRTH_YEAR"]={(split+"a",split+"b")}
    candidates={}
    for split in ("TRAIN","VALIDATION"):
        candidates[split,"P4_DOB"]={(split+"c",split+"d")}
        candidates[split,"P2_M1_YEAR_MONTH"]={
            (split+"c",split+"d"),(split+"x",split+"y")}
    return rows,baseline,candidates,["P4_DOB","P2_M1_YEAR_MONTH"]

def budget(max_u):
    return {"version":p.VERSION,"canonical_sha256":SHA,
        "minimum_global_point_recall":0.95,
        "max_incremental_u_train":max_u,
        "max_u_degree_p95_train":100,
        "max_u_degree_p99_train":100}

class AdaptivePrefixTests(unittest.TestCase):
    def test_training_frontier_automatically_discovers_lowest_U_not_fixed_prefix(self):
        rows,b,c,names=setup_fixture()
        result=p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA)
        self.assertEqual(result["status"],"NO_APPROVED_BUDGET_NO_SELECTION")
        self.assertEqual(result["TRAIN_single_add_on_pareto"],["P4_DOB"])
        self.assertIsNone(result["selected_add_on"])
        self.assertFalse(result["may_promote_policy"])
        self.assertFalse(result["city_scale_certified"])

    def test_external_budget_selects_on_training_only_not_validation(self):
        rows,b,c,names=setup_fixture()
        c["VALIDATION","P4_DOB"]=set()
        result=p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA,
                          policy=budget(0))
        self.assertEqual(result["selected_add_on"],"P4_DOB")
        self.assertTrue(result["validation_refit"] is False)
        self.assertEqual(result["result_by_split"]["TRAIN"]["variants"]["P4_DOB"]["M"],2)
        self.assertEqual(result["selected_validation_frozen"]["M"],1)
        self.assertFalse(result["TEST_evaluated"])
        self.assertNotIn("train_pii",str(result))

    def test_no_feasible_budget_fails_closed_and_incorrect_source_rejected(self):
        rows,b,c,names=setup_fixture()
        policy=budget(0)
        policy["max_u_degree_p99_train"]=0
        # delta U zero and baseline has no false candidates; this is still
        # feasible, so use impossible minimum p95 instead via U in fixture.
        result=p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA,
                          policy=policy)
        self.assertEqual(result["selected_add_on"],"P4_DOB")
        policy["canonical_sha256"]="b"*64
        with self.assertRaisesRegex(ValueError,"SHA-bound"):
            p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA,
                       policy=policy)
        policy=budget(0)
        c["TRAIN","P4_DOB"]={( "TRAINc","TRAINd"),
                             ("TRAINx","TRAINy")}
        no=p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA,
                      policy=policy)
        self.assertEqual(no["status"],"NO_TRAIN_FEASIBLE_ADD_ON")
        self.assertIsNone(no["selected_add_on"])

    def test_train_and_validation_marginals_count_both_U_endpoints(self):
        rows,b,c,names=setup_fixture()
        result=p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],b,c,names,SHA)
        train=result["result_by_split"]["TRAIN"]["variants"]["P2_M1_YEAR_MONTH"]
        self.assertEqual(train["delta_M"],1)
        self.assertEqual(train["delta_U"],1)
        self.assertEqual(train["M"],2)
        self.assertEqual(train["U"],1)
        self.assertAlmostEqual(train["u_degree_mean"],2/6)

    def test_truth_and_selected_base_must_be_consistent(self):
        rows,b,c,names=setup_fixture()
        with self.assertRaisesRegex(ValueError,"frozen original"):
            p.evaluate(rows,base,["UNKNOWN"],b,c,names,SHA)
        bad=b.copy()
        del bad["VALIDATION","P_PHON_BIRTH_YEAR"]
        with self.assertRaises(KeyError):
            p.evaluate(rows,base,["P_PHON_BIRTH_YEAR"],bad,c,names,SHA)

if __name__=="__main__":
    unittest.main()

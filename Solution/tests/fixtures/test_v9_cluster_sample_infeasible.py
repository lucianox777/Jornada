"""A source with infeasible 95% recall remains reportable, not promotable."""
import importlib.util
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/"scripts"
def load(name,filename):
    spec=importlib.util.spec_from_file_location(name,ROOT/filename)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
m=load("v9_size_infeasible_runner","v9-cluster-sample-size-power.py")
base=load("v9_size_infeasible_baseline","v9-sqlite-memory-cells.py")
SHA="c"*64

def row(oid,person_id,split,manager,p,mother,phon,date):
    return {"observacao_id":oid,"base_person_id":person_id,
            "particao":split,"gestor":manager,"cpf":"",
            "nome_normalizado":p,"nome_fonetico":phon,
            "nome_mae_normalizado":mother,"nome_mae_fonetico":phon,
            "data_nascimento":date}

class InfeasibleEvidenceTests(unittest.TestCase):
    def test_adversarial_split_is_reported_without_selected_plan(self):
        rows=[]
        for part in ("TRAIN","VALIDATION"):
            rows.extend([
                row(part+"a",part+"p",part,"G1","MARIA SILVA","ANA LIMA","MAR","1980-04-01"),
                row(part+"b",part+"p",part,"G2","MARIA SILVA","ANA LIMA","MAR","1980-04-01"),
                row(part+"c",part+"q",part,"G1","ALICE SOUZA","ANA LIMA","ALC","1981-05-02"),
                row(part+"d",part+"q",part,"G2","BEATRIZ LIMA","ZOE MORAIS","BTR","1982-08-03"),
            ])
        rows.extend([
            row("t1","t","TEST","G1","ANA SILVA","ANA SOUZA","ANA","1990-01-03"),
            row("t2","t","TEST","G2","ANA SILVA","ANA SOUZA","ANA","1990-01-03")])
        evidence={
            "canonical_observations_sha256":SHA,
            "selection_split":"TRAIN_ONLY","TEST_evaluated":False,
            "diagnostic_only":True,"may_promote_policy":False,
            "minimum_U_95pct_diagnostic":{
                "trained_on":"TRAIN_ONLY","validation_refit":False,
                "status":"TRAIN_TARGET_INFEASIBLE",
                "plans":{"greedy_pruned":None}},
            "results":{
                part:{"all_no_cpf_truth_M":2,
                      "metrics":{"UNION_EXPANDED":{"M":1}}}
                for part in ("TRAIN","VALIDATION")
            },
        }
        out=m.analyze(rows,evidence,base,SHA,seed=44,rounds=200)
        self.assertEqual(out["evaluated_plan_kind"],
                         "UNSELECTED_FULL_19_DIAGNOSTIC_INFEASIBLE_TRAIN")
        self.assertFalse(out["original_train_95pct_plan_feasible"])
        for part in ("TRAIN","VALIDATION"):
            full=out["split_results"][part]["sample_sizes"][-1]
            self.assertEqual(full["retrieved_M"],1)
            self.assertEqual(full["true_M_pairs"],2)
            self.assertFalse(full["cluster_lower_at_least_95"])
        self.assertFalse(out["may_promote_policy"])
        evidence["minimum_U_95pct_diagnostic"]["status"]="TRAIN_TARGET_FEASIBLE"
        with self.assertRaisesRegex(ValueError,"non-feasible baseline"):
            m.analyze(rows,evidence,base,SHA,seed=44,rounds=200)

if __name__=="__main__":
    unittest.main()

"""V9 synthetic sample-size: truth-bearing PERSON clusters and honest power."""
import importlib.util
import math
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/"scripts"
def load(name,filename):
    spec=importlib.util.spec_from_file_location(name,ROOT/filename)
    m=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m
m=load("v9_test_sample_size_power","v9-cluster-sample-size-power.py")
base=load("v9_test_size_base","v9-sqlite-memory-cells.py")
SHA="c"*64

def row(oid, identity, manager, split, person="MARIA SILVA",
        mother="ANA LIMA",dob="1980-04-01"):
    return {
        "observacao_id":oid,"base_person_id":identity,
        "gestor":manager,"particao":split,"cpf":"",
        "nome_normalizado":person,"nome_fonetico":"MR",
        "nome_mae_normalizado":mother,"nome_mae_fonetico":"AN",
        "data_nascimento":dob}

def example():
    rs=[]
    for split in ("TRAIN","VALIDATION"):
        rs.extend([
            row(split+"a",split+"p","G1",split),
            row(split+"b",split+"p","G2",split),
            row(split+"c",split+"q","G1",split,person="ALICE"),
            row(split+"d",split+"q","G2",split,person="ALICE",dob="1980-04-02"),
            row(split+"e",split+"x","G1",split)])
    rs.extend([
        row("test1","test-person","G1","TEST"),
        row("test2","test-person","G2","TEST")])
    report={
        "TEST_evaluated":False,"selection_split":"TRAIN_ONLY",
        "diagnostic_only":True,"may_promote_policy":False,
        "canonical_observations_sha256":SHA,
        "minimum_U_95pct_diagnostic":{
            "trained_on":"TRAIN_ONLY","validation_refit":False,
            "plans":{"greedy_pruned":{
                "selected_cells":["P_PREFIX3_DOB"],
                "measured":{p:{"all_no_cpf_truth_M":2,"M":1}
                            for p in ("TRAIN","VALIDATION")}}}}}
    return rs,report

class SampleSizeTests(unittest.TestCase):
    def test_true_global_M_counts_unrecoverable_ineligible_and_TEST_blind(self):
        rs,report=example()
        result=m.analyze(rs,report,base,SHA,seed=42,rounds=200)
        self.assertEqual(result["independent_repetitions_measured"],1)
        self.assertFalse(result["TEST_evaluated"])
        self.assertFalse(result["may_promote_policy"])
        self.assertFalse(result["perfect_sample_claimed"])
        for split in ("TRAIN","VALIDATION"):
            final=result["split_results"][split]["sample_sizes"][-1]
            self.assertEqual(final["match_bearing_base_identities"],2)
            self.assertEqual(final["true_M_pairs"],2)
            self.assertEqual(final["retrieved_M"],1)
            self.assertEqual(final["global_recall"],.5)
            self.assertFalse(final["cluster_lower_at_least_95"])
            self.assertEqual(final["population_sample_kind"],
                "MATCH_BEARING_BASE_IDENTITIES_NOT_OBSERVATIONS")
        self.assertNotIn("test-person",str(result))
        self.assertNotIn("MARIA SILVA",str(result))

    def test_bootstrap_is_seeded_and_identity_not_pair(self):
        clusters=[(3,3),(8,0),(2,2),(6,6)]
        a=m.bootstrap_lower(clusters,500,72)
        b=m.bootstrap_lower(clusters,500,72)
        self.assertEqual(a,b)
        self.assertGreaterEqual(a,0)
        self.assertLessEqual(a,1)
        self.assertEqual(m.summary(clusters,400,12)["true_M_pairs"],19)
        self.assertIsNotNone(m.summary(clusters,400,12)["delta_method_precision"])
        with self.assertRaisesRegex(ValueError,"bootstrap"):
            m.bootstrap_lower(clusters,20,1)

    def test_h0_95_cannot_be_certified_with_high_power_at_true_95(self):
        effect=m.planned_effective_pairs()
        self.assertGreater(effect["0.955"],effect["0.96"])
        self.assertGreater(effect["0.96"],effect["0.97"])
        self.assertGreater(effect["0.97"],effect["0.98"])
        with self.assertRaisesRegex(ValueError,"exceed null"):
            m.planned_effective_pairs(plausible=(.95,))
        self.assertTrue(all(x>0 for x in effect.values()))

    def test_no_leakage_no_selection_via_validation_or_unverified_plan(self):
        rs,report=example()
        with self.assertRaisesRegex(ValueError,"SHA mismatch"):
            m.analyze(rs,report,base,"a"*64,seed=42,rounds=200)
        report["TEST_evaluated"]=True
        with self.assertRaisesRegex(ValueError,"TEST-blind"):
            m.analyze(rs,report,base,SHA,seed=42,rounds=200)
        rs,report=example()
        report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"][
            "selected_cells"]=["NOT_A_PASS"]
        with self.assertRaisesRegex(ValueError,"selected"):
            m.analyze(rs,report,base,SHA,seed=42,rounds=200)
        rs,report=example()
        rs[-1]["base_person_id"]="TRAINp"
        with self.assertRaisesRegex(ValueError,"leakage"):
            m.analyze(rs,report,base,SHA,seed=42,rounds=200)

    def test_never_hide_regression_against_actual_SQL_reference(self):
        rs,report=example()
        report["minimum_U_95pct_diagnostic"]["plans"]["greedy_pruned"][
            "measured"]["TRAIN"]["M"]=2
        with self.assertRaisesRegex(ValueError,"actual frozen indexed SQL"):
            m.analyze(rs,report,base,SHA,seed=42,rounds=200)

if __name__=="__main__":
    unittest.main()

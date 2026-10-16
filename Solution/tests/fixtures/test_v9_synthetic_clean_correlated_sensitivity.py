import importlib.util
import unittest
from pathlib import Path

module=Path(__file__).resolve().parents[2]/"scripts"/"v9-synthetic-clean-correlated-sensitivity.py"
spec=importlib.util.spec_from_file_location("v9_sensitivity_comparison",module)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

def fixtures():
    x,y={},{}
    for p in ("clean","correlated"):
        info={"M":95,"U":20,"u_degree":{"mean":1.0,"p95":4,"p99":7}}
        x[p]={
          "selection_split":"TRAIN_ONLY","TEST_evaluated":False,
          "fdr_measured":False,"may_promote_policy":False,
          "sql_server_p95_measured":False,
          "canonical_observations_sha256":("1" if p=="clean" else "2")*64,
          "minimum_U_95pct_diagnostic":{
              "trained_on":"TRAIN_ONLY","validation_refit":False,
              "plans":{"greedy_pruned":{
                  "selected_cells":["P_PHON_M_LAST"],
                  "measured":{part:info for part in ("TRAIN","VALIDATION")}}}},
          "results":{part:{"all_no_cpf_truth_M":100,
                      "name_eligible_truth_M":98,
                      "name_eligible_observations":40,
                      "metrics":{"UNION_EXPANDED":info}}
                    for part in ("TRAIN","VALIDATION")},
          "independent_base_identity_recall_confidence":{
              part:{"greedy_pruned":{"lower_one_sided_95":0.92}}
              for part in ("TRAIN","VALIDATION")}
        }
        y[p]={
         "error_profile":p,
         "error_profile_provenance":"HYPOTHETICAL_UNCALIBRATED_SYNTHETIC_STRESS_PROFILE",
         "seed":42,"input_fingerprint_sha256":"a"*64,
         "ruleset_version":"V2","rng_version":"XOSHIRO",
         "population_profile":"DEMOGRAPHIC_PRIMARY_V1",
         "demographic_primary":{k:("DEMOGRAPHIC_PRIMARY_V1" if k=="mode" else
             100 if k=="birth_row_count" else "b"*64)
             for k in m.DEMOGRAPHIC_ID_KEYS}}
    return x,y

class CleanCorrelatedSensitivityTests(unittest.TestCase):
    def test_profiles_run_independently_with_same_reference_and_test_blind(self):
        x,y=fixtures()
        result=m.compare(x,y)
        self.assertEqual(result["comparison_kind"],
                         "UNPAIRED_SAME_SEED_AND_DEMOGRAPHIC_REFERENCE")
        self.assertFalse(result["may_promote_v9"])
        self.assertFalse(result["TEST_evaluated"])
        self.assertEqual(result["seed_neutral_demographic_reference"],
                         m.demographic_reference_identity(y["clean"]))
        self.assertEqual(result["profiles"]["clean"]["observed"]["TRAIN"]
                         ["TRAIN_selected_greedy_pruned"]["global_recall"],0.95)
        self.assertEqual(result["profiles"]["correlated"]["observed"]["VALIDATION"]
                         ["bootstrap_lower_one_sided_95_greedy"],0.92)

    def test_reject_different_reference_seed_or_missing_explicit_profile(self):
        x,y=fixtures()
        y["clean"]["input_fingerprint_sha256"]="c"*64
        with self.assertRaisesRegex(ValueError,"different reference or seed"):
            m.compare(x,y)
        x,y=fixtures()
        y["clean"]["error_profile"]="correlated"
        with self.assertRaisesRegex(ValueError,"not bound"):
            m.compare(x,y)
        x,y=fixtures()
        y["clean"]["seed"]=43
        with self.assertRaisesRegex(ValueError,"different reference or seed"):
            m.compare(x,y)

    def test_invalid_candidate_counts_and_fanout_fail_closed(self):
        for bad in (True, -1, 101, 2.5):
            x,y=fixtures()
            x["clean"]["results"]["TRAIN"]["metrics"]["UNION_EXPANDED"]["M"]=bad
            with self.assertRaisesRegex(ValueError,"candidate M/U"):
                m.compare(x,y)
        for bad in (float("nan"), float("inf"), -1):
            x,y=fixtures()
            x["clean"]["results"]["TRAIN"]["metrics"]["UNION_EXPANDED"][
                "u_degree"]["p99"]=bad
            with self.assertRaisesRegex(ValueError,"invalid U fanout"):
                m.compare(x,y)
        x,y=fixtures()
        x["clean"]["results"]["TRAIN"]["name_eligible_truth_M"]=101
        with self.assertRaisesRegex(ValueError,"name eligibility"):
            m.compare(x,y)

    def test_same_raw_corpus_or_test_evidence_forbidden(self):
        x,y=fixtures()
        x["clean"]["canonical_observations_sha256"]=x["correlated"][
            "canonical_observations_sha256"]
        with self.assertRaisesRegex(ValueError,"same corpus"):
            m.compare(x,y)
        x,y=fixtures()
        x["clean"]["TEST_evaluated"]=True
        with self.assertRaisesRegex(ValueError,"TEST-blind"):
            m.compare(x,y)

if __name__=="__main__":
    unittest.main()

"""Fail-closed out-of-sample replay of seed42 blocking choices on seeds43/44."""
import importlib.util
from pathlib import Path
import unittest

SCRIPTS=Path(__file__).resolve().parents[2]/"scripts"

def load(name,path):
    spec=importlib.util.spec_from_file_location(name,SCRIPTS/path)
    value=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value

m=load("v9_cross_seed_plan_replay_test","v9-frozen-cross-seed-plan-transfer.py")
base=load("v9_cross_seed_sql_test","v9-sqlite-memory-cells.py")

def row(oid,identity,part,manager,person="MARIA SILVA",mother="ANA SOUZA",
        date="1981-02-03",cpf=""):
    return {"observacao_id":oid,"base_person_id":identity,
        "particao":part,"gestor":manager,"cpf":cpf,
        "nome_normalizado":person,"nome_fonetico":"MR",
        "nome_mae_normalizado":mother,"nome_mae_fonetico":"AN",
        "data_nascimento":date}

def fixture():
    demographic={k:("DEMOGRAPHIC_PRIMARY_V1" if k=="mode" else
                    111 if k=="birth_row_count" else "d"*64)
                 for k in m.demographic_reference_identity_keys()}
    rows=[]
    for part in ("TRAIN","VALIDATION"):
        rows.extend([
            row(part+"a",part+"p",part,"G1"),
            row(part+"b",part+"p",part,"G2"),
            row(part+"c",part+"noise",part,"G3"),
            row(part+"d",part+"withCPF",part,"G1",cpf="999")])
    rows.extend([
        row("testa","testperson","TEST","G1"),
        row("testb","testperson","TEST","G2")])
    sql=base.assess(rows,cap=100,max_passes=2)
    sql["canonical_observations_sha256"]="b"*64
    source={"seed":42,"TEST_evaluated":False,"may_promote_v9":False,
            "source_reference_sha256":"c"*64,
            "seed_neutral_demographic_reference":demographic,
            "profiles":{k:{"canonical_sha256":"a"*64,
                            "greedy_train_cells":["P_PHON_M_LAST"]}
                        for k in ("clean","correlated")}}
    manifest={"seed":43,"error_profile":"clean",
        "input_fingerprint_sha256":"c"*64,
        "demographic_primary":dict(demographic),
        "error_profile_provenance":
            "HYPOTHETICAL_UNCALIBRATED_SYNTHETIC_STRESS_PROFILE"}
    frozen={"frozen_source_sha256":"b"*64,
            "test_used_for_frequency":False,
            "projection_version":base.PROJECTION}
    return rows,sql,source,manifest,frozen

class CrossSeedReplayTests(unittest.TestCase):
    def test_uses_preselected_42_cells_and_measures_target_train_val(self):
        rows,sql,source,gen,frozen=fixture()
        r=m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)
        self.assertEqual(r["frozen_SOURCE_TRAIN_selected_cells"],["P_PHON_M_LAST"])
        self.assertFalse(r["target_fit_used_to_select_cells"])
        self.assertFalse(r["TEST_evaluated"])
        self.assertFalse(r["may_promote_policy"])
        self.assertFalse(r["accepted_links_or_fdr_measured"])
        for split in ("TRAIN","VALIDATION"):
            info=r["separate_target_train_and_validation_measurements"][split]
            self.assertEqual(info["all_no_cpf_truth_M"],1)
            self.assertEqual(info["M"],1)
            self.assertEqual(info["U"],2)
            self.assertEqual(info["global_M_recall"],1)
            self.assertAlmostEqual(info["u_degree"]["mean"],2*info["U"]/3)
            self.assertFalse(info["cluster_bootstrap"]["population_or_fdr_certification"])
        self.assertNotIn("MARIA SILVA",str(r))
        self.assertNotIn("testperson",str(r))

    def test_missing_source_plan_or_unapproved_seed_is_rejected(self):
        rows,sql,source,gen,frozen=fixture()
        source["profiles"]["clean"]["greedy_train_cells"]=None
        with self.assertRaisesRegex(ValueError,"42-TRAIN"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)
        source["profiles"]["clean"]["greedy_train_cells"]=["P_PHON_M_LAST"]
        with self.assertRaisesRegex(ValueError,"source seed"):
            m.compare(source,sql,gen,frozen,rows,"clean",42,base,cap=100)

    def test_source_target_ibge_sha_and_test_blind(self):
        rows,sql,source,gen,frozen=fixture()
        gen["demographic_primary"]["birth_sha256"]="wrong"
        with self.assertRaisesRegex(ValueError,"IBGE"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)
        gen["demographic_primary"]["birth_sha256"]="d"*64
        sql["TEST_evaluated"]=True
        with self.assertRaisesRegex(ValueError,"snapshot"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)
        sql["TEST_evaluated"]=False
        rows[-1]["base_person_id"]="TRAINp"
        with self.assertRaisesRegex(ValueError,"leakage"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)

    def test_target_sql_cell_M_U_must_match_recomputed_physical_index(self):
        rows,sql,source,gen,frozen=fixture()
        sql["results"]["VALIDATION"]["cells"]["P_PHON_M_LAST"]["U"]+=1
        with self.assertRaisesRegex(ValueError,"diverges from indexed SQL"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=100)

    def test_frozen_cell_overflow_fails_never_silently_truncates(self):
        rows,sql,source,gen,frozen=fixture()
        with self.assertRaisesRegex(ValueError,"cap"):
            m.compare(source,sql,gen,frozen,rows,"clean",43,base,cap=1)

if __name__=="__main__":
    unittest.main()

"""Regressions for real SQLite indexed P(1..4), M(initial) + birth variants."""
import importlib.util
import json
import unittest
from pathlib import Path

P = Path(__file__).resolve().parents[2]/"scripts"/"v9-prefix-mother-initial-sweep.py"
spec = importlib.util.spec_from_file_location("v9_prefix_mother_test",P)
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)


def row(oid, base, gestor, part="TRAIN", person="ALICE SILVA",
        mother="ANA LIMA", dob="1990-02-11", cpf=""):
    return {"observacao_id":oid, "base_person_id":base,
            "particao":part,"gestor":gestor, "nome_normalizado":person,
            "nome_mae_normalizado":mother, "data_nascimento":dob,
            "cpf":cpf, "nome_fonetico":"","nome_mae_fonetico":""}


def corpus():
    return [
        row("a","p","G1",person="ALICE SILVA",mother="ANA LIMA"),
        row("b","p","G2",person="ALENA SILVA",mother="AMANDA LIMA",
            dob="1990-02-12"),
        row("c","q","G1",person="CARLA SOUZA",mother="",dob="1985-04-10"),
        row("d","q","G2",person="CARINA SOUZA",mother="",dob="1985-04-10"),
        row("e","v","G1","VALIDATION",person="ALICE SILVA",mother="ANA LIMA"),
        row("f","v","G2","VALIDATION",person="ALENA SILVA",mother="AMANDA LIMA",
            dob="1990-02-12"),
        row("g","v2","G1","VALIDATION",person="CARLA SOUZA",mother="",dob="1985-04-10"),
        row("h","v2","G2","VALIDATION",person="CARINA SOUZA",mother="",dob="1985-04-10"),
        row("i","test","G1","TEST"),
        row("j","test","G2","TEST"),
    ]


class PrefixMotherInitialSweepTests(unittest.TestCase):
    def test_28_hypotheses_are_preregistered_and_include_one_character(self):
        vs=v.variants()
        self.assertEqual(len(vs),28)
        self.assertIn("P1_M1_YEAR_MONTH",vs)
        self.assertIn("P4_M1_DOB",vs)
        self.assertIn("P3_M1_MONTH_DAY",vs)
        self.assertIn("P3_M3_YEAR_MONTH",vs)

    def test_shorter_P_prefix_recovers_both_errors_without_dob_day_or_full_mother(self):
        r=v.evaluate(corpus())
        tr=r["results"]["TRAIN"]["variants"]
        self.assertEqual(r["results"]["TRAIN"]["all_no_cpf_truth_M"],2)
        self.assertEqual(tr["P3_DOB"]["M"],0)
        self.assertEqual(tr["P2_M1_YEAR_MONTH"]["M"],1)
        self.assertEqual(tr["P3_M1_YEAR_MONTH"]["M"],0)
        self.assertEqual(tr["P1_M1_YEAR_MONTH"]["M"],1)
        self.assertEqual(tr["P2_M1_YEAR_MONTH"]["incremental_M_vs_P3_DOB"],1)
        self.assertEqual(tr["P2_DOB"]["M"],1)
        self.assertEqual(tr["P2_M1_DOB"]["M"],0)
        self.assertEqual(tr["P2_YEAR_MONTH"]["M"],2)  # PM and PD
        self.assertEqual(r["results"]["VALIDATION"]["variants"]["P2_YEAR_MONTH"]["M"],2)
        self.assertFalse(r["may_promote_policy"])
        self.assertFalse(r["TEST_evaluated"])
        self.assertNotIn("ALICE SILVA",json.dumps(r))
        self.assertNotIn('"a"',json.dumps(r))

    def test_cap_is_recorded_as_unmeasured_not_truncated(self):
        rows=corpus()
        rows.insert(2,row("another","other","G3",person="ALMA OLIVEIRA",
                          mother="ANA LIMA",dob="1990-02-13"))
        r=v.evaluate(rows,cap=1)
        self.assertEqual(r["results"]["TRAIN"]["variants"]["P1_YEAR_MONTH"]["status"],
                         "CAP_EXCEEDED_UNMEASURED")
        self.assertIsNone(r["results"]["TRAIN"]["variants"]["P1_YEAR_MONTH"]["M"])

    def test_eligibility_and_testing_split_fail_closed(self):
        r=row("noMother","p","G1",mother="",dob="1991-10-10")
        self.assertIsNone(v.single_key(r,v.variants()["P2_M1_YEAR_MONTH"]))
        self.assertIsNotNone(v.single_key(r,v.variants()["P2_YEAR_MONTH"]))
        r["data_nascimento"]="1900-01-01"
        self.assertIsNone(v.single_key(r,v.variants()["P2_YEAR_MONTH"]))
        cross=corpus()
        cross[-1]["base_person_id"]="p"
        with self.assertRaisesRegex(ValueError,"leakage"):
            v.evaluate(cross)

    def test_u_degree_counts_both_endpoints(self):
        rows=corpus()
        rows.insert(2,row("false","extra","G3",person="ALICE SOUZA",
                          mother="ANA SOUZA",dob="1990-02-13"))
        report=v.evaluate(rows)
        info=report["results"]["TRAIN"]["variants"]["P1_M1_YEAR_MONTH"]
        self.assertGreater(info["U"],0)
        n=report["results"]["TRAIN"]["name_eligible_observations"]
        self.assertAlmostEqual(info["u_degree"]["mean"],2*info["U"]/n)

if __name__=="__main__":
    unittest.main()

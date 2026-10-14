"""Same-frame ABCD proxy-vs-real-frequency-adaptive indexed SQL comparison."""
import importlib.util
import json
from pathlib import Path
import unittest

SCRIPTS=Path(__file__).resolve().parents[2]/"scripts"
def load(name, file):
    spec=importlib.util.spec_from_file_location(name,SCRIPTS/file)
    obj=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj

main=load("v9_abcd_composition_test","v9-abcd-adaptive-physical-comparison.py")
abc=load("v9_abcd_fixed_test","v9-abc-paired-blocking.py")
base=load("v9_memory_for_abcd_test","v9-sqlite-memory-cells.py")
planner=load("v9_freq_for_abcd_test","v9-adaptive-frequency-planner.py")

def row(id,person,manager,split="TRAIN",pname="MARIA SILVA",
        mother="ANA SOUZA",dob="1980-02-03",pfn="MRS",
        mfn="ANS",cpf=""):
    return {"observacao_id":id,"base_person_id":person,"gestor":manager,
       "particao":split,"cpf":cpf,
       "nome_normalizado":pname,"nome_fonetico":pfn,
       "nome_mae_normalizado":mother,"nome_mae_fonetico":mfn,
       "data_nascimento":dob}

def evidence():
    rows=[]
    for split in ("TRAIN","VALIDATION"):
        rows.extend([
            row(split+"a",split+"p","G1",split),
            row(split+"b",split+"p","G2",split,
                pname="MARYA SILVA",pfn="MRS"),
            row(split+"c",split+"q","G3",split,
                pname="MARIA SILVA",pfn="MRS"),
            row(split+"d",split+"r","G1",split,
                pname="ALICE COSTA",pfn="ALS",mother="",mfn="",dob="1977-10-12"),
            row(split+"e",split+"r","G2",split,
                pname="ALICE COSTA",pfn="ALS",mother="",mfn="",dob="1977-10-12")
        ])
    rows.append(row("testa","testidentity","G1","TEST"))
    rows.append(row("testb","testidentity","G2","TEST"))
    report=base.assess(rows,cap=1000,max_passes=3)
    return rows,report

class OriginalABCDCompositionTests(unittest.TestCase):
    def test_physical_sql_comparison_on_same_global_M_and_16_arms(self):
        rows,report=evidence()
        r=main.compare(rows,report,abc,base,planner,cap=1000)
        self.assertTrue(r["D0_D1_are_not_adaptive"])
        self.assertTrue(r["adaptive_strategy_executed_using_indexed_sql"])
        self.assertFalse(r["TEST_evaluated"])
        self.assertFalse(r["may_promote_policy"])
        self.assertFalse(r["fdr_measured"])
        for part in ("TRAIN","VALIDATION"):
            result=r["results"][part]
            self.assertEqual(result["all_no_cpf_truth_M"],2)
            self.assertEqual(result["name_eligible_truth_M"],2)
            a=result["arms"]
            self.assertEqual(sum(z.split("+")[0] in abc.ARMS for z in a),16)
            self.assertEqual(a["A0"]["M"],1) # only PD matching with no maternal field
            self.assertEqual(a["A1"]["M"],2) # phonetic repairs MARIA/MARYA
            self.assertGreaterEqual(a["D1+E0"]["M"],a["D1"]["M"])
            self.assertGreaterEqual(a["ADAPTIVE_FREQ_TOP_3"]["M"],
                                    a["ADAPTIVE_FREQ_TOP_2"]["M"])
            self.assertEqual(a["COMPOSE_19"]["M"],report["results"][part]["metrics"]["UNION_EXPANDED"]["M"])
            self.assertEqual(a["COMPOSE_19"]["U"],report["results"][part]["metrics"]["UNION_EXPANDED"]["U"])
            self.assertAlmostEqual(
                a["COMPOSE_19"]["u_degree"]["mean"],
                2*a["COMPOSE_19"]["U"]/result["name_eligible_observations"])
            self.assertTrue(a["ADAPTIVE_FREQ_TOP_3"]["strategy_semantics"].startswith("TRAIN_"))
        self.assertNotIn("testidentity",json.dumps(r))
        self.assertNotIn("MARYA SILVA",json.dumps(r))

    def test_fail_closed_when_composition_or_provenance_mismatches(self):
        rows,report=evidence()
        report["results"]["TRAIN"]["cells"]["P_PHON_M_LAST"]["M"]+=1
        with self.assertRaisesRegex(ValueError,"physical composition M/U"):
            main.compare(rows,report,abc,base,planner,cap=1000)
        rows,report=evidence()
        report["TEST_evaluated"]=True
        with self.assertRaisesRegex(ValueError,"TEST-blind"):
            main.compare(rows,report,abc,base,planner,cap=1000)
        rows,report=evidence()
        rows[-1]["base_person_id"]="TRAINp"
        with self.assertRaisesRegex(ValueError,"leakage"):
            main.compare(rows,report,abc,base,planner,cap=1000)

    def test_cap_never_truncates_and_no_CPF_motherless_PD_works(self):
        rows,report=evidence()
        with self.assertRaisesRegex(ValueError,"cap"):
            main.compare(rows,report,abc,base,planner,cap=1)
        result=main.compare(rows,report,abc,base,planner,cap=1000)
        self.assertEqual(result["results"]["TRAIN"]["arms"]["A0"]["M"],1)
        self.assertGreaterEqual(result["results"]["TRAIN"]["arms"]["A0+E0"]["M"],1)

if __name__=="__main__":
    unittest.main()

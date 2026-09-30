import csv,tempfile,unittest
from pathlib import Path
import importlib.util
P=Path(__file__).with_name("generate_birth_reference.py")
spec=importlib.util.spec_from_file_location("g",P); g=importlib.util.module_from_spec(spec); spec.loader.exec_module(g)

def ref(path,missing=None,delta=0):
    vals={i:100000-i*500 for i in range(90)}; vals[90]=200000
    # tests that exercise read_ref temporarily bind the expected total to fixture total.
    with path.open("w",encoding="utf-8",newline="") as f:
        w=csv.writer(f,lineterminator="\n"); w.writerow(["idade","populacao"])
        for i,v in vals.items():
            if i!=missing: w.writerow([i,v+(delta if i==0 else 0)])
    return vals

class T(unittest.TestCase):
 def test_real_ref_contract(self):
  p=P.parents[2]/"data/reference/synthetic-birth-sp/ibge_projection_2024_sp_ambos_2026.csv"
  ages,total=g.read_ref(p)
  self.assertEqual(len(ages),91); self.assertEqual(total,46179008)
  self.assertEqual(ages[0],470019); self.assertEqual(ages[89],52654); self.assertEqual(ages[90],171261)
 def test_fail_closed_missing_age(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/"x.csv"; vals=ref(p,57); old=g.EXPECTED_TOTAL; g.EXPECTED_TOTAL=sum(vals.values())
   try:
    with self.assertRaisesRegex(ValueError,"idades ausentes"): g.read_ref(p)
   finally: g.EXPECTED_TOTAL=old
 def test_fail_closed_total(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/"x.csv"; vals=ref(p,delta=1); old=g.EXPECTED_TOTAL; g.EXPECTED_TOTAL=sum(vals.values())
   try:
    with self.assertRaisesRegex(ValueError,"difere do Total"): g.read_ref(p)
   finally: g.EXPECTED_TOTAL=old
 def test_90_plus_decay_and_conservation(self):
  p=P.parents[2]/"data/reference/synthetic-birth-sp/ibge_projection_2024_sp_ambos_2026.csv"
  ages,total=g.read_ref(p); tail,r=g.expand_90_plus(ages)
  self.assertAlmostEqual(r,52654/59998); self.assertEqual(sum(v for _,v in tail),171261)
  self.assertEqual(tail[0][0],90); self.assertEqual(tail[-1][0],g.MAX_SYNTHETIC_AGE)
  self.assertTrue(all(90<=age<=g.MAX_SYNTHETIC_AGE for age,_ in tail))
  self.assertTrue(all(tail[i][1]>=tail[i+1][1] for i in range(len(tail)-1)))
  daily,_=g.daily_distribution(ages); self.assertEqual(sum(daily.values()),total)
 def test_age_zero_window(self):
  p=P.parents[2]/"data/reference/synthetic-birth-sp/ibge_projection_2024_sp_ambos_2026.csv"
  ages,_=g.read_ref(p); daily,_=g.daily_distribution(ages)
  self.assertIn(g.date(2025,7,2),daily); self.assertIn(g.date(2026,7,1),daily)
 def test_schema_and_source_hash_are_frozen(self):
  self.assertEqual(g.SCHEMA,"JORNADA_SYNTH_BIRTH_DAILY_V1")
  self.assertEqual(g.MAX_SYNTHETIC_AGE,115)
  self.assertEqual(g.SOURCE_XLSX_SHA256,"6E5C3D21A2E8FF50BADD7BE2785E1664B41A43277543BE541641B0CD802C3205")
if __name__=="__main__": unittest.main()

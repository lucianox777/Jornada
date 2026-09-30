import tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import importlib.util
P=Path(__file__).with_name("generate_birth_reference.py")
spec=importlib.util.spec_from_file_location("g",P); g=importlib.util.module_from_spec(spec); spec.loader.exec_module(g)

def table(missing=None,total_delta=0,tail=100):
    h=["UF","Sexo","Idade","2026"]; rows=[h]
    vals={i:1000-i for i in range(90)}; vals[90]=tail
    total=sum(vals.values())+total_delta
    rows.append(["SP","Total","Total",str(total)])
    for i in range(90):
        if i!=missing: rows.append(["SP","Total",str(i),str(vals[i])])
    rows.append(["SP","Total","90 anos ou mais",str(tail)])
    return rows

class T(unittest.TestCase):
 def read(self,rows):
  with patch.object(g,"_xlsx_rows",return_value=iter(rows)):
   return g.read_projection(Path(g.SOURCE_FILE))
 def test_reads_sp_total_2026_and_reconciles(self):
  ages,total=self.read(table())
  self.assertEqual(ages[0],1000); self.assertEqual(ages[89],911); self.assertEqual(ages[90],100)
  self.assertEqual(sum(ages.values()),total)
 def test_fail_closed_missing_age(self):
  with self.assertRaisesRegex(ValueError,"idades ausentes"): self.read(table(missing=57))
 def test_fail_closed_total(self):
  with self.assertRaisesRegex(ValueError,"difere do Total"): self.read(table(total_delta=1))
 def test_requires_official_filename(self):
  with self.assertRaisesRegex(ValueError,"arquivo esperado"): list(g._xlsx_rows(Path("wrong.xlsx")))
 def test_90_plus_decay_comes_from_file(self):
  ages,_=self.read(table(tail=250)); tail,r=g.expand_90_plus(ages)
  self.assertAlmostEqual(r,ages[89]/ages[88]); self.assertEqual(sum(v for _,v in tail),250)
  self.assertTrue(all(a>=90 for a,_ in tail))
 def test_age_window_and_total_are_conserved(self):
  ages,total=self.read(table(tail=250)); daily,_=g.daily_distribution(ages)
  self.assertEqual(sum(daily.values()),total)
  self.assertIn(g.date(2025,7,2),daily); self.assertIn(g.date(2026,7,1),daily)
 def test_schema_unchanged(self):
  self.assertEqual(g.SCHEMA,"JORNADA_SYNTH_BIRTH_DAILY_V1")
if __name__=="__main__": unittest.main()

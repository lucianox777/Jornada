import csv,json,tempfile,unittest
from pathlib import Path
import importlib.util
P=Path(__file__).with_name('generate_birth_reference.py')
spec=importlib.util.spec_from_file_location('g',P); g=importlib.util.module_from_spec(spec); spec.loader.exec_module(g)

def sidra(path, missing=None, total_delta=0, tail=6):
 with path.open('w',encoding='utf-8',newline='') as f:
  w=csv.writer(f); total=365*100+tail+total_delta
  w.writerow(['Idade','Valor']); w.writerow(['Total',str(total)])
  w.writerow(['Menos de 1 ano','365'])
  for i in range(1,100):
   if i!=missing: w.writerow([f'{i} ano' if i==1 else f'{i} anos','365'])
  w.writerow(['100 anos ou mais',str(tail)])

class T(unittest.TestCase):
 def test_schema_hash_extrapolation_and_100_105(self):
  with tempfile.TemporaryDirectory() as td:
   d=Path(td); s=d/'sidra.csv'; sidra(s)
   out=d/'births.json'; man=d/'manifest.json'
   self.assertEqual(g.main(['--sidra-9514',str(s),'--post-census-cutoff','2022-08-10','--out',str(out),'--manifest',str(man)]),0)
   o=json.loads(out.read_text()); m=json.loads(man.read_text())
   self.assertEqual(o['schema_version'],g.SCHEMA)
   self.assertEqual(m['sources'][0]['populationWeight'],365*100+6)
   self.assertEqual(m['model']['centenarianTail'].split(';')[0],'100+ represented uniformly over ages 100..105')
   self.assertEqual(m['model']['postCensus']['cutoff'],'2022-08-10')
   self.assertEqual(len(m['output']['sha256']),64)
   rows={x['date']:x['births'] for x in o['rows']}
   self.assertTrue(all(rows[g.CENSUS_DATE.isoformat()+'' ]>=0 for _ in [0]))
 def test_sidra_rejects_missing_age(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'sidra.csv'; sidra(p,missing=57)
   with self.assertRaisesRegex(ValueError,'idades ausentes'): g.read_sidra(p)
 def test_sidra_reconciles_total(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'sidra.csv'; sidra(p,total_delta=1)
   with self.assertRaisesRegex(ValueError,'difere do Total'): g.read_sidra(p)
 def test_tail_is_100_105_and_conserved(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'sidra.csv'; sidra(p,tail=7)
   groups=g.read_sidra(p); tail=[x for x in groups if x[0]==100][0]
   self.assertEqual((tail[0],tail[1],tail[2]),(100,105,7))
   daily,total,zero=g.census_daily(groups)
   self.assertEqual(total,365*100+7); self.assertEqual(zero,365)
 def test_post_census_cutoff_and_determinism(self):
  d={}; from collections import defaultdict
  a=defaultdict(int); b=defaultdict(int)
  g.extend_post_census(a,365,g.date(2022,8,3)); g.extend_post_census(b,365,g.date(2022,8,3))
  self.assertEqual(a,b); self.assertEqual(sum(a.values()),3)
  with self.assertRaisesRegex(ValueError,'cutoff'): g.extend_post_census(defaultdict(int),365,g.date(2022,7,31))
  with self.assertRaisesRegex(ValueError,'futuro'): g.extend_post_census(defaultdict(int),365,g.date(2026,9,30),today=g.date(2026,9,29))
 def test_apportion_conserves(self):
  self.assertEqual(g.apportion(3,2),[2,1]); self.assertEqual(sum(g.apportion(7,6)),7)
if __name__=='__main__': unittest.main()

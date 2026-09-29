import csv,json,tempfile,unittest
from pathlib import Path
import importlib.util
P=Path(__file__).with_name('generate_birth_reference.py')
spec=importlib.util.spec_from_file_location('g',P); g=importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
class T(unittest.TestCase):
 def test_weights_windows_schema_and_hash(self):
  with tempfile.TemporaryDirectory() as td:
   d=Path(td); sid=d/'sidra.csv'
   with sid.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['Idade','Valor']); w.writerow(['Menos de 1 ano','365']); [w.writerow([f'{i} ano' if i==1 else f'{i} anos','365']) for i in range(1,100)]; w.writerow(['100 anos ou mais','23'])
   sin=d/'sin.csv'
   with sin.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['DTNASC','CODMUNRES']); w.writerow(['01082022','3550308']); w.writerow(['01082022','3550308']); w.writerow(['02082022','3550308'])
   out=d/'births.json'; man=d/'manifest.json'; self.assertEqual(g.main(['--sidra-9514',str(sid),'--sinasc',str(sin),'--out',str(out),'--manifest',str(man)]),0)
   o=json.loads(out.read_text()); m=json.loads(man.read_text()); rows={x['date']:x['births'] for x in o['rows']}
   self.assertEqual(o['schema_version'],g.SCHEMA); self.assertEqual(m['sources'][0]['populationWeight'],365*100+23); self.assertEqual(rows['2022-08-01'],2); self.assertEqual(rows['2022-08-02'],1); self.assertEqual(len(m['output']['sha256']),64)
 def test_sinasc_requires_residence_and_rejects_non_msp(self):
  with tempfile.TemporaryDirectory() as td:
   d=Path(td)
   no_geo=d/'no_geo.csv'
   with no_geo.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['DTNASC']); w.writerow(['01082022'])
   with self.assertRaisesRegex(ValueError,'geografia de residência ausente'): g.read_sinasc([no_geo])
   other=d/'other.csv'
   with other.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['DTNASC','CODMUNRES']); w.writerow(['01082022','3550308']); w.writerow(['02082022','3509502'])
   with self.assertRaisesRegex(ValueError,'fora da residência 3550308'): g.read_sinasc([other])
 def test_sidra_rejects_missing_interior_age(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'sidra.csv'
   with p.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['Idade','Valor']); w.writerow(['Menos de 1 ano','10']); w.writerow(['2 anos','10']); w.writerow(['100 anos ou mais','2'])
   with self.assertRaisesRegex(ValueError,'idades ausentes'): g.read_sidra(p)
 def test_duplicate_dates_impossible_and_positive(self):
  self.assertEqual(g.apportion(3,2),[2,1]); self.assertEqual(sum(g.apportion(1761,23)),1761)
if __name__=='__main__': unittest.main()

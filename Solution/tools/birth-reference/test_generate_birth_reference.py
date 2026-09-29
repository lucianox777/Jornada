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
    w=csv.writer(f); w.writerow(['Idade','Valor']); w.writerow(['Menos de 1 ano','365']); w.writerow(['1 ano','365']); w.writerow(['100 anos ou mais','23'])
   sin=d/'sin.csv'
   with sin.open('w',encoding='utf-8',newline='') as f:
    w=csv.writer(f); w.writerow(['DTNASC']); w.writerow(['01082022']); w.writerow(['01082022']); w.writerow(['02082022'])
   out=d/'births.json'; man=d/'manifest.json'; self.assertEqual(g.main(['--sidra-9514',str(sid),'--sinasc',str(sin),'--out',str(out),'--manifest',str(man)]),0)
   o=json.loads(out.read_text()); m=json.loads(man.read_text()); rows={x['date']:x['births'] for x in o['rows']}
   self.assertEqual(o['schema_version'],g.SCHEMA); self.assertEqual(m['sources'][0]['populationWeight'],753); self.assertEqual(rows['2022-08-01'],2); self.assertEqual(rows['2022-08-02'],1); self.assertEqual(len(m['output']['sha256']),64)
 def test_duplicate_dates_impossible_and_positive(self):
  self.assertEqual(g.apportion(3,2),[2,1]); self.assertEqual(sum(g.apportion(1761,23)),1761)
if __name__=='__main__': unittest.main()

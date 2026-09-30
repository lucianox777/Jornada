import importlib.util
from pathlib import Path
import unittest
P=Path(__file__).resolve().parents[3]/"scripts"/"ci-impact-classifier.py"
spec=importlib.util.spec_from_file_location("ci_impact",P); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
class CiImpactClassifierTests(unittest.TestCase):
 def test_docs_only_does_not_request_runtime_gates(self):
  f=m.classify(["Solution/docs/Dividas_Tecnicas.md"])["flags"]
  self.assertTrue(f["docs"]); self.assertFalse(f["full"]); self.assertFalse(f["run_unit"]); self.assertFalse(f["run_e2e"])
 def test_sql_requests_ddl_and_runtime_sql_gates(self):
  f=m.classify(["Solution/database/migrations/x.sql"])["flags"]
  self.assertTrue(f["sql"]); self.assertTrue(f["run_ddl"]); self.assertTrue(f["run_integration"]); self.assertTrue(f["run_e2e"])
 def test_unknown_is_full(self):
  f=m.classify(["unexpected/new.surface"])["flags"]
  self.assertTrue(f["full"]); self.assertTrue(f["run_unit"]); self.assertTrue(f["run_e2e"])
 def test_workflow_is_full(self):
  f=m.classify([".github/workflows/ci.yml"])["flags"]
  self.assertTrue(f["workflow"]); self.assertTrue(f["full"])
if __name__=="__main__": unittest.main()

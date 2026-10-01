import importlib.util
from pathlib import Path
import unittest
P=Path(__file__).resolve().parents[2]/"scripts"/"ci-impact-classifier.py"
spec=importlib.util.spec_from_file_location("ci_impact",P); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)

class CiImpactClassifierTests(unittest.TestCase):
    def test_docs_only_may_skip_full_ci(self):
        r=m.classify(["Solution/docs/Dividas_Tecnicas.md"])
        self.assertTrue(r["docs_only"]); self.assertFalse(r["run_full"])

    def test_any_code_requires_full_ci(self):
        r=m.classify(["Solution/src/Jornada.Api/Program.cs"])
        self.assertFalse(r["docs_only"]); self.assertTrue(r["run_full"])

    def test_sql_requires_full_ci(self):
        self.assertTrue(m.classify(["Solution/database/migrations/x.sql"])["run_full"])

    def test_dt10_publication_surface_requests_dt10_evidence(self):
        r=m.classify(["Solution/database/migrations/20260926_Linkage_Publicacao_Progressiva_Lote.sql"])
        self.assertTrue(r["run_full"]); self.assertTrue(r["run_dt10"])

    def test_unrelated_code_does_not_request_dt10_evidence(self):
        r=m.classify(["Solution/src/Jornada.Api/Program.cs"])
        self.assertTrue(r["run_full"]); self.assertFalse(r["run_dt10"])

    def test_config_requires_full_ci(self):
        self.assertTrue(m.classify(["Solution/config/linkage/model-fs-catalog.json"])["run_full"])

    def test_workflow_requires_full_ci(self):
        r=m.classify([".github/workflows/ci.yml"])
        self.assertTrue(r["run_full"]); self.assertTrue(r["run_dt10"])

    def test_mixed_docs_and_code_requires_full_ci(self):
        r=m.classify(["Solution/docs/x.md","Solution/src/Jornada.Api/Program.cs"])
        self.assertTrue(r["run_full"])

    def test_unknown_requires_full_ci(self):
        self.assertTrue(m.classify(["unexpected/new.surface"])["run_full"])

if __name__=="__main__": unittest.main()

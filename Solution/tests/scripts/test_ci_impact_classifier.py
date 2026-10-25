import importlib.util
from pathlib import Path
import unittest
P=Path(__file__).resolve().parents[2]/"scripts"/"ci-impact-classifier.py"
spec=importlib.util.spec_from_file_location("ci_impact",P); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)

class CiImpactClassifierTests(unittest.TestCase):
    def test_docs_only_may_skip_full_ci(self):
        r=m.classify(["Solution/docs/Dividas_Tecnicas.md"])
        self.assertTrue(r["docs_only"]); self.assertFalse(r["run_full"])

    def test_e2e_baseline_manifest_requires_full_ci(self):
        r=m.classify(["Solution/docs/evidence/E2E_B_Baseline_20261001.yml"])
        self.assertFalse(r["docs_only"]); self.assertTrue(r["run_full"]); self.assertFalse(r["run_dt10"])

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

    def test_reusable_dt10_workflow_changes_require_sql_evidence(self):
        r=m.classify([".github/workflows/dt10-evidence.yml"])
        self.assertTrue(r["run_full"])
        self.assertTrue(r["run_dt10"])

    def test_mixed_docs_and_code_requires_full_ci(self):
        r=m.classify(["Solution/docs/x.md","Solution/src/Jornada.Api/Program.cs"])
        self.assertTrue(r["run_full"])

    def test_unknown_requires_full_ci(self):
        self.assertTrue(m.classify(["unexpected/new.surface"])["run_full"])

    def test_v9_30k_probe_gold_only_vs_calibrator_impact(self):
        """Run the new probe classifier assertions in the existing mandatory CI impact job."""
        p = Path(__file__).resolve().parents[2] / "scripts" / "v9-physical-impact-classifier.py"
        probe_spec = importlib.util.spec_from_file_location("v9_physical_impact_ci", p)
        probe = importlib.util.module_from_spec(probe_spec)
        probe_spec.loader.exec_module(probe)
        gold_only = [("M", "Solution/src/Jornada.Linkage.Runner/V9GoldDisposableLowerProcedurePatch.cs")]
        self.assertFalse(probe.classify(gold_only)["run_physical_30k"])
        self.assertFalse(probe.classify([("M", "Solution/docs/Indice_Decisoes_Vigentes.md")])["run_physical_30k"])
        mixed = gold_only + [("M", "Solution/src/Jornada.Linkage.Core/FellegiSunterScoring.cs")]
        self.assertTrue(probe.classify(mixed)["run_physical_30k"])
        self.assertTrue(probe.classify([("M", "Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs")])["run_physical_30k"])
        self.assertTrue(probe.classify([("M", ".github/workflows/v9-physical-seed42-probe.yml")])["run_physical_30k"])
        self.assertTrue(probe.classify([])["run_physical_30k"])
        self.assertTrue(probe.classify([], manual=True)["run_physical_30k"])


if __name__=="__main__": unittest.main()

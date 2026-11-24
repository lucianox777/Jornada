import importlib.util
from pathlib import Path
import unittest
P=Path(__file__).resolve().parents[2]/"scripts"/"ci-impact-classifier.py"
spec=importlib.util.spec_from_file_location("ci_impact",P); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)

class CiImpactClassifierTests(unittest.TestCase):
    def test_docs_only_may_skip_full_ci(self):
        r=m.classify(["Solution/docs/Dividas_Tecnicas.md"])
        self.assertTrue(r["docs_only"]); self.assertFalse(r["run_full"])

    def test_executable_under_docs_must_not_bypass_full_ci(self):
        # A directory name alone must never exempt a script/configuration.
        for path in (
            "Solution/docs/evidence/replay.py",
            "Solution/docs/scripts/verify.sh",
            "Solution/docs/evidence/policy.json",
            "Solution/docs/evidence/pipeline.yml",
            "Solution/docs/evidence/migration.sql",
            "solution/docs/evidence/runner.PY",
        ):
            with self.subTest(path=path):
                r = m.classify([path])
                self.assertFalse(r["docs_only"])
                self.assertTrue(r["run_full"])

    def test_plain_text_docs_remain_fast_path(self):
        for path in ("Solution/docs/decision.md", "Solution/docs/readme.txt", "README.md"):
            with self.subTest(path=path):
                self.assertTrue(m.classify([path])["docs_only"])

    def test_e2e_baseline_manifest_requires_full_ci(self):
        r=m.classify(["Solution/docs/evidence/E2E_B_Baseline_20261001.yml"])
        self.assertFalse(r["docs_only"]); self.assertTrue(r["run_full"]); self.assertFalse(r["run_dt10"])

    def test_full_unit_scope_includes_all_source_and_test_paths_and_gate_changes(self):
        for path in ("Solution/src/Jornada.Api/Program.cs",
                     "Solution/tests/Jornada.Tests/Test.cs",
                     "Solution/tests/scripts/check.py",
                     "Solution/tests/readme.txt",
                     ".github/workflows/jornada-pr-required-fast.yml",
                     ".github/workflows/ci.yml",
                     "Solution/scripts/ci-impact-classifier.py",
                     "Solution/scripts/test-evidence-gate.py"):
            with self.subTest(path=path):
                self.assertTrue(m.classify(["README.md", path])["run_unit_full"])
        for path in ("README.md", "Solution/docs/decision.md",
                     "Solution/database/migrations/x.sql",
                     "Solution/src-other/file.cs", "Solution/tests-other/file.cs"):
            with self.subTest(path=path):
                self.assertFalse(m.classify([path])["run_unit_full"])
        self.assertTrue(m.classify([])["run_unit_full"])

    def test_any_code_requires_full_ci(self):
        r=m.classify(["Solution/src/Jornada.Api/Program.cs"])
        self.assertFalse(r["docs_only"]); self.assertTrue(r["run_full"])

    def test_nuget_lockfiles_always_trigger_full_ci_without_dt10(self):
        # Regression: lockfile additions/repairs must exercise dependency-lock on master.
        for path in (
            "Solution/src/Jornada.Linkage.SyntheticCorpus.Parametrized/packages.lock.json",
            "Solution/tests/Jornada.Tests/packages.lock.json",
        ):
            with self.subTest(path=path):
                result = m.classify([path])
                self.assertEqual(result["classification"], "substantive")
                self.assertFalse(result["docs_only"])
                self.assertTrue(result["run_full"])
                self.assertFalse(result["run_dt10"])

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
        self.assertFalse(probe.classify([("M", ".github/workflows/v9-physical-seed42-probe.yml")])["run_physical_30k"])
        self.assertTrue(probe.classify([
            ("M", ".github/workflows/v9-physical-seed42-probe.yml"),
            ("M", "Solution/src/Jornada.Linkage.Core/FellegiSunterScoring.cs")
        ])["run_physical_30k"])
        self.assertTrue(probe.classify([])["run_physical_30k"])
        self.assertTrue(probe.classify([], manual=True)["run_physical_30k"])


class EventDiffTests(unittest.TestCase):
    def setUp(self):
        import contextlib
        import tempfile
        import subprocess
        import os
        self.repo = Path(self.enterContext(tempfile.TemporaryDirectory()))
        self.enterContext(contextlib.chdir(self.repo))
        def git(*args):
            return subprocess.check_output(["git", *args], text=True).strip()
        self.git = git
        git("init", "-q", "-b", "master")
        git("config", "user.name", "fixture")
        git("config", "user.email", "fixture@example.test")
        self.base = self.commit("README.md")

    def commit(self, path):
        p = Path(path)
        p.parent.mkdir(exist_ok=True, parents=True)
        p.write_text("change\n")
        self.git("add", "-A")
        self.git("commit", "-qm", "fixture")
        return self.git("rev-parse", "HEAD")

    def push(self, base, head, **fields):
        event = {"ref":"refs/heads/master", "forced":False, "before":base, "after":head}
        return m.event_impact(dict(event, **fields), "push", head)

    def test_push_exact_diff_new_and_missing_base(self):
        head = self.commit("Solution/docs/decision.md")
        result = self.push(self.base, head)
        self.assertTrue(result["docs_only"])
        self.assertEqual(result["paths"], ["Solution/docs/decision.md"])
        self.assertEqual((result["base"],result["head"]), (self.base,head))
        for base in ("0"*40,"1"*40,"bad",head):
            self.assertTrue(self.push(base,head)["run_full"])
        self.assertTrue(self.push(self.base,head,forced=True)["run_full"])
        self.assertTrue(self.push(self.base,head,ref="refs/tags/v1")["run_full"])

    def test_pr_merge_base_excludes_master_only_code(self):
        self.git("checkout","-qb","topic")
        head = self.commit("Solution/docs/readme.txt")
        self.git("checkout","-q","master")
        other = self.commit("Solution/src/code.cs")
        pr = {"pull_request":{"base":{"sha":other,"ref":"master"},"head":{"sha":head}}}
        result = m.event_impact(pr,"pull_request",other)
        self.assertTrue(result["docs_only"])
        self.assertEqual(result["base"],self.base)

    def test_rename_requires_both_paths_and_handles_literal_newlines(self):
        old = self.commit("Solution/docs/readme.md\nsource.cs")
        Path("Solution/docs/readme.md\nsource.cs").rename("Solution/docs/moved.md")
        head = self.commit("Solution/docs/moved.md")
        self.assertTrue(self.push(old,head)["run_full"])
        self.assertIn("Solution/docs/readme.md\nsource.cs",self.push(old,head)["paths"])

    def test_full_unit_diff_deletion_rename_and_unverified_event(self):
        old = self.commit("Solution/src/file.cs")
        Path("Solution/docs").mkdir(parents=True, exist_ok=True)
        Path("Solution/src/file.cs").rename("Solution/docs/moved.md")
        renamed = self.commit("Solution/docs/moved.md")
        self.assertTrue(self.push(old, renamed)["run_unit_full"])
        self.assertIn("Solution/src/file.cs", self.push(old, renamed)["paths"])
        test_head = self.commit("Solution/tests/test.cs")
        Path("Solution/tests/test.cs").unlink()
        self.git("add", "-A")
        self.git("commit", "-qm", "delete test")
        self.assertTrue(self.push(test_head, self.git("rev-parse", "HEAD"))["run_unit_full"])
        for base in ("0"*40, "1"*40, "bad"):
            self.assertTrue(self.push(base, renamed)["run_unit_full"])
        self.assertTrue(m.event_impact({}, "workflow_dispatch", renamed)["run_unit_full"])

    def test_executable_or_symlink_requires_full(self):
        import os
        p = Path("README.md")
        p.chmod(0o755)
        self.git("add","README.md")
        self.git("commit","-qm","chmod")
        self.assertTrue(self.push(self.base,self.git("rev-parse","HEAD"))["run_full"])
        p.unlink()
        os.symlink("source", p)
        self.git("add","README.md")
        self.git("commit","-qm","symlink")
        self.assertTrue(self.push(self.base,self.git("rev-parse","HEAD"))["run_full"])

    def test_binary_document_requires_full_ci(self):
        Path("README.md").write_bytes(b"text\0binary")
        self.git("add", "README.md")
        self.git("commit", "-qm", "binary document")
        result = self.push(self.base, self.git("rev-parse", "HEAD"))
        self.assertTrue(result["run_full"])
        self.assertTrue(result["run_dt10"])
        self.assertEqual(result["paths"], ["README.md"])
        self.assertEqual(result["reason"], "binary or unverified document diff")

    def test_closed_list_paths(self):
        for path in [".github/docs.md", "Solution/docs/LOCKED.md", "Solution/docs/manifest.txt",
                     "Solution/docs/openapi.md", "Solution/docs/fixtures/input.md", "Solution/docs/fixture.txt",
                     "Solution/tests/readme.txt", "Solution/docs/SolutionSchema.md", "Solution/docs/x.json"]:
            self.assertTrue(m.classify([path])["run_full"], path)


if __name__ == "__main__":
    unittest.main()

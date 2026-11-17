"""Regression checks for independent synthetic cohort artifacts (no scientific execution)."""
import importlib.util
from pathlib import Path
import tempfile
import tarfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/v9-standard-prepare-handoff.py"
spec = importlib.util.spec_from_file_location("v9_standard_prepare_handoff", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CohortArtifacts(unittest.TestCase):
    def test_pack_and_restore_three_separate_cohorts(self):
        for cohort in module.COHORT_DIRS:
            with self.subTest(cohort=cohort), tempfile.TemporaryDirectory() as temp:
                base = Path(temp)
                src = base / "source"
                dest = base / "restore"
                (src / cohort).mkdir(parents=True)
                dest.mkdir()
                (src / cohort / "observacoes.csv").write_text("id,nome\\n1,teste\\n")
                archive = base / "corpus.tar.gz"
                module.pack(src, archive, cohort)
                module.unpack(dest, archive, cohort)
                self.assertEqual((dest / cohort / "observacoes.csv").read_bytes(),
                                 (src / cohort / "observacoes.csv").read_bytes())

    def test_cross_cohort_archive_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            (base / "people-100k").mkdir()
            (base / "people-100k/observacoes.csv").write_text("x")
            archive = base / "corpus.tar.gz"
            module.pack(base, archive)
            dest = base / "restore"
            dest.mkdir()
            with self.assertRaises(ValueError):
                module.unpack(dest, archive, "corpus")

    def test_reject_unknown_cohort(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                module.pack(Path(temp), Path(temp) / "bad.tar.gz", "../escape")

    def test_workflow_retains_isolation_and_one_use(self):
        workflow = (ROOT.parent / ".github/workflows/v9-standard-linear-100k.yml").read_text()
        self.assertIn("v9-standard-validation-corpus-", workflow)
        self.assertIn("v9-standard-test-corpus-", workflow)
        self.assertIn("--cohort-dir validation-people", workflow)
        self.assertIn("--cohort-dir corpus", workflow)
        self.assertIn("python3 scripts/v9-reserve-test-seed.py", workflow)
        self.assertIn("steps.test_seed.outputs.test_seed", workflow)
        self.assertIn("test-seed-reservation.json", workflow)
        self.assertIn("retention-days: 30", workflow)


if __name__ == "__main__":
    unittest.main()

"""Regression test for CI-V9-PROBE-01: exclusions narrowly audited, unknown is RUN."""
import importlib.util
import unittest
from pathlib import Path

P = Path(__file__).resolve().parents[2] / "scripts" / "v9-physical-impact-classifier.py"
spec = importlib.util.spec_from_file_location("v9_physical_impact", P)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class V9PhysicalImpactTests(unittest.TestCase):
    def changes(self, *paths):
        return m.classify([("M", p) for p in paths])["run_physical_30k"]

    def test_gold_patcher_preflight_and_orchestrator_do_not_recalibrate(self):
        for path in (
            "Solution/src/Jornada.Linkage.Runner/V9GoldDisposableLowerProcedurePatch.cs",
            "Solution/src/Jornada.Linkage.Runner/V9GoldHarnessDatabasePreparation.cs",
            "Solution/src/Jornada.Linkage.Runner/V9GoldHarnessCommand.cs",
            "Solution/tests/Jornada.Tests/Unit/V9GoldLowerNonMatchExperimentTests.cs",
            "Solution/scripts/v9-gold-12-trial-localdb.ps1",
        ):
            with self.subTest(path=path):
                self.assertFalse(self.changes(path))

    def test_gold_holdout_preflight_does_not_recalibrate_train(self):
        # Audited isolated contract and unit tests cannot alter the 30k
        # calibrator. Adding shared files or modifying the runner entrypoint
        # still requires the full physical probe.
        for path in (
            "Solution/src/Jornada.Linkage.Runner/V9GoldIndependentHoldoutContract.cs",
            "Solution/tests/Jornada.Tests/Unit/V9GoldIndependentHoldoutContractTests.cs",
        ):
            with self.subTest(path=path):
                self.assertFalse(self.changes(path))
                self.assertFalse(m.classify([("A", path)])["run_physical_30k"])
                self.assertTrue(m.classify([("D", path)])["run_physical_30k"])
        self.assertTrue(self.changes(
            "Solution/src/Jornada.Linkage.Runner/V9GoldIndependentHoldoutContract.cs",
            "Solution/src/Jornada.Linkage.Runner/Program.cs"
        ))
        self.assertTrue(self.changes(
            "Solution/src/Jornada.Linkage.Runner/V9GoldIndependentHoldoutExecutor.cs"
        ))
        self.assertTrue(self.changes(
            "Solution/src/Jornada.Linkage.Core/V9LowerNonMatchTrainCalibrator.cs"
        ))

    def test_only_markdown_skips(self):
        self.assertFalse(self.changes("Solution/docs/X.md", "Documentos/README.md"))
        self.assertTrue(self.changes("Solution/docs/evidence/E2E_B_Baseline_20261001.yml"))

    def test_nested_worker_and_calibration_rerun(self):
        for path in (
            "Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs",
            "Solution/src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlAdaptiveCalibrationProbe.cs",
            "Solution/src/Jornada.Linkage.Parameters.Worker/V9FellegiSunterAdaptiveCalibrationAdapter.cs",
            "Solution/src/Jornada.Linkage.Core/FellegiSunterScoring.cs",
            "Solution/src/Jornada.Linkage.Core/V9LowerNonMatchTrainCalibrator.cs",
            "Solution/src/Jornada.Contracts/SyntheticObservationMultiplicityContract.cs",
            "Solution/src/Jornada.Linkage.SyntheticCorpus/Program.cs",
            "Solution/scripts/v9-nested-calibration-study.py",
            "Solution/data/reference/ibge-nomes-2022/nome.csv",
            "Solution/src/Jornada.Linkage.Core/Jornada.Linkage.Core.csproj",
            "Solution/src/Jornada.Linkage.Core/packages.lock.json",
            "Solution/global.json",
            ".github/workflows/v9-physical-seed42-probe.yml",
            "Solution/scripts/v9-physical-impact-classifier.py",
            "Solution/tests/scripts/test_v9_physical_impact_classifier.py",
            "unexpected/unclassified.surface",
        ):
            with self.subTest(path=path):
                self.assertTrue(self.changes(path))

    def test_mixed_gold_and_worker_requires(self):
        self.assertTrue(self.changes(
            "Solution/src/Jornada.Linkage.Runner/V9GoldDisposableLowerProcedurePatch.cs",
            "Solution/src/Jornada.Linkage.Core/FellegiSunterScoring.cs"
        ))

    def test_empty_rename_delete_copy_typechange_fail_closed(self):
        self.assertTrue(m.classify([])["run_physical_30k"])
        self.assertTrue(m.classify([("D", "Solution/docs/old.md")])["run_physical_30k"])
        self.assertTrue(m.classify([
            ("R100", "Solution/docs/old.md"), ("R100", "Solution/docs/new.md")
        ])["run_physical_30k"])
        self.assertTrue(m.classify([
            ("T", "Solution/src/Jornada.Linkage.Runner/V9GoldDisposableLowerProcedurePatch.cs")
        ])["run_physical_30k"])

    def test_name_status_z_supports_rename_and_unicode(self):
        raw = b"M\0Solution/docs/a.md\0R100\0Solution/docs/old.md\0Solution/docs/new.md\0"
        records = m.parse_name_status_z(raw)
        self.assertEqual(len(records), 3)
        self.assertTrue(m.classify(records)["run_physical_30k"])
        with self.assertRaises(ValueError):
            m.parse_name_status_z(b"M\0Solution/docs/a.md")
        with self.assertRaises(ValueError):
            m.parse_name_status_z(b"")

    def test_manual_always_requires_probe(self):
        self.assertTrue(m.classify([], manual=True)["run_physical_30k"])



if __name__ == "__main__":
    unittest.main()

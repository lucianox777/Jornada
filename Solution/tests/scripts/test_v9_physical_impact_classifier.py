"""Regression test for CI-V9-PROBE-01: exclusions narrowly audited, unknown is RUN."""
import importlib.util
import unittest
from pathlib import Path

P = Path(__file__).resolve().parents[2] / "scripts" / "v9-physical-impact-classifier.py"
spec = importlib.util.spec_from_file_location("v9_physical_impact", P)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class V9PhysicalImpactTests(unittest.TestCase):
    def test_policy_only_bootstrap(self):
        paths = sorted(m.CI_POLICY_ONLY)
        self.assertFalse(m.classify([("M", p) for p in paths])["run_physical_30k"])
        self.assertTrue(m.classify([("M", paths[0]), ("M", "Solution/database/schema.sql")])["run_physical_30k"])
        self.assertTrue(m.classify([("D", paths[0])])["run_physical_30k"])
        self.assertTrue(m.classify([("M", paths[0])], manual=True)["run_physical_30k"])

    def test_dependabot_metadata_only_sha_97195227(self):
        # Regression for #1203 (97195227): Dependabot groups and schedules
        # cannot change the physical 30k model or its SQL inputs.
        for path in (".github/dependabot.yml", ".github/dependabot.yaml"):
            with self.subTest(path=path):
                self.assertFalse(self.changes(path))
                self.assertFalse(m.classify([("A", path)])["run_physical_30k"])
                self.assertTrue(m.classify([("D", path)])["run_physical_30k"])
        self.assertFalse(self.changes(".github/workflows/v9-physical-seed42-probe.yml"))
        self.assertTrue(self.changes(".github/dependabot.yml", "Solution/database/Jornada_Fase1_v3.70.sql"))

    def test_dependabot_metadata_mixed_with_safe_docs_skips_probe(self):
        # A mixed metadata + Markdown-only change remains non-physical.
        for metadata in (".github/dependabot.yml", ".github/dependabot.yaml"):
            for status in ("A", "M"):
                with self.subTest(metadata=metadata, status=status):
                    records = [(status, metadata),
                               ("M", "Solution/docs/decision.md"),
                               ("A", "Documentos/README.md")]
                    result = m.classify(records)
                    self.assertFalse(result["run_physical_30k"])
                    self.assertEqual(result["decision"], "NOT_REQUIRED_FOR_THIS_DIFF")
                    # Adding a physical input to the same diff must fail closed.
                    self.assertTrue(m.classify(records + [
                        ("M", "Solution/database/Jornada_Fase1_v3.70.sql")
                    ])["run_physical_30k"])

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
            "unexpected/unclassified.surface",
        ):
            with self.subTest(path=path):
                self.assertTrue(self.changes(path))

    def test_only_test_changes_skip_expensive_30k(self):
        paths = (
            "Solution/tests/Jornada.Integration.Tests/Integration/Dt10V9LowerCanonicalLocalDbTests.cs",
            "Solution/tests/Jornada.Integration.Tests/Integration/NameFrequencyReferenceSqlServerTests.cs",
            "Solution/tests/Jornada.Tests/Unit/SomeNewRegressionTests.cs",
            "Solution/tests/scripts/test_new_regression.py",
        )
        for path in paths:
            with self.subTest(path=path):
                self.assertFalse(self.changes(path))
                self.assertFalse(m.classify([("A", path)])["run_physical_30k"])
                self.assertTrue(m.classify([("D", path)])["run_physical_30k"])
        self.assertTrue(self.changes(paths[0], "Solution/database/Jornada_Fase1_v3.70.sql"))
        self.assertTrue(self.changes("Solution/tests/Jornada.Integration.Tests/Integration/TestAsserts.cs"))
        self.assertTrue(self.changes("Solution/tests/Jornada.Integration.Tests/Fixtures/PhysicalProbeTests.cs"))
        self.assertTrue(self.changes("Solution/tests/Jornada.Tests/Shared/SharedProbeTests.cs"))
        self.assertFalse(self.changes("Solution/tests/scripts/test_v9_physical_impact_classifier.py"))

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

    def test_dt10_sql_and_governance_changes_require_probe(self):
        for path in (
            "Solution/database/migrations/20261026_DT10_V9_Lower_Governed_Off.sql",
            "Solution/database/Jornada_Fase1_v3.70.sql",
            "Solution/database/rollback/20261026_DT10_V9_Lower_Disable_Restore.sql",
            ".github/workflows/ci.yml",
        ):
            with self.subTest(path=path):
                self.assertTrue(self.changes(path))

    def test_malformed_diff_rejected_fail_closed(self):
        for raw in (
            b"",
            b"M\0Solution/docs/a.md",
            b"R100\0Solution/docs/old.md\0",
            b"Q\0Solution/docs/a.md\0",
            b"M\0../Solution/docs/a.md\0",
        ):
            with self.subTest(raw=raw):
                with self.assertRaises(ValueError):
                    m.parse_name_status_z(raw)

    def test_end_to_end_diff_parser_preserves_probe_decision(self):
        # Exercise the real byte parser before classification, not just the
        # classify() helper fed with synthetic tuples.
        cases = (
            (b"M\0Solution/docs/decision.md\0", False),
            (b"A\0Solution/docs/new.md\0", False),
            (b"M\0Solution/database/migrations/20261026_DT10_V9_Lower_Governed_Off.sql\0", True),
            (b"M\0Solution/docs/decision.md\0M\0Solution/database/Jornada_Fase1_v3.70.sql\0", True),
            (b"D\0Solution/docs/decision.md\0", True),
            (b"R100\0Solution/docs/old.md\0Solution/docs/new.md\0", True),
        )
        for raw, expected in cases:
            with self.subTest(raw=raw):
                parsed = m.parse_name_status_z(raw)
                result = m.classify(parsed)
                self.assertEqual(result["run_physical_30k"], expected)
                self.assertEqual(result["decision"], "RUN" if expected else "NOT_REQUIRED_FOR_THIS_DIFF")

    def test_manual_always_requires_probe(self):
        self.assertTrue(m.classify([], manual=True)["run_physical_30k"])



if __name__ == "__main__":
    unittest.main()

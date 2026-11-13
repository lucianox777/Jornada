"""Scientific separation: TEST accepts ONE winner, owns its cohort and never selects."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
CORPUS = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCorpusReader.cs"
PROBE = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs"
REPLAY = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9FrozenSourceResolveReplay.cs"
PROGRAM = ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"


class StandaloneTestContract(unittest.TestCase):
    def test_only_model_is_explicit_cli_parameter(self):
        source=CLI.read_text()
        self.assertIn('args.Length != 2 || args[0] != "--v9-independent-test"',source)
        self.assertIn("V9FittedSnapshotArtifact.ReadPinnedAsync(winnerFile, winnerSha)",source)
        self.assertIn("V9IndependentTestCorpusReader.ReadAsync(fullTestDir)",source)
        self.assertIn("V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync",source)
        self.assertIn("TotalComparedConfigurations: 1",source)
        self.assertIn("EndToEndRepetitions: 20",source)
        self.assertIn("JORNADA_V9_TEST_CORPUS_DIR",source)
        self.assertIn("JORNADA_V9_TEST_OUTPUT_DIR",source)
        for bad in ("--v9-calibrate", "--v9-validate-finalists", "finalist-validation.json",
                    "selection.json", "selectedValidationSnapshotSha256", "SelectScientificWinner",
                    "RefitAsync", "FitAsync"):
            self.assertNotIn(bad,source)
        self.assertIn("validationResultsRead = false",source)
        self.assertIn("trainPlanRead = false",source)
        self.assertIn("refitted = false",source)
        self.assertIn("configurationsEvaluated = 1",source)
        self.assertIn("certified = false",source)
        self.assertIn('guardedArgs[0] == "--v9-independent-test"',PROGRAM.read_text())

    def test_test_reader_owns_independent_cohort(self):
        source=CORPUS.read_text()
        self.assertIn("const ulong Seed = 20261113001UL",source)
        self.assertIn('ns = "EXTERNAL_TEST_" + manifestSha + ":"',source)
        self.assertIn('ns + id, "TEST"',source)
        self.assertIn("byIdentity.Count != 100000",source)
        self.assertIn('m.GetProperty("error_profile").GetString() != "clean"',source)
        self.assertIn('m.GetProperty("reference_code").GetString() != "CENSO2022_NOMES_BRASIL_V1"',source)
        self.assertIn("SHA256.HashDataAsync(stream, cancellationToken)",source)
        self.assertIn("EvaluateFrozenTestAsync",PROBE.read_text())
        self.assertIn("ReplayTest",REPLAY.read_text())
        self.assertNotIn("finalist-validation.json",source)

    def test_test_job_only_gets_winner_model_and_own_data(self):
        source=WF.read_text()
        validation,test=source.split("\n  validation-100k:\n",1)[1].split("\n  test-100k:\n",1)
        self.assertIn("selectedValidationSnapshotSha256",validation)
        self.assertIn("test-winner-only/*.fitted.json",validation)
        self.assertIn("v9-tf4-test-one-winner-",test)
        self.assertIn("--v9-independent-test",test)
        self.assertIn("JORNADA_V9_TEST_CORPUS_DIR:",test)
        self.assertIn("V9_TF4_TEST_PRIVATE_KEY_B64",test)
        self.assertIn("v9-tf4-test-sealed-",test)
        self.assertIn("timeout-minutes: 360",test)
        self.assertIn("timeout-minutes: 330",test)
        self.assertIn("inputs.test_required == true",test)
        self.assertNotIn("if: ${{ false }}",test)
        for bad in ("finalist-validation.json", "plan.json", "selection.json",
                    "validation-people", "people-100k", "--v9-calibrate",
                    "--v9-validate-finalists", "--people 100000",
                    "selectedValidationSnapshotSha256"):
            self.assertNotIn(bad,test)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"',test)

    def test_validation_handoff_carries_only_fitted_winner(self):
        validation=WF.read_text().split("\n  validation-100k:\n",1)[1].split("\n  test-100k:\n",1)[0]
        stage=validation.split("name: Upload ONLY the frozen winner model",1)[1].split(
            "- name: Upload independent VALIDATION aggregate result",1)[0]
        self.assertIn("path: Solution/.local/v9-100k-technical/test-winner-only/*.fitted.json",stage)
        self.assertNotIn("validation-results/*.json",stage)
        self.assertNotIn("selection.json",stage)
        self.assertNotIn("plan.json",stage)
        self.assertIn("if-no-files-found: error",stage)


if __name__ == "__main__":
    unittest.main()

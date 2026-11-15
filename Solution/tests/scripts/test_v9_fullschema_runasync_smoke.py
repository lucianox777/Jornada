"""Fail-closed contract for the opt-in 200-observation full-schema RunAsync smoke.

This is not scientific TEST, and it must not become a shortcut around the
production parity gate or the immutable one-use holdout.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent
WF = REPO / ".github/workflows/v9-test-fullschema-runasync-smoke.yml"
TF4 = REPO / ".github/workflows/v9-v3-four-name-100k.yml"
TEST = ROOT / "tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs"


class V9FullSchemaNonPublishingContract(unittest.TestCase):
    def test_physical_smoke_is_isolated_from_real_test(self):
        wf = WF.read_text(encoding="utf-8")
        self.assertIn("pull_request:", wf)
        self.assertIn("workflow_dispatch:", wf)
        self.assertIn("timeout-minutes: 45", wf)
        self.assertIn("v9-tf-start-disposable-sql.sh", wf)
        self.assertIn("database/Jornada_Fase1_v3.70.sql", wf)
        self.assertIn("20261114_V9_Operational_Schema_Readonly_Preflight.sql", wf)
        self.assertIn("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL", wf)
        self.assertIn("dotnet test tests/Jornada.Tests/Jornada.Tests.csproj", wf)
        self.assertIn("no_sealed", wf.replace("test_corpus_opened=false", "no_sealed"))
        self.assertIn("scientific_test_100k=BLOCKED", wf)
        self.assertNotIn("V9_TF4_TEST_PRIVATE_KEY_B64", wf)
        self.assertNotIn("TEST_PRIVATE_KEY", wf)
        self.assertNotIn("gh workflow run", wf)
        self.assertNotIn("Publish=true", wf)

    def test_real_runasync_is_called_but_never_publishes(self):
        src = TEST.read_text(encoding="utf-8")
        self.assertIn("ProbabilisticLinkageBatchRunner MakeRunner(", src)
        self.assertIn(".RunAsync(Request(), CancellationToken.None)", src)
        self.assertIn("LinkageRunType.MODEL_VALIDATION", src)
        self.assertIn("Guid.NewGuid(), false", src)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(", src)
        self.assertIn("CompleteTf4OperationalWinner()", src)
        self.assertIn("IdentityComparison.NormalizeText(", src)
        self.assertIn("V9_SYNTHETIC_SECOND_BATCH_INJECTED_FAILURE", src)
        self.assertIn('"CONCLUIDO_SEM_PUBLICACAO"', src)
        self.assertIn('"FALHOU"', src)
        self.assertIn("ReadPublicationFingerprintAsync", src)
        self.assertIn("JornadaHarness", src)
        self.assertIn("CREATE DATABASE", (REPO / ".github/workflows/v9-test-fullschema-runasync-smoke.yml").read_text())
        self.assertNotIn("UpdateRef", src)
        self.assertNotIn("PublishAsync(", src)
        self.assertNotIn("test_required=true", src)

    def test_scientific_test_stays_blocked(self):
        wf = TF4.read_text(encoding="utf-8")
        prep = wf.split("\n  prepare-test-100k:\n", 1)[1].split("\n  physical-100k:\n", 1)[0]
        train = wf.split("\n  physical-100k:\n", 1)[1].split("\n  audit-100k:\n", 1)[0]
        for stage in (prep, train):
            self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", stage)
        self.assertIn("test_required:", wf)
        self.assertIn("test-100k:", wf)


if __name__ == "__main__":
    unittest.main()

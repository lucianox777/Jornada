"""Fail-closed regression: independent scientific TEST requires real production parity.

This deliberately does not claim that SQL conversion or byte roundtrip proves
execution parity with the production SqlProbabilisticIdentityLinkage runner.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/v9-v3-four-name-100k.yml"


class ScientificTestParityGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_independent_test_is_explicitly_blocked_before_sealing(self):
        preparation = self.workflow.split("  prepare-test-100k:", 1)[1].split(
            "  physical-100k:", 1
        )[0]
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", preparation)
        self.assertIn("exit 1", preparation)
        self.assertLess(
            preparation.index("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED"),
            preparation.index("Generate independent TEST once"),
        )

    def test_physical_train_rejects_unavailable_scientific_test(self):
        physical = self.workflow.split("  physical-100k:", 1)[1].split(
            "  audit-100k:", 1
        )[0]
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", physical)
        self.assertIn("exit 1", physical)

    def test_sql_conversion_is_not_misrepresented_as_production_parity(self):
        independent = self.workflow.split("  test-100k:", 1)[1]
        self.assertIn("productionRunnerFullPipelineParityCertified", independent)
        self.assertIn("is False", independent)
        self.assertIn("certified", independent)
        self.assertIn("is False", independent)


    def test_test_only_adapter_cannot_be_mistaken_for_production_parity(self):
        runner = ROOT / "Solution/src/Jornada.Linkage.Runner"
        scorer = (runner / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text(encoding="utf-8")
        production = (runner / "SqlProbabilisticIdentityLinkage.cs").read_text(encoding="utf-8")
        self.assertIn("MaximumSyntheticObservations = 512", scorer)
        self.assertIn("frozenRows.Count is < 2 or > MaximumSyntheticObservations", scorer)
        self.assertIn("JornadaHarness", scorer)
        self.assertIn("ResolvePinnedV9FrozenTestCohortAsync", scorer)
        self.assertIn("LoadPinnedV9OperationalRuntimeAsync", production)
        self.assertIn("LoadPinnedV9GroupedCohortRuntimeAsync", production)
        self.assertIn("ResolvePinnedV9FrozenTestCohortAsync", production)
        # These diagnostic entry points do not prove the production ResolveWithoutCpf path.
        self.assertIn("ResolveWithoutCpfAsync", production)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", self.workflow)

    def test_operational_graph_is_not_replaced_by_physical_oracle(self):
        runner = ROOT / "Solution/src/Jornada.Linkage.Runner"
        scorer = (runner / "V9PinnedSyntheticGraphCohortRunScorer.cs").read_text(encoding="utf-8")
        parity = (runner / "V9OperationalBlockingChaveCohortParity.cs").read_text(encoding="utf-8")
        self.assertIn("operational.OperationalEdges.ToList()", scorer)
        self.assertIn("physical.Edges.SequenceEqual(operational.OperationalEdges)", scorer)
        self.assertIn("OperationalEdges", parity)
        self.assertNotIn("edges = physical.Edges.ToList()", scorer)


if __name__ == "__main__":
    unittest.main()

"""Regression guard: operational SQL edges reach the V9 scientific cohort runtime."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs"
AUDIT = ROOT / "src/Jornada.Linkage.Runner/V9OperationalCandidateGraphParityAudit.cs"


class OperationalGraphRuntimeContract(unittest.TestCase):
    def test_runtime_reconciles_full_graph_before_global_replay(self):
        source = CORE.read_text(encoding="utf-8")
        self.assertIn("ReplayOperationalGraph(", source)
        self.assertIn("actual.SetEquals(expected)", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_INVALID_OBSERVATION", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_DUPLICATE_OBSERVATION", source)
        self.assertIn("string.Equals(left.Gestor, right.Gestor, StringComparison.Ordinal)", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_INCOMPLETE_OR_DRIFTED", source)
        self.assertIn("return ReplayTest(test, ordered)", source)

    def test_operational_sql_graph_is_delivered_to_runtime(self):
        source = AUDIT.read_text(encoding="utf-8")
        self.assertIn("actualDirections.Values.Any(v => v != 3)", source)
        self.assertIn("expected.SetEquals(actual)", source)
        self.assertIn("operationalRuntime.ReplayOperationalGraph(test,", source)
        self.assertIn("ProductionBatchPipelineCertified: false", source)


if __name__ == "__main__":
    unittest.main()

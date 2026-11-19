"""Structural regression guard for diagnostic V9 operational graph replay."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs"
AUDIT = ROOT / "src/Jornada.Linkage.Runner/V9OperationalCandidateGraphParityAudit.cs"


class OperationalGraphRuntimeContract(unittest.TestCase):
    def test_rejects_incomplete_graph(self):
        source = CORE.read_text(encoding="utf-8")
        self.assertIn("ReplayOperationalGraph(", source)
        self.assertIn("actual.SetEquals(expected)", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_INVALID_EDGE", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_DUPLICATE_OBSERVATION", source)
        self.assertIn("V9_OPERATIONAL_GRAPH_INCOMPLETE_OR_DRIFTED", source)
        self.assertIn("return ReplayTest(test,", source)

    def test_identity_partition_is_compared_by_membership_not_guids(self):
        source = AUDIT.read_text(encoding="utf-8")
        self.assertIn("CanonicalGroups(reference.IdentityByObservation)", source)
        self.assertIn("CanonicalGroups(operational.IdentityByObservation)", source)
        self.assertIn("GroupBy(x => x.Value)", source)
        self.assertIn("SetEquals(operational.IdentityByObservation.Keys)", source)
        self.assertNotIn("id != x.Value", source)

    def test_audit_routes_operational_graph_to_runtime(self):
        source = AUDIT.read_text(encoding="utf-8")
        self.assertIn("operationalRuntime.ReplayOperationalGraph(test,", source)


if __name__ == "__main__":
    unittest.main()

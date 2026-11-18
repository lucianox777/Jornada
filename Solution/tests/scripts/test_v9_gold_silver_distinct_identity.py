import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class GoldSilverDistinctIdentityTests(unittest.TestCase):
    def test_graph_fans_out_to_silver_without_self_person_edges(self):
        code = (ROOT / "V9OperationalCohortGraphPreview.cs").read_text()
        self.assertIn("Dictionary<Guid, List<Source>>", code)
        self.assertIn("sources.Add(source)", code)
        self.assertIn("if (candidateUuid == source.GoldUuid) continue;", code)
        self.assertIn("foreach (var target in targets)", code)
        self.assertIn("byId.Add(source.ObservationId)", code)
        self.assertNotIn("byUuid.TryAdd(source.GoldUuid, source)", code)

    def test_sql_census_same_identity_semantics(self):
        code = (ROOT / "V9OperationalSqlEdgeCensus.cs").read_text()
        self.assertIn("Dictionary<Guid, List<V9OperationalCohortGraphPreview.Source>>", code)
        self.assertIn("if (candidate == source.GoldUuid) continue;", code)
        self.assertIn("foreach (var target in targets)", code)
        self.assertIn("observationIds.Add(source.ObservationId)", code)

    def test_gold_membership_counts_distinct_gold(self):
        membership = (ROOT / "V9OperationalGoldCohortMembership.cs").read_text()
        orchestrator = (ROOT / "V9OperationalSnapshotGraphVerification.cs").read_text()
        self.assertIn("expected.Add(source.GoldUuid);", membership)
        self.assertNotIn("!expected.Add(source.GoldUuid)", membership)
        self.assertIn("frozenCohort.Select(s => s.GoldUuid).Distinct().LongCount()", orchestrator)


if __name__ == "__main__":
    unittest.main()

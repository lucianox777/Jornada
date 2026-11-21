"""Static regression guards for the nonpublishing V9 collector (not runtime proof)."""
import unittest
from pathlib import Path

SOURCE = (Path(__file__).resolve().parents[2] /
          "src/Jornada.Linkage.Runner/V9DifferentialRunEvidenceCollector.cs").read_text(encoding="utf-8")


class CollectorContractTests(unittest.TestCase):
    def test_frozen_universe_and_single_use(self):
        for token in ("SortedSet<long>", "ids.Distinct().Count() != ids.Length",
                      "sealedEvidence = true", "Differential evidence already sealed"):
            with self.subTest(token=token):
                self.assertIn(token, SOURCE)

    def test_decisions_are_unique_and_complete(self):
        for token in ("decisions.TryAdd(observationId, decision)",
                      "decisions.Count != universe.Count", "universe.All(decisions.ContainsKey)"):
            with self.subTest(token=token):
                self.assertIn(token, SOURCE)

    def test_groups_partition_frozen_universe(self):
        for token in ("members.Distinct().Count() != universe.Count",
                      "universe.SetEquals(members)", "groups.OrderBy(g => g[0])"):
            with self.subTest(token=token):
                self.assertIn(token, SOURCE)

    def test_overlapping_groups_rejected_before_seal(self):
        for token in ("HashSet<long> groupedMembers", "ids.Any(groupedMembers.Contains)",
                      "groupedMembers.Add(id)"):
            with self.subTest(token=token):
                self.assertIn(token, SOURCE)

    def test_seal_only_after_serialization(self):
        self.assertLess(SOURCE.index("var json = JsonSerializer.Serialize("),
                        SOURCE.index("sealedEvidence = true;"))
        self.assertLess(SOURCE.index("sealedEvidence = true;"),
                        SOURCE.index("return json;"))

    def test_no_certification_or_implicit_production_wiring(self):
        self.assertIn("certified = false", SOURCE)
        self.assertIn("testTruthUsed = false", SOURCE)
        self.assertIn("Not connected to production DI or RunAsync", SOURCE)


if __name__ == "__main__":
    unittest.main()

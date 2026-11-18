import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class GoldSilverCardinalityTests(unittest.TestCase):
    def test_provenance_cardinality_does_not_assume_one_to_one(self):
        code = (ROOT / "V9OperationalGoldSilverCardinality.cs").read_text(encoding="utf-8")
        for token in ("IsolationLevel.Snapshot", "LEFT JOIN identidade.vinculo_fonte",
                      "LEFT JOIN silver.pessoa_observacao",
                      "COUNT_BIG(o.pessoa_observacao_id)",
                      "linked_observations > 1", "GoldWithMultipleObservations",
                      "RUNNER_V9_PROVENANCE_CARDINALITY_INCONSISTENT"):
            self.assertIn(token, code)
        for forbidden in ("INSERT INTO", "UPDATE ", "DELETE FROM", "TOP (", "NOLOCK"):
            self.assertNotIn(forbidden, code)


if __name__ == "__main__":
    unittest.main()

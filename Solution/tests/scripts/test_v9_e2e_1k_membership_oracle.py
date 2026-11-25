import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-e2e-1k-membership-oracle.py"
spec = importlib.util.spec_from_file_location("membership", SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class MembershipTests(unittest.TestCase):
    def setUp(self):
        self.e = dict(Observations=2034, CandidatePeople=100, SecureRecords=130,
                      EvidenceMembershipSha256="a"*64, SilverEdges=200,
                      SilverEdgesSha256="b"*64, SilverSqlReplayEqual=True,
                      ActualConsumedSilverEdgesVerified=False)
        self.o = dict(runId="synthetic-run", policy="V9_DEV_B1_SECURE_PERSISTENT_ORIGIN_RECORD_RANK_V1",
                      observations=2034, candidatePeople=100, secureRecords=130,
                      evidenceMembershipSha256="a"*64)
        self.r = dict(runId="synthetic-run", status="PUBLICADO", eligible=2034, evaluated=2034)

    def test_equal_census_does_not_certify_consumed_edges_or_scores(self):
        receipt = m.compare(self.e, self.o, self.r)
        self.assertEqual(receipt["status"], "PASS")
        self.assertFalse(receipt["actualConsumedSilverEdgesVerified"])
        self.assertFalse(receipt["independentScoreOracleVerified"])
        self.assertFalse(receipt["e2eSystemCertified"])

    def test_same_counts_different_membership(self):
        self.o["evidenceMembershipSha256"] = "c"*64
        with self.assertRaises(ValueError):
            m.compare(self.e, self.o, self.r)

    def test_count_and_run_drift(self):
        for field, value in (("candidatePeople", 99), ("secureRecords", 131), ("runId", "different")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                m.compare(self.e, {**self.o, field: value}, self.r)

    def test_directional_replay_requires_all_directions_and_abstentions(self):
        self.e["SilverDirectionalReplay"] = dict(DirectionsCompared=400,
            ScoredDirections=360, AbstainedDirections=40)
        self.assertEqual(m.verify_directional_replay(self.e)["silverDirectionsCompared"], 400)
        for field, value in (("DirectionsCompared", 399), ("ScoredDirections", 359),
                             ("AbstainedDirections", True), ("AbstainedDirections", -1)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                m.verify_directional_replay({**self.e, "SilverDirectionalReplay":
                    {**self.e["SilverDirectionalReplay"], field: value}})

    def test_graph_replay_required(self):
        self.e["SilverSqlReplayEqual"] = False
        with self.assertRaises(ValueError):
            m.compare(self.e, self.o, self.r)

if __name__ == "__main__":
    unittest.main()

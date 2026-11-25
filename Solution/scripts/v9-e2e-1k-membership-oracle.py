#!/usr/bin/env python3
"""Reconcile the independent pre-FULL census against the actual b1 scorer receipt."""
import argparse
import json
import re
from pathlib import Path

def require(value):
    if not value:
        raise ValueError("MEMBERSHIP_DIVERGENCE")

def compare(expected, observed, runner):
    require(expected["SilverSqlReplayEqual"] is True
            and expected["ActualConsumedSilverEdgesVerified"] is False)
    require(observed["runId"].lower() == runner["runId"].lower()
            and runner["status"] == "PUBLICADO"
            and observed["policy"] == "V9_DEV_B1_SECURE_PERSISTENT_ORIGIN_RECORD_RANK_V1")
    for a, b in (("Observations", "observations"), ("CandidatePeople", "candidatePeople"),
                 ("SecureRecords", "secureRecords"), ("EvidenceMembershipSha256", "evidenceMembershipSha256")):
        require(type(expected[a]) is type(observed[b]) and expected[a] == observed[b])
    require(expected["Observations"] == runner["eligible"] == runner["evaluated"]
            and expected["Observations"] > 0 and expected["CandidatePeople"] > 0
            and expected["SecureRecords"] > 0 and expected["SilverEdges"] > 0
            and re.fullmatch("[0-9a-f]{64}", expected["EvidenceMembershipSha256"])
            and re.fullmatch("[0-9a-f]{64}", expected["SilverEdgesSha256"]))
    return {"schema": "V9_E2E_1K_MEMBERSHIP_ORACLE_V1", "status": "PASS",
            "scope": "ACTUAL_B1_MEMBERSHIP_AND_SEPARATE_PRE_FULL_SILVER_SQL_REPLAY",
            "observations": expected["Observations"], "candidatePeople": expected["CandidatePeople"],
            "secureRecords": expected["SecureRecords"], "membershipSha256": expected["EvidenceMembershipSha256"],
            "silverEdges": expected["SilverEdges"], "silverEdgesSha256": expected["SilverEdgesSha256"],
            "silverSqlReplayEqual": True, "actualConsumedSilverEdgesVerified": False,
            "independentScoreOracleVerified": False, "e2eSystemCertified": False}

def verify_directional_replay(expected):
    row = expected["SilverDirectionalReplay"]
    compared, scored, abstained = (row[k] for k in
        ("DirectionsCompared", "ScoredDirections", "AbstainedDirections"))
    require(all(type(x) is int and x >= 0 for x in (compared, scored, abstained))
            and compared == 2 * expected["SilverEdges"] == scored + abstained
            and scored > 0)
    return {"silverDirectionalReplayVerified": True, "silverDirectionsCompared": compared,
            "silverScoredDirections": scored, "silverAbstainedDirections": abstained}

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, required=True)
    a = p.parse_args()
    try:
        def read(name):
            f = a.root / name
            require(f.is_file() and not f.is_symlink() and 0 < f.stat().st_size <= 32 * 1024 * 1024)
            return f.read_text(encoding="utf-8")
        expected = json.loads(read("membership-expected.json"))
        runner = json.loads(read("runner-receipt.json"))
        lines = [x[len("V9_DEV_B1_EVIDENCE "):] for x in read("runner-full.log").splitlines()
                 if x.startswith("V9_DEV_B1_EVIDENCE ")]
        require(len(lines) == 1)
        observed = json.loads(lines[0])
        receipt = compare(expected, observed, runner)
        scalar = json.loads(read("scalar-receipt.json"))
        require(scalar["IndependentRankVerified"] is True
                and scalar["IndependentR1DecisionVerified"] is True
                and expected["FrozenGroupReplayEqual"] is True
                and 0 < expected["IndependentGroupCount"] <= runner["evaluated"]
                and re.fullmatch("[0-9a-f]{64}", expected["IndependentGroupSha256"])
                and scalar["Observations"] == scalar["RankRowsEqual"] == runner["evaluated"]
                and scalar["ScoreableRecords"] == observed["scoreableRecords"]
                and scalar["PeopleWithoutScoreableEvidence"] == observed["peopleWithoutScoreableEvidence"])
        receipt.update(verify_directional_replay(expected))
        receipt["independentScoreOracleVerified"] = True
        receipt["independentR1DecisionVerified"] = True
        receipt["frozenGroupReplayEqual"] = True
        receipt["independentGroupCount"] = expected["IndependentGroupCount"]
        receipt["independentGroupSha256"] = expected["IndependentGroupSha256"]
        receipt["scope"] = "ACTUAL_B1_MEMBERSHIP_RANK_R1_LEDGER_AND_SEPARATE_SILVER_DIRECTION_GROUP_REPLAY"
        with (a.root / "membership-receipt.json").open("x", encoding="utf-8") as f:
            json.dump(receipt, f, sort_keys=True)
        print("V9_E2E_1K_ACTUAL_B1_RANK_R1_AND_SEPARATE_GRAPH_GROUP_REPLAY_PASS")
    except (ValueError, KeyError, TypeError, OSError, AttributeError):
        raise SystemExit("V9_E2E_1K_MEMBERSHIP_ORACLE_REJECTED") from None

if __name__ == "__main__":
    main()

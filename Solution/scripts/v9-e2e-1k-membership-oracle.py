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
        receipt = compare(expected, json.loads(lines[0]), runner)
        with (a.root / "membership-receipt.json").open("x", encoding="utf-8") as f:
            json.dump(receipt, f, sort_keys=True)
        print("V9_E2E_1K_ACTUAL_B1_MEMBERSHIP_AND_SILVER_REPLAY_PASS_NOT_SCORE_ORACLE")
    except (ValueError, KeyError, TypeError, OSError, AttributeError):
        raise SystemExit("V9_E2E_1K_MEMBERSHIP_ORACLE_REJECTED") from None

if __name__ == "__main__":
    main()

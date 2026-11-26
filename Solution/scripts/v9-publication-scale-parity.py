#!/usr/bin/env python3
"""Fail closed on mismatch of two physical same-input 1k FULL pairs."""
import importlib.util,json,os,re,tempfile,zipfile
from pathlib import Path
spec=importlib.util.spec_from_file_location("preserved",Path(__file__).with_name("v9-preserve-campaign38-artifacts.py"))
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
token=os.environ["GH_TOKEN"];sha=os.environ["EXPECTED"];run=os.environ["GITHUB_RUN_ID"]
def require(ok):
    if not ok:raise SystemExit("V9_PUBLICATION_SCALE_PARITY_FAILED")
artifacts=p.api_json(f"https://api.github.com/repos/{p.REPO}/actions/runs/{run}/artifacts?per_page=100",token)["artifacts"]
def read_pair(artifact, label, directory):
    require(artifact["digest"].startswith("sha256:"))
    tmp, _ = p.download_verified(artifact["id"], artifact["digest"][7:], directory, token)
    with zipfile.ZipFile(tmp) as archive:
        frame = json.loads(archive.read("pair-receipt.json"))
        receipts = [json.loads(archive.read(name)) for name in
                    ("diagnostic-receipt.json", "restored-diagnostic-receipt.json")]
    require(frame["schema"] == "V9_RESTORED_PREFULL_PAIR_V1" and frame["commit"] == sha
            and frame["inputVerified"] is True and frame["distinctPublishedRuns"] is True
            and frame["rawEdgesAndScoresEqual"] is True
            and type(frame["references"]) is int and frame["references"] > 0
            and type(frame["observations"]) is int
            and frame["observations"] == receipts[0]["versions"] == receipts[1]["versions"])
    other = "optimized1k" if label == "diagnostic1k" else "diagnostic1k"
    for row, profile in zip(receipts, (label, other)):
        require(row["commit"] == sha and row["profile"] == profile and row["population"] == 1000
                and row["fullCompleted"] is True and row["independentMembershipVerified"] is True
                and row["identityCensusStatus"] == "PASS" and row["matrixExecuted"] is False
                and row["winnerSha256"] == p.WINNER_SHA)
    return receipts

def compare(receipts):
    # Keep every original equality mandatory. Report only fixed field names,
    # never payloads, raw UUIDs, keys, scores or individual records.
    fields=("versions","eligible","winnerSha256","pairMetrics","normalizedPublicationSha256","normalizedDecisionGraphSha256")
    different=[k for k in fields if receipts[0][k]!=receipts[1][k]]
    if different:print("V9_PARITY_DIFFERENT_FIELDS="+",".join(different))
    expected_fields={"graph."+k for k in ("group","candidates","neighbors","minimum","reason")} | {
        "rank."+k for k in ("Best","BestScore","Second","SecondScore","Margin","ScoreableRecords",
            "PeopleWithoutScoreableEvidence","CandidateCount","HasPass","Status","Resolved","Reason")}
    diagnostics=[r.get("decisionFieldSha256") for r in receipts]
    require(all(isinstance(d,dict) and set(d)==expected_fields and
        all(isinstance(v,str) and re.fullmatch("[0-9a-f]{64}",v) for v in d.values()) for d in diagnostics))
    components=sorted(k for k in expected_fields if diagnostics[0][k]!=diagnostics[1][k])
    if components:print("V9_PARITY_DIFFERENT_GRAPH_COMPONENTS="+",".join(components))
    # Additional proof of normalized per-record evidence and score tiers. The
    # strict Best/Second equality above is still mandatory, even on tied ranks.
    gold=[r.get("goldRankDiagnostics") for r in receipts]
    gold_fields={"normalizedGoldRecordScoreSha256","normalizedGoldScoreTiersSha256"}
    count_fields={"rows","scoreRecords","tiedBestRows","tiedSecondRows","tiedBestOrSecondRows","tiedResolvedRows"}
    require(all(isinstance(g,dict) and set(g)==gold_fields|{"counts","rawUuidRankVerified"} and
        g["rawUuidRankVerified"] is True and all(isinstance(g[k],str) and re.fullmatch("[0-9a-f]{64}",g[k]) for k in gold_fields) and
        isinstance(g["counts"],dict) and set(g["counts"])==count_fields and
        all(type(v) is int and v>=0 for v in g["counts"].values()) for g in gold))
    gold_diff=sorted(k for k in gold_fields|{"counts"} if gold[0][k]!=gold[1][k])
    print("V9_PARITY_GOLD_DIAGNOSTICS="+("DIFFERENT:"+",".join(gold_diff) if gold_diff else "EQUAL"))
    for row,g in zip(receipts,gold):
        label=row["profile"]
        print("V9_PARITY_GOLD_COUNTS_"+label+"="+json.dumps(g["counts"],sort_keys=True))
    return not different and not components and not gold_diff

pairs = []
with tempfile.TemporaryDirectory() as d:
    for label in ("diagnostic1k", "optimized1k"):
        rows = [a for a in artifacts if a["name"] == f"v9-phase4-scale-{label}-{sha}" and not a["expired"]]
        require(len(rows) == 1)
        pair = read_pair(rows[0], label, Path(d))
        pairs.append(pair)
        print("V9_PARITY_RESTORED_PAIR=" + label)
        require(compare(pair))

# Keep independent-ingestion diagnostics for historical comparison. Their
# different UUIDs are intentionally outside the same-input migration gate.
print("V9_PARITY_INDEPENDENT_INPUT_DIAGNOSTIC")
print("V9_PARITY_INDEPENDENT_INPUT_EQUAL=" + str(compare([pair[0] for pair in pairs])).lower())
print("V9_PUBLICATION_SCALE_NORMALIZED_1K_PARITY_PASS")

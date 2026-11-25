#!/usr/bin/env python3
"""Fail closed on mismatch of the two independently audited synthetic FULLs."""
import importlib.util,json,os,re,tempfile,zipfile
from pathlib import Path
spec=importlib.util.spec_from_file_location("preserved",Path(__file__).with_name("v9-preserve-campaign38-artifacts.py"))
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
token=os.environ["GH_TOKEN"];sha=os.environ["EXPECTED"];run=os.environ["GITHUB_RUN_ID"]
def require(ok):
    if not ok:raise SystemExit("V9_PUBLICATION_SCALE_PARITY_FAILED")
artifacts=p.api_json(f"https://api.github.com/repos/{p.REPO}/actions/runs/{run}/artifacts?per_page=100",token)["artifacts"]
receipts=[]
with tempfile.TemporaryDirectory() as d:
    for label in ("diagnostic1k","optimized1k"):
        rows=[a for a in artifacts if a["name"]==f"v9-phase4-scale-{label}-{sha}" and not a["expired"]]
        require(len(rows)==1 and rows[0]["digest"].startswith("sha256:"))
        tmp,_=p.download_verified(rows[0]["id"],rows[0]["digest"][7:],Path(d),token)
        with zipfile.ZipFile(tmp) as z:
            row=json.loads(z.read("diagnostic-receipt.json"))
        require(row["commit"]==sha and row["profile"]==label and row["population"]==1000 and row["fullCompleted"]
                and row["independentMembershipVerified"] and row["identityCensusStatus"]=="PASS")
        receipts.append(row)
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
require(not different and not components)
print("V9_PUBLICATION_SCALE_NORMALIZED_1K_PARITY_PASS")

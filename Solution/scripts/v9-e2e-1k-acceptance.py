#!/usr/bin/env python3
"""Physical phase-3 nucleus receipt. CI and squash remain external governance."""
import json
import os
from pathlib import Path
import re
import sys
import subprocess

def require(ok):
    if not ok: raise ValueError("V9_E2E_NUCLEUS_RECEIPT_DIVERGENCE")

def accept(root, sha):
    require(re.fullmatch("[0-9a-f]{64}|[0-9a-f]{40}",sha) and len(sha)==40)
    def read(path):
        f=root/path
        require(f.is_file() and not f.is_symlink() and 0<f.stat().st_size<=1024*1024)
        return json.loads(f.read_text(encoding="utf-8"))
    winner=read("selection/winner-pin-receipt.json")
    lineage=read("http-lineage.json")
    runner=read("runner-receipt.json")
    ident=read("identity-receipt.json")
    mem=read("membership-receipt.json")
    scalar=read("scalar-receipt.json")
    fn=read("fn-receipt.json")
    factual=read("fixture/factual-receipt.json")
    person=read("fixture/person-census-receipt.json")
    api=read("fixture/api-receipt.json")
    system=read("fixture/system-api-receipt.json")
    require(all(x["status"]=="PASS" for x in (winner,lineage,ident,mem,fn,factual,person,api,system)))
    require(winner["artifactId"]==14742452561 and winner["winnerSha256"]==runner["winnerSha256"]==ident["winnerSha256"]
            =="6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"
            and winner["scientificTest"] is False and winner["activated"] is False and winner["refitted"] is False)
    require(runner["runId"]==ident["runId"] and runner["mode"]=="FULL" and runner["status"]=="PUBLICADO"
            and runner["automaticV9Index"] is True and runner["scientificTest"] is False and runner["activated"] is False
            and runner["eligible"]==runner["evaluated"]==mem["observations"]==scalar["RankRowsEqual"]==ident["runnerEligible"]
            and ident["baseIdentities"]==1000 and ident["sourceVersions"]==lineage["sourceVersions"]
            and lineage["deliveries"]==lineage["sameKeyReplayDeliveries"]==8)
    require(all(mem[k] is True for k in ("actualConsumedSilverEdgesVerified","actualConsumedScoresVerified",
                  "actualConsumedGroupsVerified","actualGoldRecordScoresVerified","independentR1DecisionVerified","independentScoreOracleVerified"))
            and mem["silverDirectionsCompared"]==2*mem["silverEdges"]
            and mem["goldRecordScores"]==mem["secureRecords"]
            and scalar["IndependentRankVerified"] is True and scalar["IndependentR1DecisionVerified"] is True)
    require(ident["pairMetrics"]["fn"]==fn["fn"] and ident["pairMetrics"]["tp"]==fn["tp"]
            and ident["pairMetrics"]["fp"]==0 and ident["falseFusedGoldIdentities"]==0)
    require(factual["truthOutsideZip"] is True and factual["facts"]==16
            and factual["attributed"]==factual["apiFacts"] and factual["attributed"]+factual["unattributed"]==16
            and person["goldPeople"]==person["servingPeople"] and person["resolvedReferencePeople"]>0
            and api["cpfResolveMatchesSql"] is True and api["associatedPersonRoutes"]==3)
    require(system["resultadoDeliveries"]==system["monitorDeliveries"]==8 and system["monitorFullVerified"] is True
            and system["openapiContractGates"]==2 and system["semiblindEnabledVerified"] is False)
    return {"schema":"V9_E2E_PHASE3_NUCLEUS_V1","commit":sha,"runId":runner["runId"],
            "E2E_NUCLEO_PASS":True,"E2E_INTEGRAL_PASS":False,
            "notCertified":["APOIO_1194_1855","SEMIBLIND_ENABLED_539_1854","PRODUCTION","SCIENTIFIC","MULTIPLE_PHYSICAL_FULLS"],
            "scope":"DEV_1K_SINGLE_PHYSICAL_FULL_NUCLEUS",
            "requiresExternalExactHeadCIAndSquash":True}

if __name__=="__main__":
    try:
        root=Path(sys.argv[1]);sha=os.environ["JORNADA_E2E_EXACT_SHA"]
        require(os.environ.get("GITHUB_ACTIONS")=="true"
                and os.environ.get("JORNADA_RUNTIME_MODE")=="DEV"
                and os.environ.get("JORNADA_E2E_V9_DEV_FULL")=="true"
                and subprocess.check_output(["git","rev-parse","HEAD"],text=True).strip()==sha)
        receipt=accept(root,sha)
        with (root/"nucleus-receipt.json").open("x",encoding="utf-8") as f:json.dump(receipt,f,sort_keys=True)
    except (OSError,ValueError,KeyError,TypeError,AttributeError,subprocess.SubprocessError):
        raise SystemExit("V9_E2E_NUCLEUS_RECEIPT_REJECTED") from None
    print("E2E_NUCLEO_PASS=true E2E_INTEGRAL_PASS=false PHYSICAL_SCOPE_ONLY")

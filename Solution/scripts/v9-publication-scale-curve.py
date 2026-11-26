#!/usr/bin/env python3
"""Audit same-SHA migrated DEV FULL curve; never treat incomplete evidence as PASS."""
import argparse
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import tempfile
import zipfile

LABELS = ("optimized1k", "optimized2k", "optimized4k")
POPULATIONS = (1000, 2000, 4000)
TIMINGS = ("fullElapsedMs", "physicalReadMs", "physicalSnapshotMs",
           "silverGroupingMs", "goldScoringMs", "translationMs", "remainingFullLifecycleMs")
WINNER = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"

def require(ok):
    if not ok:
        raise ValueError("V9_SCALE_CURVE_EVIDENCE_REJECTED")

def number(x):
    return type(x) in (int, float) and math.isfinite(x) and x >= 0

def phase_ms(diag):
    require(diag["schema"] == "V9_DEV_FULL_DIAGNOSTIC_V1"
            and diag["status"] == "FULL_COMPLETED" and diag["rawPlanExported"] is False
            and number(diag["elapsedMs"]) and diag["elapsedMs"] > 0)
    phases = diag["phases"]
    require(isinstance(phases, list) and len(phases) > 0)
    result = {}
    previous = 0
    active = "full-start"
    for row in phases:
        label, end = row["phase"], row["elapsedMs"]
        require(isinstance(label, str) and re.fullmatch(r"[a-z][a-z0-9-]{0,79}", label)
                and number(end) and previous <= end <= diag["elapsedMs"])
        result[active] = result.get(active, 0) + end - previous
        active, previous = label, end
    result[active] = result.get(active, 0) + diag["elapsedMs"] - previous
    require(active == diag["finalPhase"])
    require(abs(sum(result.values()) - diag["elapsedMs"]) < 1e-6)
    return result

def ratios(left, right):
    require(set(left) == set(right))
    return {key: (right[key]/left[key] if left[key] > 0 else None)
            for key in sorted(left)}

EXTRA_COUNTS = ("peakTempdbUserBytes","peakTempdbInternalBytes","peakTempdbVersionBytes",
                "peakBlockingIndexBytes","finalBlockingIndexBytes","peakHostMemoryUsedBytes","minFreeDiskBytes",
                "databaseReadBytes","databaseWriteBytes","tempdbReadBytes","tempdbWriteBytes","instanceBatchRequests")

def extra_metrics(extra):
    if extra is None:
        return dict(status="ABSENT")
    require(extra["schema"]=="V9_DEV_FULL_EXTRA_RESOURCE_V1"
            and extra["sampleScope"]=="FULL_WITH_OBSERVER_OVERHEAD"
            and extra["sqlBatchScope"]=="DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS"
            and extra["exactRunnerSqlStatementCount"] is False and extra["rawSqlExported"] is False
            and extra["status"] in ("MEASURED","INCOMPLETE")
            and type(extra["sampleCount"]) is int and extra["sampleCount"]>=0)
    result=dict(status=extra["status"],sampleCount=extra["sampleCount"])
    if extra["status"]=="MEASURED":
        require(extra["sampleCount"]>=2 and extra["probeError"] is None
                and all(type(extra[k]) is int and extra[k]>=0 for k in EXTRA_COUNTS)
                and extra["peakBlockingIndexBytes"]>=extra["finalBlockingIndexBytes"]
                and number(extra["runnerCpuMs"]) and number(extra["hostCpuBusyFraction"])
                and extra["hostCpuBusyFraction"]<=1)
        result.update({k:extra[k] for k in EXTRA_COUNTS+("runnerCpuMs","hostCpuBusyFraction")})
    else:
        error=extra["probeError"]
        allowed={"FULL_RESOURCE_NO_ROW","FULL_HOST_CPU","InvalidDataException",
                 "InvalidOperationException","IOException","UnauthorizedAccessException",
                 "FormatException","OverflowException","KeyNotFoundException"}
        allowed|={"FULL_RESOURCE_"+k+"_"+suffix for k in (
            "TEMPDB_USER","TEMPDB_INTERNAL","TEMPDB_VERSION","BLOCKING_INDEX","DATABASE_READ",
            "DATABASE_WRITE","TEMPDB_READ","TEMPDB_WRITE","BATCH_REQUESTS") for suffix in ("NULL","NEGATIVE")}
        result["probeError"]=("INCOMPLETE_WITHOUT_CODE" if error is None else error
            if isinstance(error,str) and (error in allowed or re.fullmatch(r"SQL_-?[0-9]{1,10}_[0-9]{1,3}",error))
            else "UNCLASSIFIED")
    return result

def runner_sql_metrics(receipt):
    metrics = receipt.get("runnerSql")
    if metrics is None:
        return dict(status="ABSENT")
    counters = ("completedRpc", "completedBatches", "completedSqlStatements",
                "completedProcedureStatements", "topLevelSqlCpuMicroseconds", "eventFiles")
    require(metrics.get("schema") == "V9_DEV_FULL_SQL_OPERATIONS_V1"
            and metrics.get("scope") == "TAGGED_RUNNER_AND_SCORER_SQL"
            and metrics.get("droppedEvents") == 0 and metrics.get("rawEventsExported") is False
            and all(type(metrics[k]) is int and metrics[k] >= 0 for k in counters)
            and 1 <= metrics["eventFiles"] <= 3
            and metrics["completedRpc"] + metrics["completedBatches"] > 0
            and metrics["completedSqlStatements"] + metrics["completedProcedureStatements"] > 0)
    return dict(status="MEASURED", scope=metrics["scope"], **{k: metrics[k] for k in counters})

def assess(frames, sha, extras=None):
    require(len(frames) == 3 and re.fullmatch(r"[0-9a-f]{40}", sha))
    if extras is None: extras=[None]*3
    require(len(extras)==3)
    rows = []
    for (receipt, member, diag, resource), label, n, extra in zip(frames, LABELS, POPULATIONS, extras):
        require(receipt["commit"] == sha and receipt["profile"] == label
                and receipt["population"] == n and receipt["winnerSha256"] == WINNER
                and receipt["fullCompleted"] is True
                and receipt["independentMembershipVerified"] is True
                and receipt["identityCensusStatus"] == "PASS"
                and receipt["matrixExecuted"] is False and receipt["scientificTest"] is False
                and receipt["activation"] is False)
        require(member["status"] == "PASS" and member["observations"] == receipt["eligible"]
                and all(member[k] is True for k in ("actualConsumedSilverEdgesVerified",
                    "actualConsumedScoresVerified", "actualConsumedGroupsVerified",
                    "actualGoldRecordScoresVerified", "independentR1DecisionVerified")))
        full = receipt["physicalFull"]
        timing = {k:full[k] for k in TIMINGS}
        require(all(number(x) for x in timing.values()) and timing["fullElapsedMs"] > 0)
        require(resource["status"] == "COMPLETE" and resource["exitCode"] == 0)
        counts = {k:member[k] for k in ("observations","candidatePeople","secureRecords",
                  "silverEdges","silverDirectionsCompared","goldRecordScores")}
        counts["versions"] = receipt["versions"]
        require(all(type(x) is int and x >= 0 for x in counts.values()))
        mem = {k:full[k] for k in ("sampledPeakVersionStoreBytes","sampledPeakWorkingSetBytes",
                                 "processPeakWorkingSetBytes")}
        host = {k:resource[k] for k in ("peakHostMemoryUsed","minMemoryAvailable","minDiskFree")}
        require(all(type(x) is int and x >= 0 for x in (mem|host).values()))
        stages = {k: receipt[k] for k in ("ingestElapsedMs", "censusElapsedMs") if k in receipt}
        require(all(number(value) for value in stages.values()))
        rows.append(dict(profile=label,population=n,counts=counts,fullTimingMs=timing,
                         separatelyMeasuredStagesMs=stages,runnerSql=runner_sql_metrics(receipt),
                         exclusiveDiagnosticPhaseMs=phase_ms(diag),fullMemory=mem,
                         pipelineHostResources=host,extraFullResources=extra_metrics(extra)))
    t = ratios(rows[1]["fullTimingMs"], rows[2]["fullTimingMs"])
    p = ratios(rows[1]["exclusiveDiagnosticPhaseMs"],rows[2]["exclusiveDiagnosticPhaseMs"])
    failures = sorted([f"timing:{k}" for k,v in t.items() if v is not None and v>2.5]
                      +[f"phase:{k}" for k,v in p.items() if v is not None and v>2.5])
    unmeasurable = sorted([f"timing:{k}" for k,v in t.items() if v is None]
                          +[f"phase:{k}" for k,v in p.items() if v is None])
    # These counts cover the two statement completion streams and the top-level
    # RPC/batch streams separately. Do not sum the layers or equate those CPU
    # counters with background SQL or host CPU. V8 has its own physical gate.
    missing = ["independentOperationalV8Regression"]
    if any(row["runnerSql"]["status"] != "MEASURED" for row in rows):
        missing += ["exactSqlOperations", "sqlCpuAndExactRunnerAttribution"]
    if any(row["extraFullResources"]["status"] != "MEASURED" for row in rows):
        missing += ["fullTempdbUsage", "physicalIndexBytes", "fullCpuAndDiskIo"]
    return dict(schema="V9_MIGRATED_CURVE_DIAGNOSTIC_V1",commit=sha,rows=rows,
                fullTimingRatios2kTo4k=t,exclusivePhaseRatios2kTo4k=p,
                overLimit=failures,unmeasurable=unmeasurable,
                timingGate="FAIL" if failures else "INCOMPLETE" if unmeasurable else "PASS",
                missingEvidence=missing,phaseAComplete=False)

def main():
    # This can inspect a completed exact-SHA run without recomputing a corpus.
    a=argparse.ArgumentParser()
    a.add_argument("--run",required=True,type=int)
    a.add_argument("--sha",required=True)
    a.add_argument("--current-run",action="store_true")
    args=a.parse_args()
    spec=importlib.util.spec_from_file_location("preserved",Path(__file__).with_name("v9-preserve-campaign38-artifacts.py"))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    token=os.environ["GH_TOKEN"]
    root=f"https://api.github.com/repos/{module.REPO}/actions"
    run=module.api_json(f"{root}/runs/{args.run}",token)
    require(run["id"]==args.run and run["head_sha"]==args.sha
            and run["path"].split("@")[0]==".github/workflows/jornada-v9-phase4-scale.yml"
            and run["event"]=="pull_request")
    require((run["status"]=="completed" and run["conclusion"] in ("success","failure","cancelled","timed_out"))
            or (args.current_run and os.environ.get("GITHUB_ACTIONS")=="true"
                and os.environ.get("GITHUB_REPOSITORY")==module.REPO
                and os.environ.get("GITHUB_RUN_ID")==str(args.run)
                and run["status"]=="in_progress"))
    artifacts=module.api_json(f"{root}/runs/{args.run}/artifacts?per_page=100",token)
    require(artifacts["total_count"]==len(artifacts["artifacts"]))
    with tempfile.TemporaryDirectory() as folder:
        def get(prefix,label,members,optional=()):
            name=f"{prefix}-{label}-{args.sha}"
            found=[x for x in artifacts["artifacts"] if x["name"]==name and not x["expired"]]
            require(len(found)==1 and re.fullmatch("sha256:[0-9a-f]{64}",found[0]["digest"])
                    and 0<found[0]["size_in_bytes"]<=16*1024*1024)
            tmp,_=module.download_verified(found[0]["id"],found[0]["digest"][7:],Path(folder),token)
            try:
                with zipfile.ZipFile(tmp) as z:
                    result=[]
                    for m in members+optional:
                        if m in optional and m not in z.namelist():
                            result.append(None);continue
                        info=z.getinfo(m)
                        require(z.namelist().count(m)==1 and 0<info.file_size<=32*1024*1024)
                        result.append(json.loads(z.read(info)))
                    return result
            finally:Path(tmp).unlink(missing_ok=True)
        frames=[];extras=[]
        for label in LABELS:
            receipt,member=get("v9-phase4-scale",label,("diagnostic-receipt.json","membership-receipt.json"))
            diag,extra=get("v9-full-diagnostic",label,("v9-full-diagnostic.json",),("v9-full-extra-resource.json",))
            extras.append(extra)
            resource,=get("v9-scale-resource",label,("v9-scale-resource-receipt.json",))
            frames.append((receipt,member,diag,resource))
        result=assess(frames,args.sha,extras)
        result["runConclusion"]=run["conclusion"]
        # Timing evidence remains useful even if another gate fails. Never
        # infer strict parity from timings or from a run-level conclusion.
        print(json.dumps(result,sort_keys=True,allow_nan=False))

if __name__=="__main__":
    try:main()
    except Exception:raise SystemExit("V9_SCALE_CURVE_EVIDENCE_REJECTED") from None

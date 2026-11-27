#!/usr/bin/env python3
"""Audit the DEV curve or FULL10k evidence; never run a corpus or pass incomplete evidence."""
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
FULL10K_POPULATION = 10000
FULL10K_REFERENCE_RUNS = (45910892664, 45913982837, 45916702378)
FULL10K_REFERENCES = {
    "candidatePeople": 1034,
    "silverEdges": 8524,
    "silverDirectionsCompared": 17048,
    "goldRecordScores": 1820,
    "instanceBatchRequests": 84941,
}

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
    require(isinstance(metrics, dict))
    if metrics.get("schema") == "V9_DEV_FULL_SQL_OPERATIONS_V1":
        return dict(status="ARCHIVE_UNRECONCILED")
    counters = ("completedRpc", "completedBatches", "completedSqlStatements",
                "completedProcedureStatements", "topLevelSqlCpuMicroseconds", "eventFiles",
                "serverCompletedRpc", "serverCompletedBatches")
    require(metrics.get("schema") == "V9_DEV_FULL_SQL_OPERATIONS_V2"
            and metrics.get("scope") == "TAGGED_RUNNER_AND_SCORER_SQL"
            and metrics.get("droppedEvents") == 0 and metrics.get("rawEventsExported") is False
            and all(type(metrics[k]) is int and metrics[k] >= 0 for k in counters)
            and 1 <= metrics["eventFiles"] <= 4
            and metrics["serverCompletedRpc"] == metrics["completedRpc"]
            and metrics["serverCompletedBatches"] == metrics["completedBatches"]
            and metrics["completedRpc"] + metrics["completedBatches"] > 0
            and metrics["completedSqlStatements"] + metrics["completedProcedureStatements"] > 0)
    return dict(status="MEASURED", scope=metrics["scope"], **{k: metrics[k] for k in counters})

def operational_v8_evidence(pairs, sha):
    if pairs is None:
        return None
    require(isinstance(pairs,list) and len(pairs)==2)
    expected={("diagnostic1k","optimized1k"):("BASELINE","20261126_V3"),
              ("optimized1k","diagnostic1k"):("20261126_V3","BASELINE")}
    directions=set()
    rows=[]
    for receipt in pairs:
        fields={"schema","commit","primaryProfile","restoredProfile","inputVerified",
                "distinctPublishedRuns","rawEdgesAndScoresEqual","observations","references",
                "operationalV8RegressionVerified","operationalV8MutationDetected",
                "operationalV8SemanticSha256","operationalV8DecisionRows","operationalV8GoldRows",
                "operationalV8ModelId","operationalV8RuleSetFingerprintSha256",
                "operationalV8Stages","operationalV8Scenarios"}
        require(isinstance(receipt,dict) and set(receipt)==fields
                and receipt["schema"]=="V9_RESTORED_PREFULL_PAIR_V3"
                and receipt["commit"]==sha
                and all(receipt[k] is True for k in ("inputVerified","distinctPublishedRuns",
                    "rawEdgesAndScoresEqual","operationalV8RegressionVerified",
                    "operationalV8MutationDetected"))
                and type(receipt["observations"]) is int and receipt["observations"]>0
                and type(receipt["references"]) is int and receipt["references"]>0
                and type(receipt["operationalV8DecisionRows"]) is int
                and receipt["operationalV8DecisionRows"]==4
                and receipt["operationalV8Scenarios"]==[
                    "BELOW_THRESHOLD","CONFLICT","NO_CANDIDATE","RESOLVED"]
                and type(receipt["operationalV8GoldRows"]) is int
                and receipt["operationalV8GoldRows"]>0
                and re.fullmatch(r"[0-9a-f]{64}",receipt["operationalV8SemanticSha256"])
                and receipt["operationalV8ModelId"]=="88000000-0000-4000-8000-000000008008"
                and receipt["operationalV8RuleSetFingerprintSha256"]==
                    "017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd")
        direction=(receipt["primaryProfile"],receipt["restoredProfile"])
        require(direction in expected and direction not in directions
                and receipt["operationalV8Stages"]==list(expected[direction]))
        directions.add(direction)
        rows.append(dict(primaryProfile=direction[0],restoredProfile=direction[1],
            semanticSha256=receipt["operationalV8SemanticSha256"],
            decisionRows=receipt["operationalV8DecisionRows"],
            scenarios=receipt["operationalV8Scenarios"],
            goldRows=receipt["operationalV8GoldRows"]))
    require(directions==set(expected))
    return dict(status="PASS",entrypoint="ProbabilisticLinkageBatchRunner.RunAsync",
                strictBestSecond=True,tolerance=None,fixtureLifecycle="NO_TRAIN_NO_VALIDATION_NO_ACTIVATION",
                modelId="88000000-0000-4000-8000-000000008008",
                ruleSetFingerprintSha256="017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd",
                forwardAndRollbackVerified=True,mutationDetected=True,rows=rows)

def deterministic_counter_gate(observed):
    require(isinstance(observed, dict) and set(observed) == set(FULL10K_REFERENCES)
            and all(type(value) is int and value >= 0 for value in observed.values()))
    counters = {}
    over_limit = []
    for key, reference in FULL10K_REFERENCES.items():
        value = observed[key]
        observed_times_10 = value * 10
        reference_times_34 = reference * 34
        passed = observed_times_10 <= reference_times_34
        counters[key] = dict(reference4kMedian=reference, observed10k=value,
            observedTimes10=observed_times_10, referenceTimes34=reference_times_34,
            maximumObserved10k=reference_times_34 // 10,
            status="PASS" if passed else "FAIL")
        if not passed:
            over_limit.append(key)
    return counters, over_limit

def full10k_instance_batch_requests(extra):
    require(isinstance(extra, dict)
            and extra["schema"] == "V9_DEV_FULL_EXTRA_RESOURCE_V1"
            and extra["status"] == "MEASURED"
            and extra["sampleScope"] == "FULL_WITH_OBSERVER_OVERHEAD"
            and extra["sqlBatchScope"] == "DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS"
            and extra["exactRunnerSqlStatementCount"] is False
            and extra["rawSqlExported"] is False
            and type(extra["sampleCount"]) is int and extra["sampleCount"] >= 2
            and extra["probeError"] is None
            and type(extra["instanceBatchRequests"]) is int
            and extra["instanceBatchRequests"] >= 0)
    return extra["instanceBatchRequests"]

def full10k_diagnostics(receipt, diag, extra):
    # Clock and CPU availability are report-only. Malformed diagnostics never
    # approve, reject or reopen the deterministic counter confirmation.
    full_timing = dict(status="UNAVAILABLE")
    try:
        values = {key: receipt["physicalFull"][key] for key in TIMINGS}
        if all(number(value) for value in values.values()) and values["fullElapsedMs"] > 0:
            full_timing = dict(status="MEASURED",values=values)
    except (KeyError, TypeError, OverflowError):
        pass
    phase_timing = dict(status="ABSENT" if diag is None else "UNAVAILABLE")
    if diag is not None:
        try:
            phase_timing = dict(status="MEASURED",values=phase_ms(diag))
        except (KeyError, TypeError, ValueError, OverflowError):
            pass
    cpu = dict(status="UNAVAILABLE")
    if isinstance(extra,dict):
        runner_cpu = extra.get("runnerCpuMs")
        host_cpu = extra.get("hostCpuBusyFraction")
        try:
            if number(runner_cpu) and number(host_cpu) and host_cpu <= 1:
                cpu = dict(status="MEASURED",runnerCpuMs=runner_cpu,hostCpuBusyFraction=host_cpu)
        except OverflowError:
            pass
    try:
        sql = runner_sql_metrics(receipt)
    except (KeyError, TypeError, ValueError):
        sql = dict(status="UNAVAILABLE")
    return dict(fullTimingMs=full_timing,exclusiveDiagnosticPhaseMs=phase_timing,
                runnerAndHostCpu=cpu,runnerSql=sql)

def assess_full10k_confirmation(receipt, member, diag, extra, sha, expected_profile):
    """Apply the PO gate to required FULL 10k evidence, using integer arithmetic only."""
    require(re.fullmatch(r"[0-9a-f]{40}", sha)
            and isinstance(expected_profile,str)
            and re.fullmatch(r"[a-z][a-z0-9-]{0,39}",expected_profile)
            and expected_profile.endswith("10k")
            and receipt["schema"] == "V9_DEV_FULL_SCALE_DIAGNOSTIC_V1"
            and receipt["commit"] == sha
            and receipt["profile"] == expected_profile
            and type(receipt["population"]) is int
            and receipt["population"] == FULL10K_POPULATION
            and receipt["winnerSha256"] == WINNER
            and receipt["fullCompleted"] is True
            and receipt["independentMembershipVerified"] is True
            and receipt["identityCensusStatus"] == "PASS"
            and receipt["matrixExecuted"] is False
            and receipt["scientificTest"] is False
            and receipt["activation"] is False)
    require(member["schema"] == "V9_E2E_1K_MEMBERSHIP_ORACLE_V1"
            and member["status"] == "PASS"
            and member["scope"] == "ACTUAL_B1_MEMBERSHIP_RANK_R1_LEDGER_AND_CONSUMED_SILVER_EDGE_SCORE_GROUP"
            and type(member["observations"]) is int
            and member["observations"] > 0
            and type(receipt["eligible"]) is int
            and member["observations"] == receipt["eligible"]
            and all(member[k] is True for k in (
                "actualConsumedSilverEdgesVerified", "actualConsumedScoresVerified",
                "actualConsumedGroupsVerified", "actualGoldRecordScoresVerified",
                "independentR1DecisionVerified", "silverSqlReplayEqual",
                "silverDirectionalReplayVerified", "independentScoreOracleVerified",
                "frozenGroupReplayEqual")))
    observed = {key: member[key] for key in FULL10K_REFERENCES if key != "instanceBatchRequests"}
    observed["instanceBatchRequests"] = full10k_instance_batch_requests(extra)
    require(type(member["secureRecords"]) is int and member["secureRecords"] >= 0
            and observed["silverDirectionsCompared"] == 2 * observed["silverEdges"]
            and observed["goldRecordScores"] == member["secureRecords"])
    counters, over_limit = deterministic_counter_gate(observed)

    return dict(schema="V9_FULL10K_DETERMINISTIC_COUNTER_CONFIRMATION_V1",
                commit=sha,profile=expected_profile,population=FULL10K_POPULATION,
                winnerSha256=WINNER,
                decisionId="GOV-V9-CURVA-PASS-QUALIFICADO-20261126",
                reference4kRuns=list(FULL10K_REFERENCE_RUNS),
                status="FAIL_REOPEN_CURVE" if over_limit else "PASS",
                curveReopened=bool(over_limit),
                comparison="observed*10 <= reference*34",
                counters=counters,overLimit=over_limit,
                timingAndCpuRole="DIAGNOSTIC_ONLY",
                diagnostics=full10k_diagnostics(receipt,diag,extra))

def load_full10k_evidence(folder):
    require(isinstance(folder, Path) and folder.is_dir() and not folder.is_symlink())
    def read(name):
        path = folder / name
        require(path.is_file() and not path.is_symlink()
                and 0 < path.stat().st_size <= 32 * 1024 * 1024)
        return json.loads(path.read_text(encoding="utf-8"))
    receipt = read("diagnostic-receipt.json")
    member = read("membership-receipt.json")
    extra = read("v9-full-extra-resource.json")
    diag_path = folder / "v9-full-diagnostic.json"
    diag = None
    try:
        # This receipt carries clock diagnostics only. Never read a symlink and
        # never let absence, shape, size or parse errors alter the counter gate.
        if diag_path.is_symlink():
            diag = {}
        elif diag_path.exists():
            if not diag_path.is_file() or not 0 < diag_path.stat().st_size <= 32 * 1024 * 1024:
                diag = {}
            else:
                diag = json.loads(diag_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ValueError, RecursionError):
        diag = {}
    return receipt,member,diag,extra

def assess(frames, sha, extras=None, v8_pairs=None, parity_pass=False):
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
    v8=operational_v8_evidence(v8_pairs,sha)
    require(type(parity_pass) is bool)
    pre_full_missing = [] if parity_pass else ["strictParity1k"]
    if v8 is None:
        pre_full_missing += ["independentOperationalV8Regression"]
    if any(row["runnerSql"]["status"] != "MEASURED" for row in rows):
        pre_full_missing += ["exactSqlOperations", "sqlCpuAndExactRunnerAttribution"]
    if any(row["extraFullResources"]["status"] != "MEASURED" for row in rows):
        pre_full_missing += ["fullTempdbUsage", "physicalIndexBytes", "fullCpuAndDiskIo"]
    completion_blockers = ["full10kDeterministicCounterConfirmation", "phaseAAnalysis1931"]
    return dict(schema="V9_MIGRATED_CURVE_DIAGNOSTIC_V1",commit=sha,rows=rows,
                fullTimingRatios2kTo4k=t,exclusivePhaseRatios2kTo4k=p,
                overLimit=failures,unmeasurable=unmeasurable,
                timingGate="FAIL" if failures else "INCOMPLETE" if unmeasurable else "PASS",
                timingAndCpuRole="DIAGNOSTIC_ONLY",
                qualifiedCurveDecision="PASS_QUALIFIED",
                qualifiedCurveDecisionId="GOV-V9-CURVA-PASS-QUALIFICADO-20261126",
                strictParity1k="PASS" if parity_pass else "ABSENT",
                operationalV8Regression=v8,
                preFullMissingEvidence=pre_full_missing,
                phaseAPreFullGatesComplete=not pre_full_missing,
                phaseACompletionBlockers=completion_blockers,
                missingEvidence=pre_full_missing+completion_blockers,
                phaseAComplete=False)

def main():
    # Neither mode recomputes or admits a corpus.
    a=argparse.ArgumentParser()
    a.add_argument("--run",type=int)
    a.add_argument("--sha",required=True)
    a.add_argument("--current-run",action="store_true")
    a.add_argument("--confirm-full10k",action="store_true",
                   help="require and gate extracted FULL10k evidence; never admits or runs FULL 10k")
    a.add_argument("--full10k-evidence-dir",type=Path)
    a.add_argument("--full10k-profile")
    args=a.parse_args()
    if args.confirm_full10k:
        require(args.run is None and not args.current_run and args.full10k_evidence_dir is not None
                and args.full10k_profile is not None)
        receipt,member,diag,extra=load_full10k_evidence(args.full10k_evidence_dir)
        result=assess_full10k_confirmation(receipt,member,diag,extra,args.sha,args.full10k_profile)
        print(json.dumps(result,sort_keys=True,allow_nan=False),flush=True)
        # The explicit, currently unwired post-FULL mode is never allowed to
        # treat missing, malformed or over-limit evidence as confirmation.
        require(result["status"]=="PASS")
        return
    require(args.run is not None and args.full10k_evidence_dir is None and args.full10k_profile is None)
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
    jobs=module.api_json(f"{root}/runs/{args.run}/jobs?filter=latest&per_page=100",token)
    require(jobs["total_count"]==len(jobs["jobs"]))
    parity=[job for job in jobs["jobs"] if job["name"]=="parity-1k"]
    require(len(parity)==1 and parity[0]["run_id"]==args.run
            and parity[0]["head_sha"]==args.sha and parity[0]["status"]=="completed"
            and parity[0]["conclusion"]=="success")
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
        v8_pairs=[]
        for label in ("diagnostic1k","optimized1k"):
            pair,=get("v9-phase4-scale",label,("pair-receipt.json",))
            v8_pairs.append(pair)
        result=assess(frames,args.sha,extras,v8_pairs,parity_pass=True)
        result["runConclusion"]=run["conclusion"]
        # Timing evidence remains useful even if another gate fails. Never
        # infer strict parity from timings or from a run-level conclusion.
        print(json.dumps(result,sort_keys=True,allow_nan=False))

if __name__=="__main__":
    try:main()
    except Exception:raise SystemExit("V9_SCALE_CURVE_EVIDENCE_REJECTED") from None

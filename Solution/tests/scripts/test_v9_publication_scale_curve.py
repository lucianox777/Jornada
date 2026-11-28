import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT=Path(__file__).resolve().parents[2]/"scripts/v9-publication-scale-curve.py"
SPEC=importlib.util.spec_from_file_location("curve",SCRIPT)
C=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(C)
FULL10K_TEST_PROFILE="test-full10k"

class CurveTests(unittest.TestCase):
    def frame(self,label,n,m):
        sha="a"*40
        full={k:10*m for k in C.TIMINGS}
        full.update(sampledPeakVersionStoreBytes=10,sampledPeakWorkingSetBytes=20,processPeakWorkingSetBytes=30)
        r=dict(schema="V9_DEV_FULL_SCALE_DIAGNOSTIC_V1",commit=sha,profile=label,
               population=n,winnerSha256=C.WINNER,fullCompleted=True,
               independentMembershipVerified=True,identityCensusStatus="PASS",matrixExecuted=False,
               scientificTest=False,activation=False,eligible=n,versions=2*n,physicalFull=full)
        member=dict(schema="V9_E2E_1K_MEMBERSHIP_ORACLE_V1",status="PASS",
                    scope="ACTUAL_B1_MEMBERSHIP_RANK_R1_LEDGER_AND_CONSUMED_SILVER_EDGE_SCORE_GROUP",
                    observations=n,
                    candidatePeople=n,secureRecords=n,silverEdges=n,
                    silverDirectionsCompared=2*n,goldRecordScores=n)
        for k in ("actualConsumedSilverEdgesVerified","actualConsumedScoresVerified",
                  "actualConsumedGroupsVerified","actualGoldRecordScoresVerified","independentR1DecisionVerified",
                  "silverSqlReplayEqual","silverDirectionalReplayVerified",
                  "independentScoreOracleVerified","frozenGroupReplayEqual"):
            member[k]=True
        diag=dict(schema="V9_DEV_FULL_DIAGNOSTIC_V1",status="FULL_COMPLETED",rawPlanExported=False,
                  elapsedMs=10*m,phases=[dict(phase="full-read",elapsedMs=2*m),
                  dict(phase="full-publish",elapsedMs=7*m)],finalPhase="full-publish")
        resource=dict(status="COMPLETE",exitCode=0,peakHostMemoryUsed=30,minMemoryAvailable=10,minDiskFree=10)
        return r,member,diag,resource
    def frames(self,last=4):
        return [self.frame(l,n,m) for l,n,m in zip(C.LABELS,C.POPULATIONS,(1,2,last))]
    def v8_pairs(self):
        def pair(primary,restored,digest):
            return dict(schema="V9_RESTORED_PREFULL_PAIR_V3",commit="a"*40,
                primaryProfile=primary,restoredProfile=restored,inputVerified=True,
                distinctPublishedRuns=True,rawEdgesAndScoresEqual=True,observations=10,references=10,
                operationalV8RegressionVerified=True,operationalV8MutationDetected=True,
                operationalV8SemanticSha256=digest,operationalV8DecisionRows=4,
                operationalV8Scenarios=["BELOW_THRESHOLD","CONFLICT","NO_CANDIDATE","RESOLVED"],
                operationalV8GoldRows=1,
                operationalV8ModelId="88000000-0000-4000-8000-000000008008",
                operationalV8RuleSetFingerprintSha256=
                    "017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd",
                operationalV8Stages=(["BASELINE","20261126_V3"]
                    if primary=="diagnostic1k" else ["20261126_V3","BASELINE"]))
        return [pair("diagnostic1k","optimized1k","b"*64),
                pair("optimized1k","diagnostic1k","c"*64)]
    def measured_extra(self):
        extra=dict(schema="V9_DEV_FULL_EXTRA_RESOURCE_V1",status="MEASURED",sampleCount=2,
            probeError=None,sampleScope="FULL_WITH_OBSERVER_OVERHEAD",
            sqlBatchScope="DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS",
            exactRunnerSqlStatementCount=False,rawSqlExported=False,
            runnerCpuMs=15,hostCpuBusyFraction=.5)
        extra.update({k:10 for k in C.EXTRA_COUNTS})
        return extra
    def full10k_evidence(self):
        receipt,member,diag,_=self.frame(FULL10K_TEST_PROFILE,C.FULL10K_POPULATION,1)
        maxima={key:reference*34//10 for key,reference in C.FULL10K_REFERENCES.items()}
        member.update(candidatePeople=maxima["candidatePeople"],silverEdges=maxima["silverEdges"],
            silverDirectionsCompared=2*maxima["silverEdges"],
            goldRecordScores=maxima["goldRecordScores"],secureRecords=maxima["goldRecordScores"])
        extra=self.measured_extra()
        extra["instanceBatchRequests"]=maxima["instanceBatchRequests"]
        return receipt,member,diag,extra
    def test_ratio_and_missing_metrics_never_certify_phase_a(self):
        result=C.assess(self.frames(),"a"*40)
        self.assertEqual(result["timingGate"],"PASS")
        self.assertEqual(result["exclusivePhaseRatios2kTo4k"]["full-read"],2)
        self.assertFalse(result["phaseAPreFullGatesComplete"])
        self.assertFalse(result["phaseAComplete"])
        self.assertTrue(result["missingEvidence"])
        result=C.assess(self.frames(6),"a"*40)
        self.assertEqual(result["timingGate"],"FAIL")
        self.assertIn("timing:fullElapsedMs",result["overLimit"])

    def test_qualified_curve_uses_strict_v8_gate_and_keeps_time_diagnostic(self):
        probe=dict(schema="V9_DEV_FULL_SQL_OPERATIONS_V2",scope="TAGGED_RUNNER_AND_SCORER_SQL",
            droppedEvents=0,rawEventsExported=False,completedRpc=5,completedBatches=1,
            completedSqlStatements=6,completedProcedureStatements=7,
            topLevelSqlCpuMicroseconds=8,eventFiles=1,serverCompletedRpc=5,serverCompletedBatches=1)
        frames=self.frames(6)
        for receipt,*_ in frames:receipt["runnerSql"]=probe
        extra=dict(schema="V9_DEV_FULL_EXTRA_RESOURCE_V1",status="MEASURED",sampleCount=2,
            probeError=None,sampleScope="FULL_WITH_OBSERVER_OVERHEAD",
            sqlBatchScope="DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS",
            exactRunnerSqlStatementCount=False,rawSqlExported=False,
            runnerCpuMs=15,hostCpuBusyFraction=.5)
        extra.update({k:10 for k in C.EXTRA_COUNTS})
        result=C.assess(frames,"a"*40,[extra,extra,extra],self.v8_pairs(),parity_pass=True)
        self.assertEqual(result["timingGate"],"FAIL")
        self.assertEqual(result["timingAndCpuRole"],"DIAGNOSTIC_ONLY")
        self.assertEqual(result["qualifiedCurveDecision"],"PASS_QUALIFIED")
        self.assertEqual(result["strictParity1k"],"PASS")
        self.assertEqual(result["operationalV8Regression"]["status"],"PASS")
        blockers=["full10kDeterministicCounterConfirmation","phaseAAnalysis1931"]
        self.assertEqual(result["preFullMissingEvidence"],[])
        self.assertTrue(result["phaseAPreFullGatesComplete"])
        self.assertEqual(result["phaseACompletionBlockers"],blockers)
        self.assertEqual(result["missingEvidence"],blockers)
        self.assertFalse(result["phaseAComplete"])
        without_parity=C.assess(frames,"a"*40,[extra,extra,extra],self.v8_pairs())
        self.assertEqual(without_parity["preFullMissingEvidence"],["strictParity1k"])
        self.assertEqual(without_parity["missingEvidence"],["strictParity1k",*blockers])
        self.assertFalse(without_parity["phaseAPreFullGatesComplete"])
        self.assertFalse(without_parity["phaseAComplete"])
        bad=self.v8_pairs();bad[0]["operationalV8MutationDetected"]=False
        with self.assertRaises(ValueError):
            C.assess(frames,"a"*40,[extra,extra,extra],bad,parity_pass=True)
        wrong_direction=self.v8_pairs();wrong_direction[0]["operationalV8Stages"].reverse()
        with self.assertRaises(ValueError):
            C.assess(frames,"a"*40,[extra,extra,extra],wrong_direction,parity_pass=True)

    def test_full10k_gate_uses_exact_integer_boundaries_and_keeps_time_cpu_diagnostic(self):
        receipt,member,diag,extra=self.full10k_evidence()
        receipt["physicalFull"].update({key:10**30 for key in C.TIMINGS})
        diag.update(elapsedMs=10**30,phases=[dict(phase="full-read",elapsedMs=2),
            dict(phase="full-publish",elapsedMs=10**30-1)],finalPhase="full-publish")
        extra.update(runnerCpuMs=10**30,hostCpuBusyFraction=1)
        result=C.assess_full10k_confirmation(receipt,member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        self.assertEqual(result["status"],"PASS")
        self.assertFalse(result["curveReopened"])
        self.assertEqual(result["overLimit"],[])
        self.assertEqual(result["comparison"],"observed*10 <= reference*34")
        self.assertEqual(result["reference4kRuns"],[45910892664,45913982837,45916702378])
        self.assertEqual(result["timingAndCpuRole"],"DIAGNOSTIC_ONLY")
        self.assertEqual(result["diagnostics"]["runnerAndHostCpu"]["runnerCpuMs"],10**30)
        expected_maxima=dict(candidatePeople=3515,silverEdges=28981,
            silverDirectionsCompared=57963,goldRecordScores=6188,
            instanceBatchRequests=288799)
        self.assertEqual({key:value["maximumObserved10k"]
            for key,value in result["counters"].items()},expected_maxima)
        for key,row in result["counters"].items():
            self.assertEqual(row["status"],"PASS")
            self.assertLessEqual(row["observedTimes10"],row["referenceTimes34"])

    def test_full10k_clock_and_cpu_availability_never_changes_counter_pass(self):
        receipt,member,diag,extra=self.full10k_evidence()
        huge=10**1000
        receipt["physicalFull"].update({key:huge for key in C.TIMINGS})
        diag.update(elapsedMs=huge,phases=[dict(phase="full-read",elapsedMs=huge)],
            finalPhase="full-read")
        extra.update(runnerCpuMs=huge,hostCpuBusyFraction=huge)
        result=C.assess_full10k_confirmation(
            receipt,member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        self.assertEqual(result["status"],"PASS")
        self.assertEqual(result["diagnostics"]["fullTimingMs"]["status"],"UNAVAILABLE")
        self.assertEqual(result["diagnostics"]["exclusiveDiagnosticPhaseMs"]["status"],"UNAVAILABLE")
        self.assertEqual(result["diagnostics"]["runnerAndHostCpu"]["status"],"UNAVAILABLE")
        receipt["runnerSql"]=[]
        result=C.assess_full10k_confirmation(
            receipt,member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        self.assertEqual(result["status"],"PASS")
        self.assertEqual(result["diagnostics"]["runnerSql"]["status"],"UNAVAILABLE")

    def test_full10k_gate_reopens_for_each_single_counter_over_exact_limit(self):
        for key,reference in C.FULL10K_REFERENCES.items():
            with self.subTest(counter=key):
                observed={name:value*34//10 for name,value in C.FULL10K_REFERENCES.items()}
                observed[key]=reference*34//10+1
                counters,over_limit=C.deterministic_counter_gate(observed)
                self.assertEqual(over_limit,[key])
                self.assertEqual(counters[key]["status"],"FAIL")
                self.assertEqual(counters[key]["observedTimes10"],observed[key]*10)
                self.assertEqual(counters[key]["referenceTimes34"],reference*34)
        receipt,member,diag,extra=self.full10k_evidence()
        member["candidatePeople"]=3516
        result=C.assess_full10k_confirmation(receipt,member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        self.assertEqual(result["status"],"FAIL_REOPEN_CURVE")
        self.assertTrue(result["curveReopened"])
        self.assertEqual(result["overLimit"],["candidatePeople"])

    def test_full10k_gate_fails_closed_on_provenance_or_counter_type(self):
        receipt,member,diag,extra=self.full10k_evidence()
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(dict(receipt,profile="wrong"),member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(dict(receipt,commit="b"*40),member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(dict(receipt,population=10000.0),member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,dict(member,observations=True),diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(dict(receipt,eligible=True),member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(dict(receipt,winnerSha256="b"*64),member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,dict(member,status="INCOMPLETE"),diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,dict(member,actualConsumedScoresVerified=False),diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        bad_member=dict(member,candidatePeople=float(member["candidatePeople"]))
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,bad_member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        bad_member=dict(member,silverDirectionsCompared=member["silverDirectionsCompared"]+1)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,bad_member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        bad_member=dict(member,secureRecords=member["secureRecords"]+1)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,bad_member,diag,extra,"a"*40,FULL10K_TEST_PROFILE)
        bad_extra=dict(extra,status="INCOMPLETE",sampleCount=0,probeError="FULL_RESOURCE_BATCH_REQUESTS_NULL")
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,member,diag,bad_extra,"a"*40,FULL10K_TEST_PROFILE)
        with self.assertRaises(ValueError):
            C.assess_full10k_confirmation(receipt,member,diag,
                dict(extra,sqlBatchScope="TAGGED_RUNNER_AND_SCORER_SQL"),"a"*40,FULL10K_TEST_PROFILE)
        for value in (True,1.0,"1",-1):
            observed=dict(C.FULL10K_REFERENCES,candidatePeople=value)
            with self.assertRaises(ValueError):
                C.deterministic_counter_gate(observed)
        missing=dict(C.FULL10K_REFERENCES);del missing["candidatePeople"]
        with self.assertRaises(ValueError):
            C.deterministic_counter_gate(missing)

    def test_full10k_cli_requires_files_and_exits_nonzero_for_over_limit(self):
        receipt,member,diag,extra=self.full10k_evidence()
        command=[sys.executable,str(SCRIPT),"--confirm-full10k","--sha","a"*40,
            "--full10k-profile",FULL10K_TEST_PROFILE]
        without_dir=subprocess.run(command,capture_output=True,text=True,check=False)
        self.assertNotEqual(without_dir.returncode,0)
        with tempfile.TemporaryDirectory() as folder_name:
            folder=Path(folder_name)
            files={"diagnostic-receipt.json":receipt,"membership-receipt.json":member,
                "v9-full-diagnostic.json":diag,"v9-full-extra-resource.json":extra}
            for name,value in files.items():
                (folder/name).write_text(json.dumps(value),encoding="utf-8")
            actual_command=[*command,"--full10k-evidence-dir",folder_name]
            passed=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertEqual(passed.returncode,0,passed.stderr)
            self.assertEqual(json.loads(passed.stdout)["status"],"PASS")
            (folder/"v9-full-diagnostic.json").write_text("not-json",encoding="utf-8")
            invalid_diagnostic=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertEqual(invalid_diagnostic.returncode,0,invalid_diagnostic.stderr)
            self.assertEqual(json.loads(invalid_diagnostic.stdout)["diagnostics"]
                ["exclusiveDiagnosticPhaseMs"]["status"],"UNAVAILABLE")
            (folder/"v9-full-diagnostic.json").write_text("",encoding="utf-8")
            empty_diagnostic=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertEqual(empty_diagnostic.returncode,0,empty_diagnostic.stderr)
            self.assertEqual(json.loads(empty_diagnostic.stdout)["diagnostics"]
                ["exclusiveDiagnosticPhaseMs"]["status"],"UNAVAILABLE")
            (folder/"v9-full-diagnostic.json").write_text(
                '{"elapsedMs":'+"1"*5000+'}',encoding="utf-8")
            extreme_diagnostic=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertEqual(extreme_diagnostic.returncode,0,extreme_diagnostic.stderr)
            self.assertEqual(json.loads(extreme_diagnostic.stdout)["diagnostics"]
                ["exclusiveDiagnosticPhaseMs"]["status"],"UNAVAILABLE")
            (folder/"v9-full-diagnostic.json").write_text(
                "["*100000+"0"+"]"*100000,encoding="utf-8")
            nested_diagnostic=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertEqual(nested_diagnostic.returncode,0,nested_diagnostic.stderr)
            self.assertEqual(json.loads(nested_diagnostic.stdout)["diagnostics"]
                ["exclusiveDiagnosticPhaseMs"]["status"],"UNAVAILABLE")
            (folder/"v9-full-extra-resource.json").unlink()
            missing=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertNotEqual(missing.returncode,0)
            (folder/"v9-full-extra-resource.json").write_text(json.dumps(extra),encoding="utf-8")
            member["candidatePeople"]=3516
            (folder/"membership-receipt.json").write_text(json.dumps(member),encoding="utf-8")
            failed=subprocess.run(actual_command,capture_output=True,text=True,check=False)
            self.assertNotEqual(failed.returncode,0)
            self.assertEqual(json.loads(failed.stdout)["status"],"FAIL_REOPEN_CURVE")
    def test_zero_denominator_and_wrong_provenance(self):
        frames=self.frames()
        frames[1][0]["physicalFull"]["physicalSnapshotMs"]=0
        result=C.assess(frames,"a"*40)
        self.assertEqual(result["timingGate"],"INCOMPLETE")
        frames[2][0]["commit"]="b"*40
        with self.assertRaises(ValueError):C.assess(frames,"a"*40)
    def test_phase_monotonicity(self):
        f=self.frames()
        f[1][2]["phases"][1]["elapsedMs"]=1
        with self.assertRaises(ValueError):C.assess(f,"a"*40)

    def test_split_v9_blocking_maintenance_is_an_independent_gate(self):
        f=self.frames()
        for index, elapsed in enumerate((40,40,100)):
            scale=1 if index<2 else 2
            f[index][2].update(elapsedMs=elapsed, finalPhase="publish-commit", phases=[
                dict(phase="publish-blocking", elapsedMs=10*scale),
                dict(phase="publish-blocking-v9-maintenance", elapsedMs=20*scale),
                dict(phase="publish-commit", elapsedMs=30 if index<2 else 80)])
        result=C.assess(f,"a"*40)
        self.assertEqual(result["timingGate"],"FAIL")
        self.assertEqual(result["exclusivePhaseRatios2kTo4k"]["publish-blocking"],2)
        self.assertEqual(result["exclusivePhaseRatios2kTo4k"]["publish-blocking-v9-maintenance"],4)
        self.assertIn("phase:publish-blocking-v9-maintenance",result["overLimit"])

    def test_extended_resource_scope_and_invalid_cpu(self):
        x=dict(schema="V9_DEV_FULL_EXTRA_RESOURCE_V1",status="MEASURED",sampleCount=2,
               probeError=None,sampleScope="FULL_WITH_OBSERVER_OVERHEAD",
               sqlBatchScope="DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS",
               exactRunnerSqlStatementCount=False,rawSqlExported=False,
               runnerCpuMs=15,hostCpuBusyFraction=.5)
        x.update({k:10 for k in C.EXTRA_COUNTS})
        result=C.assess(self.frames(),"a"*40,[x,x,x])
        self.assertNotIn("fullTempdbUsage",result["missingEvidence"])
        self.assertIn("exactSqlOperations",result["missingEvidence"])
        self.assertFalse(result["phaseAComplete"])
        self.assertIn("fullTempdbUsage",C.assess(self.frames(),"a"*40,[x,None,x])["missingEvidence"])
        with self.assertRaises(ValueError):C.extra_metrics(dict(x,hostCpuBusyFraction=1.1))
        with self.assertRaises(ValueError):C.extra_metrics(dict(x,exactRunnerSqlStatementCount=True))

    def test_incomplete_probe_retains_only_fixed_failure_codes(self):
        x=dict(schema="V9_DEV_FULL_EXTRA_RESOURCE_V1",status="INCOMPLETE",sampleCount=0,
               probeError="FULL_RESOURCE_BATCH_REQUESTS_NULL",sampleScope="FULL_WITH_OBSERVER_OVERHEAD",
               sqlBatchScope="DISPOSABLE_INSTANCE_INCLUDING_OBSERVERS",
               exactRunnerSqlStatementCount=False,rawSqlExported=False)
        self.assertEqual(C.extra_metrics(x)["probeError"],"FULL_RESOURCE_BATCH_REQUESTS_NULL")
        self.assertEqual(C.extra_metrics(dict(x,probeError="arbitrary private text"))["probeError"],"UNCLASSIFIED")
        self.assertEqual(C.extra_metrics(dict(x,probeError="SQL_207_1"))["probeError"],"SQL_207_1")
        self.assertEqual(C.extra_metrics(dict(x,probeError=None))["probeError"],"INCOMPLETE_WITHOUT_CODE")

    def test_sql_probe_counts_require_lossless_attribution_and_known_scope(self):
        probe = dict(schema="V9_DEV_FULL_SQL_OPERATIONS_V2", scope="TAGGED_RUNNER_AND_SCORER_SQL",
                     droppedEvents=0, rawEventsExported=False, completedRpc=45,
                     completedBatches=1, completedSqlStatements=50, completedProcedureStatements=60,
                     topLevelSqlCpuMicroseconds=90052, eventFiles=1,
                     serverCompletedRpc=45, serverCompletedBatches=1)
        frames = self.frames()
        for receipt, *_ in frames:
            receipt.update(runnerSql=probe, ingestElapsedMs=150, censusElapsedMs=10)
        result = C.assess(frames, "a" * 40)
        self.assertNotIn("exactSqlOperations", result["missingEvidence"])
        self.assertEqual(result["rows"][0]["runnerSql"]["topLevelSqlCpuMicroseconds"], 90052)
        self.assertEqual(result["rows"][0]["separatelyMeasuredStagesMs"]["censusElapsedMs"], 10)
        for change in (dict(droppedEvents=1), dict(eventFiles=5), dict(scope="INSTANCE")):
            with self.assertRaises(ValueError):
                C.runner_sql_metrics({"runnerSql": dict(probe, **change)})

    def test_four_event_files_require_matching_server_and_archive_counts(self):
        probe = dict(schema="V9_DEV_FULL_SQL_OPERATIONS_V2", scope="TAGGED_RUNNER_AND_SCORER_SQL",
                     droppedEvents=0, rawEventsExported=False, completedRpc=45,
                     completedBatches=1, completedSqlStatements=50, completedProcedureStatements=60,
                     topLevelSqlCpuMicroseconds=90052, eventFiles=4,
                     serverCompletedRpc=45, serverCompletedBatches=1)
        actual = C.runner_sql_metrics({"runnerSql": probe})
        self.assertEqual(actual["status"], "MEASURED")
        self.assertEqual(actual["eventFiles"], 4)
        for change in (dict(serverCompletedRpc=46), dict(serverCompletedBatches=2), dict(eventFiles=5)):
            with self.assertRaises(ValueError):
                C.runner_sql_metrics({"runnerSql": dict(probe, **change)})

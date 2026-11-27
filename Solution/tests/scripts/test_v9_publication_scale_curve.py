import importlib.util
from pathlib import Path
import unittest

SPEC=importlib.util.spec_from_file_location("curve",Path(__file__).resolve().parents[2]/"scripts/v9-publication-scale-curve.py")
C=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(C)

class CurveTests(unittest.TestCase):
    def frame(self,label,n,m):
        sha="a"*40
        full={k:10*m for k in C.TIMINGS}
        full.update(sampledPeakVersionStoreBytes=10,sampledPeakWorkingSetBytes=20,processPeakWorkingSetBytes=30)
        r=dict(commit=sha,profile=label,population=n,winnerSha256=C.WINNER,fullCompleted=True,
               independentMembershipVerified=True,identityCensusStatus="PASS",matrixExecuted=False,
               scientificTest=False,activation=False,eligible=n,versions=2*n,physicalFull=full)
        member=dict(status="PASS",observations=n,candidatePeople=n,secureRecords=n,silverEdges=n,
                    silverDirectionsCompared=2*n,goldRecordScores=n)
        for k in ("actualConsumedSilverEdgesVerified","actualConsumedScoresVerified",
                  "actualConsumedGroupsVerified","actualGoldRecordScoresVerified","independentR1DecisionVerified"):
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
            return dict(schema="V9_RESTORED_PREFULL_PAIR_V2",commit="a"*40,
                primaryProfile=primary,restoredProfile=restored,inputVerified=True,
                distinctPublishedRuns=True,rawEdgesAndScoresEqual=True,observations=10,references=10,
                operationalV8RegressionVerified=True,operationalV8MutationDetected=True,
                operationalV8SemanticSha256=digest,operationalV8DecisionRows=1,
                operationalV8GoldRows=1,
                operationalV8ModelId="88000000-0000-4000-8000-000000008008",
                operationalV8RuleSetFingerprintSha256=
                    "900a6ab052f078f8422d76a297a5a9fbf6fe0003f764edc86e5d01e802991504",
                operationalV8Stages=(["BASELINE","20261126_V3"]
                    if primary=="diagnostic1k" else ["20261126_V3","BASELINE"]))
        return [pair("diagnostic1k","optimized1k","b"*64),
                pair("optimized1k","diagnostic1k","c"*64)]
    def test_ratio_and_missing_metrics_never_certify_phase_a(self):
        result=C.assess(self.frames(),"a"*40)
        self.assertEqual(result["timingGate"],"PASS")
        self.assertEqual(result["exclusivePhaseRatios2kTo4k"]["full-read"],2)
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
        self.assertEqual(result["missingEvidence"],[])
        self.assertTrue(result["phaseAComplete"])
        without_parity=C.assess(frames,"a"*40,[extra,extra,extra],self.v8_pairs())
        self.assertEqual(without_parity["missingEvidence"],["strictParity1k"])
        self.assertFalse(without_parity["phaseAComplete"])
        bad=self.v8_pairs();bad[0]["operationalV8MutationDetected"]=False
        with self.assertRaises(ValueError):
            C.assess(frames,"a"*40,[extra,extra,extra],bad,parity_pass=True)
        wrong_direction=self.v8_pairs();wrong_direction[0]["operationalV8Stages"].reverse()
        with self.assertRaises(ValueError):
            C.assess(frames,"a"*40,[extra,extra,extra],wrong_direction,parity_pass=True)
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

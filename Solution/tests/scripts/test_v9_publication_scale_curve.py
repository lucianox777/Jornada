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
    def test_ratio_and_missing_metrics_never_certify_phase_a(self):
        result=C.assess(self.frames(),"a"*40)
        self.assertEqual(result["timingGate"],"PASS")
        self.assertEqual(result["exclusivePhaseRatios2kTo4k"]["full-read"],2)
        self.assertFalse(result["phaseAComplete"])
        self.assertTrue(result["missingEvidence"])
        result=C.assess(self.frames(6),"a"*40)
        self.assertEqual(result["timingGate"],"FAIL")
        self.assertIn("timing:fullElapsedMs",result["overLimit"])
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

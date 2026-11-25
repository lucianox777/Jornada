import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location("gate",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-1k-gate.py")
G=importlib.util.module_from_spec(spec);spec.loader.exec_module(G)

SHA="a"*40
RUN="6665150c-e1f1-4dae-8229-507cc6600a85"
ATTEMPT="ad8f4b7a-b521-4cba-b58a-3025d48bc912"

def fixture():
    base=dict(tp=563,fp=0,fn=1842,truePairs=2405,fnRecovered=0,fpIntroduced=0,
              falseFusedIdentitiesIntroduced=0,newIdentities=0,fnByScope=G.BASE_SCOPE.copy(),
              recoveredByScope={"cross_manager":0,"same_manager_same_origin":0})
    s=dict(commit=SHA,runId=RUN,schema="V9_PHASE4_S4B_1K_V1",status="PASS",winnerSha256=G.P.WINNER,
           baselineIdentityCounts={"people":1000,"versions":2349,"origins":1957},officialPolicyUnchanged=True,
           alternativePublication=False,scientificTest=False,activated=False,factorialExecuted=False,
           variants={v:base.copy() for v in G.VARIANTS})
    n=dict(commit=SHA,runId=RUN,E2E_NUCLEO_PASS=True,E2E_INTEGRAL_PASS=False)
    i=dict(runId=RUN,winnerSha256=G.P.WINNER,status="PASS",pairMetrics=base.copy(),**G.P.COUNTS)
    g=dict(runId=RUN,actualConsumedSilverEdgesVerified=True,actualConsumedScoresVerified=True,
           actualConsumedGroupsVerified=True,actualGoldRecordScoresVerified=True)
    l=dict(attempt=ATTEMPT,status="PASS")
    return s,n,i,g,l

class GateTests(unittest.TestCase):
    def test_reject_missing_or_different_attempt(self):
        f=fixture();self.assertEqual(G.validate(*f,SHA,ATTEMPT)["fn"],1842)
        with self.assertRaises(ValueError):G.validate(*f,"b"*40,ATTEMPT)
        with self.assertRaises(ValueError):G.validate(*f,SHA,RUN)
        f[0]["alternativePublication"]=True
        with self.assertRaises(ValueError):G.validate(*f,SHA,ATTEMPT)
    def test_reject_missing_variant_or_scope_drift(self):
        f=fixture();del f[0]["variants"]["one_new_per_group"]
        with self.assertRaises(ValueError):G.validate(*f,SHA,ATTEMPT)
        f=fixture();f[0]["variants"]["one_new_per_group"]["fnRecovered"]=1
        with self.assertRaises(ValueError):G.validate(*f,SHA,ATTEMPT)
    def test_r2_only_indication_and_unique_target(self):
        def row(k,status,reason,resolved=None,established=None,protected=False,group=1):
            return dict(id=k,group=group,status=status,reason=reason,resolved=resolved,
                        established=established,protected=protected)
        ind="V9_R1_INDEFINIDO_COM_INDICIO"
        rows=[row(1,"RESOLVIDO","OK","X"),row(2,"NAO_RESOLVIDO",ind),
              row(3,"NAO_RESOLVIDO",ind,protected=True),row(4,"CONFLITO","MARGEM"),
              row(5,"NAO_RESOLVIDO","V9_R1_INDEFINIDO_SEM_EVIDENCIA_SEGURA"),
              row(6,"NAO_RESOLVIDO","V9_R1_INDEFINIDO_SEM_INDICIO",group=2)]
        self.assertEqual(G.r2_indication_hypothesis(rows),{2:"X"})
        rows.append(row(7,"RESOLVIDO","OK","Y"))
        self.assertEqual(G.r2_indication_hypothesis(rows),{})

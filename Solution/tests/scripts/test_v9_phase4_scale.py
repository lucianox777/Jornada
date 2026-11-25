import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import os
spec=importlib.util.spec_from_file_location("scale",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py")
S=importlib.util.module_from_spec(spec);spec.loader.exec_module(S)
class ScaleTests(unittest.TestCase):
    def test_progressive_diagnostic_admission(self):
        env=dict(GITHUB_ACTIONS="true",GITHUB_REPOSITORY="lucianox777/Jornada",
                 GITHUB_EVENT_NAME="pull_request",JORNADA_RUNTIME_MODE="DEV",
                 JORNADA_V9_FULL_DIAGNOSTIC="true")
        for label,n in (("diagnostic1k",1000),("diagnostic2k",2000),("diagnostic4k",4000)):
            with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=label,
                           JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                self.assertEqual(S.profile()[1][0],n)
                with self.assertRaises(ValueError):S.plan(Path("."))
        for label,n in (("comparable",10000),("challenge",10000),("diagnostic4k",2000)):
            with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=label,
                           JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                with self.assertRaises(ValueError):S.profile()
    def test_decision_component_digests_detect_exact_field_changes(self):
        key=("base-a","SMS",1)
        graph=dict(key=key,group=[key],candidates=[],neighbors=[],minimum=None,reason="X")
        rank=dict(Id=key,Best=None,BestScore=0,Second=None,SecondScore=None,Margin=None,
                  ScoreableRecords=0,PeopleWithoutScoreableEvidence=0,CandidateCount=0,HasPass=False,
                  Status="NAO_RESOLVIDO",Resolved=None,Reason="X")
        a=S.decision_field_digests([graph],[rank])
        b=S.decision_field_digests([dict(graph,reason="Y")],[dict(rank,BestScore=1)])
        self.assertEqual({k for k in a if a[k]!=b[k]},{"graph.reason","rank.BestScore"})
        self.assertEqual(len(a),17)
        key2=("base-b","SMS",1)
        g2=dict(graph,key=key2,group=[key2])
        r2=dict(rank,Id=key2)
        self.assertEqual(S.decision_field_digests([graph,g2],[rank,r2]),
                         S.decision_field_digests([g2,graph],[r2,rank]))
        with self.assertRaises(ValueError):S.decision_field_digests([dict(graph,unexpected=1)],[rank])
    def test_conditional_bound_and_cap(self):
        b=S.bound(10000,0,20000,0,1)
        self.assertAlmostEqual(b["cpUpper"],1-.025**(1/10000),places=12)
        self.assertGreater(S.bound(10000,1,20000,1,216)["conditionalFdrUpper"],b["conditionalFdrUpper"])
        self.assertIsNone(S.bound(10000,0,1,0,216)["conditionalFdrUpper"])
        self.assertFalse(b["certified"])
    def test_ordinal_cases_and_integrity(self):
        truth={1:dict(base="A",origin=1,manager="SEHAB",wave=1,cpf=False),
               2:dict(base="A",origin=2,manager="SMADS",wave=2,cpf=False),
               3:dict(base="B",origin=3,manager="SMS",wave=2,cpf=False)}
        def row(k,p):
            return dict(Id=k,Person=p,RawReason="X",RawStatus="RESOLVIDO",EffectiveReason="X",Method="FS_ASSOCIATION",
                        Score=.999,AssociationScore=.999,Fs=1,Candidates=1,SecureCandidates=1,PriorCardinality=1,
                        OnlyViaNonsecure=0,Best=p,LeaderId=k,GroupSize=1,GroupMinPosterior=None,SecureOrigin=False,CandidateIds=[p])
        _,state=S.M.assess([row(1,"x"),row(2,"y"),row(3,"y")],truth)
        q=S.quality(state,truth,216);self.assertEqual(q["fullyIntactTruePeople"],0)
        self.assertEqual(q["sourceFdr95Conditional"]["falseAcceptedSources"],2)
        self.assertEqual(q["acceptedPairs"],1)
        self.assertEqual(q["acceptedSources"],2)
        self.assertEqual(q["nonAnchorAcceptedSources"],1)
        self.assertEqual(q["nonAnchorFalseAcceptedSources"],1)
        self.assertEqual(q["nonAnchorErrorSourcePeople"],1)
        self.assertEqual(q["eligibleAnchorsRemoved"],1)
        self.assertEqual(q["anchorFallbackDestinations"],1)
        # Proven founder takes precedence over earlier ID, and CPF anchor
        # is already excluded from the original denominator.
        state["byid"][3]["Method"]="S4B_HYPOTHETICAL_FOUNDER"
        q2=S.quality(state,truth,216)
        self.assertEqual(q2["anchorFallbackDestinations"],0)
        self.assertEqual(q2["nonAnchorAcceptedSources"],1)
        truth[3]["cpf"]=True
        q3=S.quality(state,truth,216)
        self.assertEqual(q3["eligibleAnchorsRemoved"],0)
        self.assertEqual(q3["acceptedSources"],q3["nonAnchorAcceptedSources"])
        truth[3]["cpf"]=False
        state["byid"][3]["Method"]="FS_ASSOCIATION"
        self.assertEqual(q["errorSourcePeople"],2)
        self.assertEqual(q["contaminatedDestinations"],1)
        self.assertEqual(q["versionsPerPersonHistogram"],{1:1,2:1})
        self.assertEqual(q["sourceCapSensitivity"][-1]["cap"],8)
        self.assertTrue(q["sourceCapSensitivity"][0]["diagnosticOnly"])
        a=S.audit_cases(state,truth,None,{"obs":{1:1,2:2,3:3},"base":{"A":1,"B":2},"origin":{1:1,2:2,3:3}})
        self.assertEqual(len(a["splits"]),1);self.assertEqual(len(a["fusions"]),1)
        self.assertEqual(a["splits"][0]["person"],1)
        self.assertNotIn("y",a["fusions"][0]["members"][0]["candidateDestinations"])

import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location("scale",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py")
S=importlib.util.module_from_spec(spec);spec.loader.exec_module(S)
class ScaleTests(unittest.TestCase):
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
        a=S.audit_cases(state,truth,None,{"obs":{1:1,2:2,3:3},"base":{"A":1,"B":2},"origin":{1:1,2:2,3:3}})
        self.assertEqual(len(a["splits"]),1);self.assertEqual(len(a["fusions"]),1)
        self.assertEqual(a["splits"][0]["person"],1)
        self.assertNotIn("y",a["fusions"][0]["members"][0]["candidateDestinations"])

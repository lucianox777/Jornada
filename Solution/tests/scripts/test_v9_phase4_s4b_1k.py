import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location("s4b",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-s4b-1k.py")
S=importlib.util.module_from_spec(spec);spec.loader.exec_module(S)

def fixture():
    rows=[];ds=[]
    for k,m,o,v,g,neighbors in [(1,"SEHAB",1,1,10,[2]),(2,"SMADS",2,1,10,[1]),
                               (3,"SEHAB",1,2,20,[4]),(4,"SMS",4,1,20,[3])]:
        rows.append(dict(observationId=k,manager=m,originId=o,version=v,hasCpf=False,
                         currentUuid=None,currentStatus="NAO_RESOLVIDO",currentMethod=None,runResult="INDEFINIDA"))
        ds.append(dict(observationId=k,groupRoot=g,groupSize=2,groupMinPosterior=.9995,
                       silverNeighbors=neighbors,rankReason=S.SEM,candidates=[]))
    return rows,ds,{1:"A",2:"A",3:"A",4:"B"}

class S4bTests(unittest.TestCase):
    def test_group_record_and_origin_sensitivity(self):
        r,d,t=fixture();x=S.contrast(r,d,t);v=x["variants"]
        self.assertEqual(v["official_indefinite"]["fnByScope"],{"cross_manager":2,"same_manager_same_origin":1})
        self.assertEqual((v["one_new_per_group"]["fnRecovered"],v["one_new_per_group"]["fpIntroduced"],
                          v["one_new_per_group"]["falseFusedIdentitiesIntroduced"]),(1,1,1))
        self.assertEqual(v["one_new_per_record"]["fnRecovered"],0)
        self.assertEqual(v["one_new_per_record_origin_continuity"]["fnRecovered"],1)
        self.assertEqual(v["one_new_per_group_origin_continuity"]["fnRecovered"],1)
        self.assertEqual(v["one_new_per_group_origin_continuity"]["fpIntroduced"],2)
        self.assertEqual(x["eligibleBySilverSizeAndMinPosterior"]["2|0.999_TO_0.9999"]["groups"],2)
    def test_excludes_mixed_group_and_invalid_complete_link(self):
        r,d,t=fixture();d[0]["rankReason"]="SEM_CANDIDATO_BLOCKING_INSUFICIENTE"
        self.assertEqual(S.contrast(r,d,t)["excludedGroups"],{"MIXED_REASON":1})
        d[0]["silverNeighbors"]=[]
        with self.assertRaises(ValueError): S.contrast(r,d,t)
    def test_truth_not_required_for_projection(self):
        r,d,t=fixture()
        _,_,_,_,_,_,f=S.prepare(r,d)
        p,_=f("one_new_per_group")
        self.assertEqual(p[1],p[2]);self.assertNotEqual(p[1],p[3])
        self.assertEqual(S.band(.99),"0.99_TO_0.999")
        self.assertEqual(S.band(.9999),"0.9999_TO_1")

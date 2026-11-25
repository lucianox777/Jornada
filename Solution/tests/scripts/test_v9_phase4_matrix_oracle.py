import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location("matrix",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-matrix-oracle.py")
M=importlib.util.module_from_spec(spec);spec.loader.exec_module(M)
def row(k,p,method="FS_ASSOCIATION"):
    return dict(Id=k,Person=p,RawReason="X",EffectiveReason=method,Method=method,Score=.999,Fs=1,Candidates=1,PriorCardinality=1,OnlyViaNonsecure=0)
class MatrixOracleTests(unittest.TestCase):
    def test_pair_and_round_reconciliation(self):
        truth={1:dict(base="A",origin=1,manager="M",cpf=False),2:dict(base="A",origin=2,manager="N",cpf=False),3:dict(base="B",origin=3,manager="N",cpf=False)}
        a,s=M.assess([row(1,"X"),row(2,None,"INDEFINIDA")],truth)
        self.assertEqual((a["tp"],a["fp"],a["fn"]),(0,0,1))
        b,t=M.assess([row(1,"X"),row(2,"X"),row(3,"X")],truth,s)
        self.assertEqual((b["tp"],b["fp"],b["fn"],b["fpThisRound"],b["resolvedLater"]),(1,2,0,2,1))
        with self.assertRaises(ValueError):M.assess([row(1,"Y"),row(2,"Y")],truth,s)
    def test_namespace_independent_partition(self):
        self.assertEqual(M.partition(dict(pred={"a":[1,2]},actual={1:"a",2:"a",3:None})),
                         M.partition(dict(pred={"b":[2,1]},actual={1:"b",2:"b",3:None})))
        self.assertEqual(M.distribution([1,2,100])["p99"],100)

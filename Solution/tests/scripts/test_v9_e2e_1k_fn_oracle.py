import importlib.util
from pathlib import Path
import unittest
import json,hashlib

spec=importlib.util.spec_from_file_location("fn",Path(__file__).resolve().parents[2]/"scripts/v9-e2e-1k-fn-oracle.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class FnTests(unittest.TestCase):
    def test_pair_not_row_and_retry_not_publication(self):
        rows=[];truth=[]
        uid="00000000-0000-4000-8000-000000000001"
        for k,cpf in ((1,True),(2,False),(3,False)):
            opaque="SYNTH-"+format(k,"032X")
            rows.append(dict(observationId=k,version=1,manager="SEHAB",package="x.zip",opaque=opaque,originId=k,
                             hasCpf=cpf,currentStatus="RESOLVIDO" if cpf else "NAO_RESOLVIDO",
                             currentUuid=uid if cpf else None,runResult=None if cpf else "INDEFINIDA",
                             publicationReason=None if cpf else "INDEFINIDA"))
            truth.append(dict(GestorCodigo="SEHAB",PackageFileName="x.zip",OpaquePersonId=opaque,BasePersonId="one"))
        raw=b"".join(json.dumps(x).encode()+b"\n" for x in truth)
        waves=[({"truthSidecar":{"sha256":hashlib.sha256(raw).hexdigest().upper()}},raw)]
        diagnostics=[dict(observationId=k,groupSize=1,groupRoot=k,candidates=[],rankReason="SEM_CANDIDATO_NO_RULESET_BLOCKING") for k in (2,3)]
        retry=[dict(ObservationId=2,OriginId=2,ProposedUuid=uid),dict(ObservationId=3,OriginId=3,ProposedUuid=uid)]
        receipt=m.breakdown(waves,rows,diagnostics,retry,3)
        self.assertEqual(receipt["fn"],3)
        self.assertEqual(receipt["byTrueGroupSize"],{"3":3})
        self.assertEqual(receipt["singleFullContrast"]["recoveredFnPairs"],3)
        self.assertFalse(receipt["singleFullContrast"]["secondPhysicalFullExecuted"])
        with self.assertRaises(ValueError): m.breakdown(waves,rows,diagnostics,retry,2)

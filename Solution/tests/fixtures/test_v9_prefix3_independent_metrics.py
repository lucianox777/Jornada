"""Independent prefix3 metrics must fail closed on TEST and bad SQL counts."""
import importlib.util
from pathlib import Path
import unittest

script=Path(__file__).resolve().parents[2]/"scripts"/"v9_prefix3_independent_metrics.py"
spec=importlib.util.spec_from_file_location("prefix3_metrics",script)
mod=importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def sample():
    splits=[]; passes=[]
    for part in mod.PARTS:
        splits.append({"Partition":part,"EligibleM":10,"EligibleObservations":12,
                       "TwelveM":5,"TwelveU":2,"FourteenM":7,"FourteenU":5,
                       "AddedM":2,"AddedU":3,"Recall":.7,
                       "MoreMNeededFor95Percent":3,
                       "P95Degree":3,"P99Degree":4,"MaxDegree":5,
                       "P95UDegree":2,"P99UDegree":3,"MaxUDegree":4})
        for name,m,u in ((mod.PASSES[0],1,1),(mod.PASSES[1],1,2)):
            passes.append({"Partition":part,"Name":name,"M":m,"U":u,
                           "NewMVsTwelve":m,"NewUVsTwelve":u,
                           "TruthMNotRetrieved":0,"TruthMWithPredicate":m})
    return {"Seed":42,"CorpusPeople":30000,"CandidatePairHardCap":100,
            "ObservationsSha256":"fixture","OtherPrefix3PairsTrainValidation":{
            "Version":"V9_SQL_TWO_MORE_PREFIX3_FAMILIES_TRAIN_VALIDATION_V1",
            "Status":"DIAGNOSTIC_ONLY_TRAIN_VALIDATION_NO_TEST",
            "MayPromotePolicy":False,"Splits":splits,"Passes":passes}}


class TestMetrics(unittest.TestCase):
    def test_measured_counts_and_hypothetical_only(self):
        x=mod.evaluate(sample(),42)["TRAIN"]
        self.assertEqual((x["M"],x["U"]), (7,5))
        self.assertAlmostEqual(x["hypothetical_fdr"],.05/(6.3+.05))
        self.assertEqual(x["mean_degree"],2)
    def test_reject_test_partition(self):
        d=sample();d["OtherPrefix3PairsTrainValidation"]["Splits"][0]["Partition"]="TEST"
        with self.assertRaises(ValueError):mod.evaluate(d,42)
    def test_reject_test_pass(self):
        d=sample();d["OtherPrefix3PairsTrainValidation"]["Passes"][0]["Partition"]="TEST"
        with self.assertRaises(ValueError):mod.evaluate(d,42)
    def test_reject_mismatch(self):
        d=sample();d["OtherPrefix3PairsTrainValidation"]["Splits"][0]["AddedU"]=4
        with self.assertRaises(ValueError):mod.evaluate(d,42)
    def test_reject_promotable(self):
        d=sample();d["OtherPrefix3PairsTrainValidation"]["MayPromotePolicy"]=True
        with self.assertRaises(ValueError):mod.evaluate(d,42)
    def test_reject_cap(self):
        d=sample();d["CandidatePairHardCap"]=11
        with self.assertRaises(ValueError):mod.evaluate(d,42)


if __name__=="__main__":
    unittest.main()

import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("research_grid", Path(__file__).with_name("v9-exploratory-research-grid.py"))
grid = importlib.util.module_from_spec(spec)
spec.loader.exec_module(grid)

def fixture():
    return {"ResearchOnly":True,"Enabled":True,"NormalCandidatePairsSha256":"a"*64,
            "Budget":{"MaximumConfigurations":4,"MaximumMemoryMiB":1024,
                      "MaximumSeconds":300,"MaximumParallelism":2},
            "Domains":{"TfWeight":[0,0.1],"SmoothingAlpha":[0.5,1.0]}}

class GridTests(unittest.TestCase):
    def test_deterministic_non_operational(self):
        raw=json.dumps(fixture()).encode()
        a=grid.plan(raw)
        self.assertEqual(a,grid.plan(raw))
        self.assertEqual(a["ConfigurationCount"],4)
        self.assertTrue(all(not x["EligibleForSelection"] and not x["PhysicallyEvaluated"] for x in a["Proposals"]))
        self.assertEqual(len({x["OptionId"] for x in a["Proposals"]}),4)
    def test_exceeds_budget(self):
        x=fixture();x["Budget"]["MaximumConfigurations"]=3
        with self.assertRaisesRegex(ValueError,"exceeds"):grid.plan(json.dumps(x).encode())
    def test_unknown_parameter(self):
        x=fixture();x["Domains"]["Unknown"]=[1]
        with self.assertRaisesRegex(ValueError,"Unknown"):grid.plan(json.dumps(x).encode())
    def test_disabled(self):
        x=fixture();x["Enabled"]=False
        with self.assertRaisesRegex(ValueError,"explicitly enabled"):grid.plan(json.dumps(x).encode())
    def test_nonfinite(self):
        x=fixture();x["Domains"]["TfWeight"]=[float("nan")]
        with self.assertRaises(ValueError):grid.plan(json.dumps(x).encode())
    def test_duplicate_json_keys(self):
        with self.assertRaisesRegex(ValueError,"Duplicate key"):grid.plan(b'{"Enabled":true,"Enabled":false}')
    def test_missing_budget_dimension(self):
        x=fixture();del x["Budget"]["MaximumSeconds"]
        with self.assertRaisesRegex(ValueError,"mandatory"):grid.plan(json.dumps(x).encode())
    def test_missing_normal_frame_sha(self):
        x=fixture();x["NormalCandidatePairsSha256"]="missing"
        with self.assertRaisesRegex(ValueError,"SHA256"):grid.plan(json.dumps(x).encode())

if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

path = Path(__file__).with_name("v9-exploratory-statistical-report.py")
spec = importlib.util.spec_from_file_location("exploratory_stat_report", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class StatisticalReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.source, self.target, self.params = (root / name for name in ("normal.json", "extended.json", "parameters.json"))
        self.raw = b'{"schema":"V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1","evaluations":2,"rows":[{},{}],"activated":false}\n'
        self.source.write_bytes(self.raw)
        self.params.write_text(json.dumps({"ExploratoryParameters":[{"Name":"TfWeight","Values":[0,0.1,0.2]}]}))
    def test_disabled_is_byte_identical(self):
        result = module.extend(self.source, self.target)
        self.assertTrue(result["identical"])
        self.assertEqual(self.target.read_bytes(), self.raw)
    def test_enabled_adds_only_diagnostics(self):
        result = module.extend(self.source, self.target, enabled=True, parameters_path=self.params)
        extended = json.loads(self.target.read_bytes())
        original = json.loads(self.raw)
        extension = extended.pop("exploratoryAnalysis")
        self.assertEqual(extended, original)
        self.assertEqual(extension["Status"], "NO_COMPARABLE_EXISTING_MEASUREMENTS")
        self.assertEqual(extension["AdditionalMeasurements"], [])
        self.assertFalse(extension["AutoApplied"])
        self.assertFalse(result["activated"])
        self.assertEqual(self.source.read_bytes(), self.raw)
    def test_reuses_only_same_frame_and_passes(self):
        report = json.loads(self.raw)
        report["rows"] = [
            {"OptionId":"EXP_1","fitSnapshotSha256":"a","CandidatePairsSha256":"same",
             "Batch":1,"ConflictMargin":0,"blockingPasses":[{"id":1}],
             "recall":0.98,"fdrObserved":0.002},
            {"OptionId":"BASE","fitSnapshotSha256":"b","CandidatePairsSha256":"same",
             "Batch":1,"ConflictMargin":0,"blockingPasses":[{"id":1}],
             "recall":0.97,"fdrObserved":0.003},
            {"OptionId":"DIFFERENT","fitSnapshotSha256":"c","CandidatePairsSha256":"other",
             "Batch":1,"ConflictMargin":0,"blockingPasses":[{"id":1}],
             "recall":0.99,"fdrObserved":0.001}
        ]
        self.source.write_text(json.dumps(report))
        self.params.write_text(json.dumps({"ExploratoryParameters":[{"Name":"OptionId","Values":["EXP_1"]}]}))
        outcome = module.extend(self.source,self.target,enabled=True,parameters_path=self.params)
        self.assertTrue(outcome["evaluated"])
        extended = json.loads(self.target.read_text())
        comparisons = extended.pop("exploratoryAnalysis")["AdditionalMeasurements"]
        self.assertEqual(len(comparisons),1)
        self.assertEqual(comparisons[0]["BaselineOptionId"],"BASE")
        self.assertAlmostEqual(comparisons[0]["RecallDelta"],0.01)
        self.assertEqual(extended,report)
    def test_no_comparison_on_different_passes(self):
        report = json.loads(self.raw)
        report["rows"] = [
            {"OptionId":"EXP_1","fitSnapshotSha256":"a","CandidatePairsSha256":"same",
             "Batch":1,"ConflictMargin":0,"blockingPasses":[1],"recall":0.98},
            {"OptionId":"BASE","fitSnapshotSha256":"b","CandidatePairsSha256":"same",
             "Batch":1,"ConflictMargin":0,"blockingPasses":[2],"recall":0.97}]
        self.source.write_text(json.dumps(report))
        self.params.write_text(json.dumps({"ExploratoryParameters":[{"Name":"OptionId","Values":["EXP_1"]}]}))
        module.extend(self.source,self.target,enabled=True,parameters_path=self.params)
        self.assertEqual(json.loads(self.target.read_text())["exploratoryAnalysis"]["AdditionalMeasurements"],[])
    def test_physical_pair_sha_and_mu_policy_guard(self):
        report = json.loads(self.raw)
        common = {"Batch": 1, "ConflictMargin": 0.5, "blockingPasses": [1, 2, 3],
                  "candidatePriorTrainingAudit": {"PhysicalCandidatePairsSha256": "f" * 64}}
        report["rows"] = [
            dict(common, OptionId="A", fitSnapshotSha256="a", MuPolicy=None,
                 recall=0.91, fdrObserved=0.04),
            dict(common, OptionId="B", fitSnapshotSha256="b", MuPolicy=None,
                 recall=0.92, fdrObserved=0.03),
            dict(common, OptionId="C", fitSnapshotSha256="c", MuPolicy={"MinimumM": 5},
                 recall=0.99, fdrObserved=0.01)]
        self.source.write_text(json.dumps(report))
        self.params.write_text(json.dumps({"ExploratoryParameters":[{"Name":"OptionId","Values":["A"]}]}))
        module.extend(self.source, self.target, enabled=True, parameters_path=self.params)
        output = json.loads(self.target.read_text())
        comparisons = output.pop("exploratoryAnalysis")["AdditionalMeasurements"]
        self.assertEqual(output, report)
        self.assertEqual([r["BaselineOptionId"] for r in comparisons], ["B"])
        self.assertEqual(comparisons[0]["CandidatePairsSha256"], "f" * 64)
        self.assertTrue(comparisons[0]["ComparisonIsDescriptiveOnly"])
        self.assertFalse(comparisons[0]["MultiplicityAdjusted"])
        self.assertFalse(comparisons[0]["IndependentIdentityUncertaintyEstimated"])
    def test_enabled_requires_declaration(self):
        with self.assertRaisesRegex(ValueError,"requires parameter"):
            module.extend(self.source,self.target,enabled=True)
    def test_disabled_rejects_parameters(self):
        with self.assertRaisesRegex(ValueError,"require --enabled"):
            module.extend(self.source,self.target,parameters_path=self.params)
    def test_refuses_overwrite(self):
        self.target.write_text("protected")
        with self.assertRaises(FileExistsError):
            module.extend(self.source,self.target)
        self.assertEqual(self.target.read_text(),"protected")
    def test_unknown_schema_fails(self):
        self.source.write_text('{"schema":"unknown"}')
        with self.assertRaisesRegex(ValueError,"Unexpected"):
            module.extend(self.source,self.target,enabled=True,parameters_path=self.params)
    def test_duplicate_names_fail(self):
        self.params.write_text('{"ExploratoryParameters":[{"Name":"A","Values":[1]},{"Name":"A","Values":[2]}]}')
        with self.assertRaisesRegex(ValueError,"Duplicate exploratory"):
            module.extend(self.source,self.target,enabled=True,parameters_path=self.params)
    def test_duplicate_json_keys_fail(self):
        self.params.write_text('{"ExploratoryParameters":[],"ExploratoryParameters":[]}')
        with self.assertRaisesRegex(ValueError,"Duplicate JSON"):
            module.extend(self.source,self.target,enabled=True,parameters_path=self.params)

if __name__ == "__main__":
    unittest.main()

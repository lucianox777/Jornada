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
        self.assertEqual(extension["Status"], "DECLARED_NOT_PHYSICALLY_EVALUATED")
        self.assertEqual(extension["AdditionalMeasurements"], [])
        self.assertFalse(extension["AutoApplied"])
        self.assertFalse(result["activated"])
        self.assertEqual(self.source.read_bytes(), self.raw)
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

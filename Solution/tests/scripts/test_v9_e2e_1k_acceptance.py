import importlib.util
from pathlib import Path
import tempfile
import unittest
import json

spec=importlib.util.spec_from_file_location("acceptance",Path(__file__).resolve().parents[2]/"scripts/v9-e2e-1k-acceptance.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class AcceptanceTests(unittest.TestCase):
    def test_missing_receipt_cannot_certify(self):
        with tempfile.TemporaryDirectory() as path:
            with self.assertRaises(ValueError): m.accept(Path(path),"a"*40)
            with self.assertRaises(ValueError): m.accept(Path(path),"bad")

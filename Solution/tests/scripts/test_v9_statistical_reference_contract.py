import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-statistical-reference-contract.py"
spec = importlib.util.spec_from_file_location("v9_statistical_reference_contract", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def valid():
    return {
        "schema": "v9-statistical-reference-v1",
        "source": {"database_fingerprint_sha256": "a" * 64, "snapshot_sha256": "b" * 64},
        "distributions": {name: [{"bucket": "A", "count": 20}] for name in module.CATEGORIES},
    }


class StatisticalReferenceContractTests(unittest.TestCase):
    def test_valid_deterministic(self):
        self.assertEqual(module.validate(valid()), module.validate(valid()))

    def test_no_raw_records_or_identifiers(self):
        for key in ("cpf", "name", "observations", "connection_string"):
            obj = valid()
            obj[key] = "sensitive"
            with self.subTest(key=key), self.assertRaises(ValueError):
                module.validate(obj)

    def test_suppression_threshold(self):
        obj = valid()
        obj["distributions"]["person_first"][0]["count"] = 19
        with self.assertRaises(ValueError):
            module.validate(obj)

    def test_duplicate_bucket(self):
        obj = valid()
        obj["distributions"]["person_first"].append({"bucket": "A", "count": 30})
        with self.assertRaises(ValueError):
            module.validate(obj)

    def test_incomplete_distribution(self):
        obj = valid()
        del obj["distributions"]["mother_first"]
        with self.assertRaises(ValueError):
            module.validate(obj)


if __name__ == "__main__":
    unittest.main()

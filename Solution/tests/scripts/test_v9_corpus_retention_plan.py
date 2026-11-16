"""No automatic corpus deletion without independently verified dependencies."""
import importlib.util
import unittest
from pathlib import Path

script = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-retention-plan.py"
spec = importlib.util.spec_from_file_location("retention", script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

A, B, C = "a" * 64, "b" * 64, "c" * 64


def index():
    return {"schema": "v9-ref-retention-index-v1", "entries": [
        {"fingerprint": A, "configuration": "seed42", "state": "CURRENT", "validated": True, "approved": True},
        {"fingerprint": B, "configuration": "seed42", "state": "PREVIOUS"},
        {"fingerprint": C, "configuration": "seed43", "state": "CURRENT", "validated": True, "approved": True},
    ]}


class RetentionPlanTests(unittest.TestCase):
    def test_only_one_current_per_config_and_never_delete(self):
        result = module.plan(index(), active_fingerprints={B}, protected_fingerprints=set())
        self.assertEqual(result["status"], "READ_ONLY_NO_DELETION")
        self.assertTrue(all(x["action"] == "RETAIN" for x in result["entries"]))
        self.assertEqual(result["entries"][1]["reason"], "ACTIVE_READER")

    def test_previous_still_retained_without_lease(self):
        result = module.plan(index(), active_fingerprints=set(), protected_fingerprints=set())
        self.assertEqual(result["entries"][1]["reason"], "GC_NOT_AUTHORIZED_WITHOUT_INDEPENDENT_DEPENDENCY_PROOF")

    def test_duplicate_current_rejected(self):
        data = index()
        data["entries"][1]["state"] = "CURRENT"
        with self.assertRaisesRegex(ValueError, "Exactly one"):
            module.plan(data, active_fingerprints=set(), protected_fingerprints=set())

    def test_unapproved_current_rejected(self):
        data = index()
        data["entries"][0]["approved"] = False
        with self.assertRaisesRegex(ValueError, "validated and approved"):
            module.plan(data, active_fingerprints=set(), protected_fingerprints=set())

    def test_unknown_reader_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unknown active"):
            module.plan(index(), active_fingerprints={"d" * 64}, protected_fingerprints=set())


if __name__ == "__main__":
    unittest.main()

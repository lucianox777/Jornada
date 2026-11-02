import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-eight-receipts-bind.py"
spec = importlib.util.spec_from_file_location("v9_tf_receipts_bind", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class EightReceiptsBindTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.sources = Path(self.tmp.name) / "sources.csv"
        self.sources.write_text("StableId\n" + "".join(f"id{i}\n" for i in range(29997)) + "a\nb\nc\n", encoding="utf-8")
        self.csv = Path(self.tmp.name) / "pairs.csv"
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\na,b,1\n",
                            encoding="utf-8")
        contract = module.preflight.preflight(self.csv, source_count=30000, source_ids_csv=self.sources)
        self.evidence = Path(self.tmp.name)
        self.receipts = {}
        for pair in contract["Pairs"]:
            for option in (pair["DiagnosticOptionId"], pair["BilateralOptionId"]):
                run_name = f"{option}.bin"
                run_bytes = ("physical-run-" + option).encode()
                (self.evidence / run_name).write_bytes(run_bytes)
                model_name = f"{option}.fitted.bin"
                model_bytes = ("fitted-model-" + option).encode()
                (self.evidence / model_name).write_bytes(model_bytes)
                self.receipts[option] = dict(
                    OptionId=option, CandidatePairsSha256=contract["CandidatePairsSha256"],
                    SourceRecords=30000, SourceStableIdsSha256=contract["SourceStableIdsSha256"], TestUsed=False, PhysicallyExecuted=True,
                    PhysicalRunSha256=hashlib.sha256(run_bytes).hexdigest(),
                    FittedModelSha256=hashlib.sha256(model_bytes).hexdigest(),
                    PhysicalRunArtifact=run_name, FittedModelArtifact=model_name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_eight_receipts_bound_to_identical_candidates(self):
        result = module.bind_receipts(self.csv, self.receipts,
                                     evidence_dir=self.evidence, source_ids_csv=self.sources)
        self.assertEqual(result["PhysicalReceiptsVerified"], 8)

    def test_tampered_artifact_rejected(self):
        option = next(iter(self.receipts))
        (self.evidence / self.receipts[option]["PhysicalRunArtifact"]).write_bytes(b"tampered")
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                 evidence_dir=self.evidence)

    def test_reused_run_artifact_rejected(self):
        ids = list(self.receipts)
        self.receipts[ids[1]]["PhysicalRunArtifact"] = self.receipts[ids[0]]["PhysicalRunArtifact"]
        self.receipts[ids[1]]["PhysicalRunSha256"] = self.receipts[ids[0]]["PhysicalRunSha256"]
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                 evidence_dir=self.evidence)

    def test_reused_model_artifact_rejected(self):
        ids = list(self.receipts)
        self.receipts[ids[1]]["FittedModelArtifact"] = self.receipts[ids[0]]["FittedModelArtifact"]
        self.receipts[ids[1]]["FittedModelSha256"] = self.receipts[ids[0]]["FittedModelSha256"]
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                 evidence_dir=self.evidence)

    def test_source_census_changed_after_receipts_rejected(self):
        original = self.sources.read_text(encoding="utf-8")
        self.sources.write_text(original.replace("id0\\n", "changed0\\n", 1), encoding="utf-8")
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                 evidence_dir=self.evidence, source_ids_csv=self.sources)

    def test_candidate_universe_changed_after_receipts_rejected(self):
        self.csv.write_text("LeftStableId,RightStableId,SharedSurname\\na,b,0\\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                 evidence_dir=self.evidence, source_ids_csv=self.sources)

    def test_missing_receipt_rejected(self):
        self.receipts.pop(next(iter(self.receipts)))
        with self.assertRaises(ValueError):
            module.bind_receipts(self.csv, self.receipts,
                                     evidence_dir=self.evidence)

    def test_unexecuted_and_unpinned_rejected(self):
        option = next(iter(self.receipts))
        for key, value in (("PhysicallyExecuted", False),
                           ("CandidatePairsSha256", "c" * 64),
                           ("TestUsed", True), ("PhysicalRunSha256", "")):
            with self.subTest(field=key):
                row = self.receipts[option]
                original = row[key]
                row[key] = value
                with self.assertRaises(ValueError):
                    module.bind_receipts(self.csv, self.receipts, evidence_dir=self.evidence, source_ids_csv=self.sources)
                row[key] = original


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Final VALIDATION -> production-side V9 operational JSON -> TEST path.

Synthetic fixture: no real corpus, gabarito, independent TEST or publication.
The bytes have precisely the same V9 JSON layout as SQL payload_fitted.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/v9-tf4-validated-operational-handoff.py"
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
VALIDATION = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs"
TEST = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
CONVERTER = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9OperationalModelConversionCli.cs"
MODEL = ROOT / "src/Jornada.Linkage.Core/V9ValidatedOperationalModelFile.cs"
spec = importlib.util.spec_from_file_location("v9_tf4_validated_handoff", SCRIPT)
handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handoff)


class V9ValidatedOperationalHandoffTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.directory = self.root / "validated-operational-model"
        self.directory.mkdir()
        self.validation = self.root / "finalist-validation.json"
        self.reference = self.root / "selected-operational-model-reference.json"
        self.fields = ["MF", "ML", "PF", "PL"]
        self.raw = {
            "SchemaVersion": "V9_FITTED_SNAPSHOT_V3",
            "JointCells": [{"index": n} for n in range(81)],
            "UnitTfCalibrations": [
                {"Side": side, "Unit": unit}
                for side in ("Mother", "Person")
                for unit in ("First", "Last")
            ],
            "BlockingPasses": [{"ordinal": 1}],
            "CandidateGrouping": {"CandidateGrouping": "BLIND_FS_COMPLETE_LINK_CANDIDATES"},
            "JointSupport": {"syntheticOnly": True},
            "FitFingerprintSha256": "d" * 64,
            "ProjectionFingerprintSha256": "e" * 64,
        }
        self.bytes = json.dumps(self.raw, sort_keys=True, separators=(",", ":")).encode()
        self.sha = hashlib.sha256(self.bytes).hexdigest()
        self.filename = self.sha + ".operational.json"
        (self.directory / self.filename).write_bytes(self.bytes)
        self.validation.write_text(json.dumps({
            "status": "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED",
            "selectedValidationSnapshotSha256": self.sha,
            "selectedValidationOperationalModelFileName": self.filename,
            "selectedValidationOperationalModelContract": handoff.CONTRACT,
            "refitted": False, "testUsed": False, "certified": False, "activated": False,
        }))
        self.ref = {
            "schema": handoff.SCHEMA, "fileName": self.filename,
            "winnerSnapshotSha256": self.sha,
            "operationalModelContract": handoff.CONTRACT,
            "snapshotSchemaVersion": "V9_FITTED_SNAPSHOT_V3",
            "jointCells": 81, "tfSlots": 4, "blockingPasses": 1,
            "byteExactRoundTrip": True,
            "fitFingerprintSha256": "d" * 64,
            "projectionFingerprintSha256": "e" * 64,
            "refitted": False, "testUsed": False, "certified": False,
            "activated": False, "published": False,
        }
        self.reference.write_text(json.dumps(self.ref))

    def verify(self):
        return handoff.verify(self.validation, self.reference, self.directory, handoff.CONTRACT)

    def test_positive_single_filename_full_four_tfs_81_and_exact_sha(self):
        self.assertEqual(self.verify(), self.filename)

    def test_no_winner_does_not_export_or_pass_through_arbitrary_model(self):
        (self.directory / self.filename).unlink()
        self.reference.unlink()
        self.validation.write_text(json.dumps({
            "status": "NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES",
            "selectedValidationSnapshotSha256": None,
            "selectedValidationOperationalModelFileName": None,
            "refitted": False, "testUsed": False, "certified": False, "activated": False,
        }))
        self.assertIsNone(self.verify())
        (self.directory / "unexpected.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "no-winner"):
            self.verify()

    def test_sha_swap_extra_file_and_wrong_reference_fail_closed(self):
        file = self.directory / self.filename
        file.write_bytes(self.bytes + b" ")
        with self.assertRaisesRegex(ValueError, "SHA"):
            self.verify()
        file.write_bytes(self.bytes)
        (self.directory / "other-model.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "one and only one"):
            self.verify()
        (self.directory / "other-model.json").unlink()
        self.ref["jointCells"] = 80
        self.reference.write_text(json.dumps(self.ref))
        with self.assertRaisesRegex(ValueError, "reference mismatches"):
            self.verify()

    def test_missing_tf_grouping_and_reference_drift_fail_closed(self):
        original = self.raw
        for changed in (
            {**original, "UnitTfCalibrations": original["UnitTfCalibrations"][:3]},
            {**original, "JointCells": original["JointCells"][:80]},
            {**original, "CandidateGrouping": {"CandidateGrouping": "GOLD"}},
            {**original, "GoldParentSnapshotSha256": "f" * 64},
        ):
            with self.subTest(changed=next(x for x in changed if changed[x] != original.get(x))):
                bytes_ = json.dumps(changed, sort_keys=True, separators=(",", ":")).encode()
                new_sha = hashlib.sha256(bytes_).hexdigest()
                model = self.directory / self.filename
                model.unlink()
                self.filename = new_sha + ".operational.json"
                (self.directory / self.filename).write_bytes(bytes_)
                self.ref["fileName"] = self.filename
                self.ref["winnerSnapshotSha256"] = new_sha
                self.reference.write_text(json.dumps(self.ref))
                r = json.loads(self.validation.read_text())
                r["selectedValidationSnapshotSha256"] = new_sha
                r["selectedValidationOperationalModelFileName"] = self.filename
                self.validation.write_text(json.dumps(r))
                with self.assertRaisesRegex(ValueError, "lost TF4"):
                    self.verify()

    def test_workflow_validation_exports_even_when_test_off_and_passes_filename_to_test(self):
        wf = WF.read_text()
        validation, test = wf.split("\n  validation-100k:\n", 1)[1].split("\n  test-100k:\n", 1)
        test = test.split("\n  e2e-closeout:\n", 1)[0]
        self.assertIn("v9-tf4-validated-operational-handoff.py verify", validation)
        self.assertIn("id: operational_file", validation)
        self.assertIn("validated-operational-model/*.operational.json", validation)
        self.assertIn("v9-tf4-validated-operational-model-", validation)
        self.assertNotIn("inputs.test_required == true", validation.split(
            "- name: Upload final VALIDATION operational JSON", 1)[1].split(
            "- name: Upload compact", 1)[0])
        self.assertIn("v9-tf4-validated-operational-model-", test)
        self.assertIn("--v9-convert-winner-operational", test)
        self.assertIn("--v9-independent-test \"$model\"", test)
        self.assertIn("*.operational.json", test)
        self.assertNotIn("*.fitted.json", test)
        self.assertNotIn("test-one-winner-", wf)

        self.assertIn("V9ValidatedOperationalModelFile.ExportAsync(", VALIDATION.read_text())
        self.assertIn("selectedValidationOperationalModelFileName", VALIDATION.read_text())
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(modelPath)", CONVERTER.read_text())
        self.assertIn("V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile)", TEST.read_text())
        self.assertIn("SequenceEqual(validatedFileWinner.SerializeUtf8())", TEST.read_text())
        core = MODEL.read_text()
        self.assertIn("V9OperationalSqlModelConverter.VerifyConversion", core)
        self.assertIn("FileMode.CreateNew", core)
        self.assertIn('public const string Suffix = ".operational.json"', core)
        self.assertIn("V9_FITTED_SNAPSHOT_V3", core)
        self.assertNotIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", core)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED", wf)


if __name__ == "__main__":
    unittest.main()

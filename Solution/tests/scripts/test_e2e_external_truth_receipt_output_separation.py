"""Guard against overwriting E2E preflight inputs with a receipt."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/e2e-external-truth-boundary.py"
spec = importlib.util.spec_from_file_location("e2e_boundary_receipt_separation", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReceiptOutputSeparationTests(unittest.TestCase):
    def test_receipt_must_not_overwrite_zip_or_external_truth(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            zip_path = root / "payload.zip"
            truth_path = root / "external-truth.json"
            zip_path.write_bytes(b"synthetic ZIP placeholder")
            truth_path.write_text('{"placeholder":true}', encoding="utf-8")
            for forbidden in (zip_path, truth_path):
                with self.subTest(forbidden=forbidden.name):
                    original = forbidden.read_bytes()
                    args = ["boundary", "--zip", str(zip_path),
                            "--truth", str(truth_path), "--receipt", str(forbidden)]
                    with patch("sys.argv", args), patch.object(
                        module, "validate", side_effect=AssertionError("must fail before validation")
                    ):
                        with self.assertRaisesRegex(ValueError, "separate"):
                            module.main()
                    self.assertEqual(forbidden.read_bytes(), original)

    def test_distinct_receipt_path_preserves_normal_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            zip_path = root / "payload.zip"
            truth_path = root / "external-truth.json"
            receipt_path = root / "receipts" / "boundary.json"
            expected = {"status": "PASS", "scope": "BOUNDARY_ONLY_NOT_LINKAGE_ORACLE"}
            args = ["boundary", "--zip", str(zip_path), "--truth", str(truth_path),
                    "--receipt", str(receipt_path)]
            with patch("sys.argv", args), patch.object(module, "validate", return_value=expected) as validate:
                module.main()
            validate.assert_called_once_with(zip_path, truth_path)
            self.assertEqual(json.loads(receipt_path.read_text(encoding="utf-8")), expected)

    def test_resolved_alias_cannot_overwrite_truth(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            zip_path = root / "payload.zip"
            truth_path = root / "external-truth.json"
            truth_path.write_text("truth", encoding="utf-8")
            args = ["boundary", "--zip", str(zip_path), "--truth", str(truth_path),
                    "--receipt", str(root / "nested" / ".." / "external-truth.json")]
            with patch("sys.argv", args), patch.object(
                module, "validate", side_effect=AssertionError("must fail before validation")
            ):
                with self.assertRaisesRegex(ValueError, "separate"):
                    module.main()


if __name__ == "__main__":
    unittest.main()

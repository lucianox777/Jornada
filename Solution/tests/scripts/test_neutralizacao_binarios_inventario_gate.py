"""Negative fail-closed controls for authorized 45 -> 43 office binary inventory."""
from __future__ import annotations
import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from neutralizacao_binarios_inventario_gate import (  # noqa: E402
    APPROVED_REMOVALS, PINNED_MASTER, SCHEMA, validate_manifest,
)


class InventoryContract(unittest.TestCase):
    def setUp(self):
        self.survivors = {f"Documentos/test-{n:02d}.pdf" for n in range(43)}
        self.source = sorted(self.survivors | APPROVED_REMOVALS)
        self.manifest = {
            "schema": SCHEMA, "sourceMasterSha": PINNED_MASTER,
            "sourcePaths": self.source, "sourceCount": 45,
            "expectedCurrentCount": 43,
            "authorizedRemovalsFromSource": sorted(APPROVED_REMOVALS),
        }
        self.report = {
            "total_files": 43,
            "files": [{"path": name, "type": ".pdf", "before": 0, "after": 0}
                      for name in sorted(self.survivors)],
            "verified_mentions_before": 0, "verified_mentions_after": 0,
            "not_audited": [],
        }

    def test_only_two_authorized_removals_pass(self):
        self.assertEqual(validate_manifest(self.manifest, self.report, self.survivors), 43)

    def test_one_additional_deletion_rejected(self):
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, self.report, self.survivors - {"Documentos/test-00.pdf"})

    def test_unexpected_extra_file_rejected_even_if_count_held(self):
        changed = (self.survivors - {"Documentos/test-00.pdf"}) | {"Documentos/extra.pdf"}
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, self.report, changed)

    def test_removed_administrative_file_restored_rejected(self):
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, self.report, self.survivors | {sorted(APPROVED_REMOVALS)[0]})

    def test_fake_alternate_authorized_removal_rejected(self):
        manifest = copy.deepcopy(self.manifest)
        manifest["authorizedRemovalsFromSource"] = ["Documentos/test-00.pdf", sorted(APPROVED_REMOVALS)[1]]
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(manifest, self.report, self.survivors)

    def test_missing_report_row_rejected(self):
        report = copy.deepcopy(self.report)
        report["files"] = report["files"][:-1]
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, report, self.survivors)

    def test_unreadable_file_rejected(self):
        report = copy.deepcopy(self.report)
        report["not_audited"] = [report["files"][0]["path"]]
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, report, self.survivors)

    def test_wrong_pinned_sha_rejected(self):
        manifest = copy.deepcopy(self.manifest)
        manifest["sourceMasterSha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(manifest, self.report, self.survivors)

    def test_report_total_inflated_rejected(self):
        report = copy.deepcopy(self.report)
        report["verified_mentions_after"] = 1
        with self.assertRaisesRegex(ValueError, "FAIL_CLOSED"):
            validate_manifest(self.manifest, report, self.survivors)


if __name__ == "__main__":
    unittest.main()

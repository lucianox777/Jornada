#!/usr/bin/env python3
"""Isolated fail-closed tests for immutable TF4 TRAIN -> VALIDATION handoff."""
from pathlib import Path
import hashlib
import importlib.util
import json
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "scripts/v9-v3-four-name-100k-handoff.py"
spec = importlib.util.spec_from_file_location("v9_tf4_handoff", PATH)
handoff = importlib.util.module_from_spec(spec)
spec.loader.exec_module(handoff)
SHA = "f" * 40
RUN = 43453565012


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, sort_keys=True), encoding="utf-8")


class V9FourNameHandoffTests(unittest.TestCase):
    def fixture(self, root):
        root = Path(root)
        plan = b'{"Purpose":"SYNTHETIC_TRAIN"}'
        (root / "bound").mkdir()
        stage = root / "results/frozen-finalists"
        stage.mkdir(parents=True)
        (root / "bound/plan.json").write_bytes(plan)
        plan_sha = hashlib.sha256(plan).hexdigest()
        selection = b'{"result":{"TestUsed":false},"synthetic":true}'
        envelope = hashlib.sha256(selection).hexdigest()
        fitted = b'{"synthetic":true,"snapshot":"FITTED"}'
        snapshot = hashlib.sha256(fitted).hexdigest()
        write_json(root / "bound/binding-receipt.json", {
            "planSha256": plan_sha, "physicalHypotheses": 324, "tfOptions": 81,
            "testUsed": False, "certified": False, "activated": False})
        write_json(root / "results/selection-reference.json", {
            "envelopeSha256": envelope, "FinalistSnapshotShas": [snapshot]})
        write_json(root / "bound/technical-audit.json", {
            "schema": handoff.AUDIT_SCHEMA, "status": handoff.AUDIT_STATUS,
            "physicalMeasured": 324, "planSha256": plan_sha,
            "codeSha": SHA, "workflowRunId": RUN,
            "finalistSnapshotShas": [snapshot], "testUsed": False,
            "certified": False, "activated": False})
        (stage / (envelope + ".selection.json")).write_bytes(selection)
        (stage / (snapshot + ".fitted.json")).write_bytes(fitted)
        return stage, snapshot

    def test_prepare_and_verify_allowlisted_files_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            stage, snap = self.fixture(root)
            manifest = handoff.prepare(root, stage, SHA, RUN)
            self.assertEqual(manifest["finalistSnapshotShas"], [snap])
            self.assertEqual(handoff.verify_contents(stage, SHA, RUN), manifest)
            self.assertEqual(len(manifest["files"]), 6)
            self.assertEqual({p.name for p in stage.iterdir()},
                             set(manifest["files"]) | {handoff.MANIFEST})

    def test_wrong_run_tampered_snapshot_and_raw_csv_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            stage, snap = self.fixture(root)
            handoff.prepare(root, stage, SHA, RUN)
            with self.assertRaisesRegex(ValueError, "audit identity"):
                handoff.verify_contents(stage, SHA, RUN + 1)
            (stage / "observacoes.csv").write_text("raw-data")
            with self.assertRaisesRegex(ValueError, "unexpected"):
                handoff.verify_contents(stage, SHA, RUN)
            (stage / "observacoes.csv").unlink()
            (stage / (snap + ".fitted.json")).write_bytes(b"corruption")
            with self.assertRaisesRegex(ValueError, "SHA mismatch"):
                handoff.verify_contents(stage, SHA, RUN)

    def test_audit_failure_cannot_be_transferred(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            stage, _ = self.fixture(root)
            p = root / "bound/technical-audit.json"
            value = json.loads(p.read_text())
            value["status"] = "FAIL"
            write_json(p, value)
            with self.assertRaisesRegex(ValueError, "audit identity"):
                handoff.prepare(root, stage, SHA, RUN)

    def test_workflow_has_separate_fresh_runner_and_pinned_provenance(self):
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        train, validation = workflow.split("\n  validation-100k:\n", 1)
        self.assertIn("  physical-100k:", train)
        self.assertIn("has_finalists: ${{ steps.finalists.outputs.has_finalists }}", train)
        self.assertNotIn("--v9-validate-finalists", train)
        self.assertIn("v9-v3-four-name-100k-handoff.py prepare", train)
        self.assertIn("if-no-files-found: error", train)
        aggregate = train.split("- name: Upload aggregated evidence only, never corpus, raw TEST or credentials", 1)[1]
        self.assertNotIn("results/frozen-finalists/*.selection.json", aggregate)
        self.assertNotIn("results/frozen-finalists/*.fitted.json", aggregate)
        self.assertIn("needs: physical-100k", validation)
        self.assertIn("needs.physical-100k.result == 'success'", validation)
        self.assertIn("needs.physical-100k.outputs.has_finalists == 'true'", validation)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', validation)
        self.assertIn('test "$EXACT_SHA" = "$GITHUB_SHA"', validation)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/master"', validation)
        self.assertIn("actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093", validation)
        self.assertIn("v9-v3-four-name-100k-handoff.py verify", validation)
        self.assertIn("Start fresh masked disposable SQL Server for VALIDATION only", validation)
        self.assertIn("v9-tf-prepare-pin.sh", validation)
        self.assertIn("--v9-validate-finalists", validation)
        self.assertIn("--seed 20261112001", validation)
        self.assertIn("timeout-minutes: 120", validation)
        self.assertIn("timeout-minutes: 40", validation)
        self.assertIn("Remove VALIDATION disposable SQL even after failure", validation)
        self.assertNotIn("people-100k/observacoes.csv", validation)

    def test_large_envelope_is_hashed_with_bounded_memory(self):
        source = PATH.read_text()
        self.assertIn('for chunk in iter(lambda: file.read(4 * 1024 * 1024), b"")', source)
        self.assertNotIn("read_bytes(", source)
        workflow = (ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml").read_text()
        self.assertIn('hashlib.file_digest(stream,"sha256")', workflow)
        self.assertNotIn("hashlib.sha256(src.read_bytes())", workflow)


if __name__ == "__main__":
    unittest.main()

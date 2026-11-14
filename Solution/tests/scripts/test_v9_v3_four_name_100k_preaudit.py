#!/usr/bin/env python3
"""TRAIN-to-AUDIT immutable transfer tests (no science certification)."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "scripts/v9-v3-four-name-100k-preaudit.py"
spec = importlib.util.spec_from_file_location("v9_tf4_preaudit", PATH)
preaudit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preaudit)
SHA = "a" * 40
RUN = 43551194008


def save_json(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, sort_keys=True), encoding="utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


class V9PreauditTransferTests(unittest.TestCase):
    def fixture(self, location, finalist=True):
        root = Path(location)
        plan = b'{"source":"SYNTHETIC_TRAIN"}'
        (root / "bound").mkdir(parents=True)
        result = root / "results"
        result.mkdir()
        (root / "bound/plan.json").write_bytes(plan)
        save_json(root / "bound/binding-receipt.json", {
            "planSha256": digest(plan), "physicalHypotheses": 324,
            "tfOptions": 81, "testUsed": False, "certified": False,
            "activated": False,
        })
        save_json(result / "mu-statistical-analysis.json",
                  {"schema": "synthetic-fixture", "rows": []})
        save_json(result / "runtime-diagnostics.json", {"totalMilliseconds": 1})
        frozen = b'{"fitted":"synthetic"}'
        selected = [digest(frozen)] if finalist else []
        if finalist:
            (result / (selected[0] + ".fitted.json")).write_bytes(frozen)
        envelope = b'{"selection":"synthetic"}'
        envelope_sha = digest(envelope)
        (result / (envelope_sha + ".selection.json")).write_bytes(envelope)
        save_json(result / "selection-reference.json", {
            "envelopeSha256": envelope_sha, "FinalistSnapshotShas": selected,
            "testUsed": False, "externalValidationUsed": False,
            "activated": False,
        })
        raw = root / "people-100k/observacoes.csv"
        raw.parent.mkdir()
        raw.write_text("NOT_FOR_TRANSFER")
        return root

    def test_hash_verified_allowlist_and_no_corpus_leakage(self):
        with tempfile.TemporaryDirectory() as temp:
            source = self.fixture(Path(temp) / "source")
            target = Path(temp) / "transfer"
            report = preaudit.prepare(source, target, SHA, RUN)
            self.assertEqual(report["status"], "UNAUDITED_TRAIN_EVIDENCE_ONLY")
            self.assertEqual(len(report["finalistSnapshotShas"]), 1)
            self.assertEqual(report, preaudit.verify(target, SHA, RUN))
            self.assertEqual(len(list(target.iterdir())), 8)
            self.assertNotIn("observacoes.csv", {p.name for p in target.iterdir()})
            self.assertNotIn("technical-audit.json", {p.name for p in target.iterdir()})
            with self.assertRaisesRegex(ValueError, "immutable run"):
                preaudit.verify(target, "INVALID", RUN)
            with self.assertRaisesRegex(ValueError, "manifest mismatch"):
                preaudit.verify(target, SHA, RUN + 1)

    def test_tampering_and_extra_member_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            source = self.fixture(Path(temp) / "source")
            target = Path(temp) / "transfer"
            preaudit.prepare(source, target, SHA, RUN)
            (target / "observacoes.csv").write_text("PRIVATE")
            with self.assertRaisesRegex(ValueError, "unexpected"):
                preaudit.verify(target, SHA, RUN)
            (target / "observacoes.csv").unlink()
            (target / "mu-statistical-analysis.json").write_text("tampered")
            with self.assertRaisesRegex(ValueError, "manifest mismatch"):
                preaudit.verify(target, SHA, RUN)

    def test_zero_finalists_audited_but_validation_not_eligible(self):
        with tempfile.TemporaryDirectory() as temp:
            source = self.fixture(Path(temp) / "source", finalist=False)
            target = Path(temp) / "transfer"
            report = preaudit.prepare(source, target, SHA, RUN)
            self.assertEqual(report["finalistSnapshotShas"], [])
            self.assertEqual(report, preaudit.verify(target, SHA, RUN))


if __name__ == "__main__":
    unittest.main()

"""Behavior tests for the read-only v1 contract preflight."""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO

SCRIPT = Path(__file__).resolve().parents[1] / "contract-v1-preflight.py"
spec = importlib.util.spec_from_file_location("contract_v1_preflight", SCRIPT)
preflight = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(preflight)


class PreflightTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "Solution"
        self.root.mkdir()
        self.approvals = []
        for gestor in preflight.GESTORES:
            prefix = f"config/contracts/gestores/{gestor}/pessoa"
            self._file(f"{prefix}/v1/pessoa.json", {"gestor": gestor, "versao": 1})
            self._file(f"{prefix}/v6/pessoa.schema.json", {
                "$id": f"https://example.test/contracts/gestores/{gestor}/pessoa/v6/pessoa.schema.json",
                "type": "object",
            })
        self._approvals()

    def _file(self, rel: str, value: dict):
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, sort_keys=True) + "\n", encoding="utf-8")
        self.approvals = [e for e in self.approvals if e["path"] != rel]
        self.approvals.append({
            "path": rel,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        })

    def _approvals(self):
        path = self.root / "config/governance/schema-approvals.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"contracts": self.approvals}), encoding="utf-8")

    def test_audit_inventory_is_read_only_and_preserves_v1_metadata(self):
        before = {
            p.relative_to(self.root): p.read_bytes()
            for p in self.root.rglob("*") if p.is_file()
        }
        result = preflight.audit(self.root)
        after = {
            p.relative_to(self.root): p.read_bytes()
            for p in self.root.rglob("*") if p.is_file()
        }
        self.assertEqual(before, after)
        self.assertEqual(result["status"], "OK_READ_ONLY")
        self.assertEqual(result["mode"], "PRE_CONSOLIDACAO")
        self.assertFalse(result["databaseResetAuthorized"])
        self.assertEqual(len(result["gestores"]), 4)
        self.assertTrue(all(
            item["metadataSource"].endswith("/v1/pessoa.json")
            for item in result["gestores"]
        ))

    def test_missing_v1_metadata_fails_closed(self):
        (self.root / "config/contracts/gestores/SMS/pessoa/v1/pessoa.json").unlink()
        with self.assertRaisesRegex(preflight.PreflightError, "Arquivo obrigatorio ausente"):
            preflight.audit(self.root)

    def test_hash_drift_fails_closed(self):
        path = self.root / "config/contracts/gestores/SMADS/pessoa/v6/pessoa.schema.json"
        path.write_text(path.read_text() + "\n", encoding="utf-8")
        with self.assertRaisesRegex(preflight.PreflightError, "Divergencia SHA-256"):
            preflight.audit(self.root)

    def test_partial_cutover_is_rejected(self):
        (self.root / "config/contracts/gestores/SMS/pessoa/v6/pessoa.schema.json").unlink()
        with self.assertRaisesRegex(preflight.PreflightError, "Consolidacao parcial"):
            preflight.audit(self.root)

    def test_post_cutover_accepts_only_v1_and_valid_hashes(self):
        for gestor in preflight.GESTORES:
            prefix = f"config/contracts/gestores/{gestor}/pessoa"
            (self.root / f"{prefix}/v6/pessoa.schema.json").unlink()
            (self.root / f"{prefix}/v6").rmdir()
            self._file(f"{prefix}/v1/pessoa.schema.json", {
                "$id": f"https://example.test/contracts/gestores/{gestor}/pessoa/v1/pessoa.schema.json",
                "type": "object",
            })
        self._approvals()
        result = preflight.audit(self.root)
        self.assertEqual(result["mode"], "CONSOLIDADO_V1")
        self.assertEqual(len(result["gestores"]), 4)

    def test_cli_exits_nonzero_on_invalid_input(self):
        path = self.root / "config/contracts/gestores/SMS/pessoa/v1/pessoa.json"
        path.write_text('{"gestor":"OUTRO","versao":1}', encoding="utf-8")
        stderr = StringIO()
        with redirect_stderr(stderr), redirect_stdout(StringIO()):
            code = preflight.main(["--root", str(self.root)])
        self.assertEqual(code, 1)
        self.assertIn("FAIL", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()

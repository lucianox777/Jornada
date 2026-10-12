#!/usr/bin/env python3
"""Fail-closed offline tests for SHA-provenanced compiled outputs."""
from __future__ import annotations

import importlib.util
import json
import pathlib
import tempfile
import unittest
import os

MODULE = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "ci-built-binary-gate.py"
SPEC = importlib.util.spec_from_file_location("ci_binary", MODULE)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


class ReuseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name) / "Solution"
        self.root.mkdir()
        for section in ("src", "tests"):
            path = self.root / section / "sample" / "packages.lock.json"
            path.parent.mkdir(parents=True)
            path.write_text('{"version":1}', encoding="utf-8")
        self.original = {}
        for i in range(22):
            path = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / f"binary{i}.dll"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(f"build artifact {i}".encode())
            self.original[path.name] = path.read_bytes()
        executable = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / "binary0.dll"
        executable.chmod(0o755)
        self.context = {
            "schema": 1, "repository": "lucianox777/Jornada",
            "gitSha": "a"*40, "gitTree": "b"*40, "runId": "42",
            "sdk": "10.0.112", "os": "Linux", "machine": "x86_64",
            "locksSha256": gate.lock_digest(self.root),
        }
        self.manifest = self.root / ".local" / "compiled-release" / "manifest.json"
        self.archive = self.root / ".local" / "compiled-release" / "release-binaries.tar.gz"

    def create(self):
        return gate.produce(self.root, self.manifest, self.archive, self.context)

    def test_hash_bind_and_permissions_roundtrip(self):
        summary = self.create()
        self.assertEqual(summary["artifactCount"], 22)
        destination = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / "binary0.dll"
        destination.unlink()
        self.assertEqual(gate.consume(self.root, self.manifest, self.archive, self.context), 22)
        self.assertEqual(destination.read_bytes(), self.original["binary0.dll"])
        self.assertEqual(destination.stat().st_mode & 0o111, 0o111)

    def test_wrong_sha_fails_without_touching_binary(self):
        self.create()
        dest = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / "binary1.dll"
        dest.write_bytes(b"sentinel")
        with self.assertRaisesRegex(ValueError, "gitSha"):
            gate.consume(self.root, self.manifest, self.archive, {**self.context, "gitSha":"f"*40})
        self.assertEqual(dest.read_bytes(), b"sentinel")

    def test_locked_dependencies_are_bound(self):
        self.create()
        (self.root / "src" / "sample" / "packages.lock.json").write_text("modified")
        self.assertNotEqual(gate.lock_digest(self.root), self.context["locksSha256"])
        with self.assertRaisesRegex(ValueError, "locksSha256"):
            gate.consume(self.root, self.manifest, self.archive, {
                **self.context, "locksSha256":gate.lock_digest(self.root)
            })

    def test_tampered_archive_fails_closed(self):
        self.create()
        with self.archive.open("ab") as f:
            f.write(b"malicious")
        target = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / "binary2.dll"
        target.write_bytes(b"sentinel")
        with self.assertRaisesRegex(ValueError, "archive SHA-256"):
            gate.consume(self.root, self.manifest, self.archive, self.context)
        self.assertEqual(target.read_bytes(), b"sentinel")

    def test_missing_manifest_record_never_partially_extracts(self):
        self.create()
        payload = json.loads(self.manifest.read_text())
        payload["files"] = payload["files"][:-1]
        payload["artifactCount"] = len(payload["files"])
        self.manifest.write_text(json.dumps(payload))
        with self.assertRaisesRegex(ValueError, "size invalid|entries differ"):
            gate.consume(self.root, self.manifest, self.archive, self.context)

    def test_bad_tar_paths_are_rejected(self):
        for bad in ("../bad", "src/x/bin/Release/../../escape", "src/x/bin/Debug/a",
                    "/etc/passwd", "src/x/bin/Release/link\\evil"):
            self.assertFalse(gate.valid_path(bad), bad)

    def test_symlinked_source_refused(self):
        target = self.root / "src" / "sample" / "bin" / "Release" / "net10.0" / "binary3.dll"
        outside = self.root / "secret.txt"
        outside.write_text("secret")
        target.unlink()
        target.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, "symlinked"):
            gate.release_files(self.root)

    def test_workflow_keeps_all_required_gates_and_uses_verified_binary(self):
        workflow = (pathlib.Path(__file__).resolve().parents[3] / ".github" / "workflows" / "ci.yml").read_text()
        for name in ("impact", "dependency-lock", "deterministic-build",
                     "unit", "integration-sql", "security-analysis", "harness-smoke",
                     "ddl-upgrade", "e2e", "dt10-evidence"):
            self.assertIn("\n  " + name + ":", workflow)
        start = workflow.index("\n  integration-sql:")
        end = workflow.index("\n  dt10-evidence:", start)
        block = workflow[start:end]
        self.assertIn("needs: [impact, dependency-lock, deterministic-build]", block)
        self.assertIn("needs.deterministic-build.result == 'success'", block)
        self.assertIn("scripts/ci-built-binary-gate.py consume", block)
        self.assertNotIn("dotnet build Jornada.sln", block)
        deterministic = workflow[workflow.index("\n  deterministic-build:"):workflow.index("\n  security-analysis:")]
        self.assertIn("Prove deterministic double build", deterministic)
        self.assertIn("ci-deterministic-release-binaries", deterministic)
        self.assertIn("scripts/ci-built-binary-gate.py produce", deterministic)


if __name__ == "__main__":
    unittest.main()

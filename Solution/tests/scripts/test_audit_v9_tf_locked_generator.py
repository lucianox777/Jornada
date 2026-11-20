"""Offline contract tests for the read-only TF LOCKED generator audit."""
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/audit-v9-tf-locked-generator.py"
spec = importlib.util.spec_from_file_location("locked_audit", SCRIPT)
audit_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_module)


class LockedGeneratorAuditTests(unittest.TestCase):
    def test_required_fast_enforces_frozen_generator_before_impact_classification(self):
        workflow = (Path(__file__).resolve().parents[3] /
                    ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        enforce = "python3 Solution/scripts/audit-v9-tf-locked-generator.py --repo ."
        self.assertEqual(workflow.count(enforce), 1)
        classify = workflow.split("  classify-impact:\n", 1)[1].split("  unit-runtime:\n", 1)[0]
        self.assertLess(classify.index(enforce), classify.index("      - id: classify"))
        self.assertNotIn("continue-on-error: true", classify)

    def test_both_manifests_and_generator_are_checked(self):
        self.assertEqual(len(audit_module.LOCKS), 2)
        self.assertTrue(all("LOCK.json" in name for name in audit_module.LOCKS))
        self.assertEqual(audit_module.PARTITION.split("/")[-1], "SyntheticCorpusGenerator.cs")

    def test_matching_and_mutated_sources(self):
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp)
            subprocess.run(["git", "init", "-q", str(repo)], check=True)
            source = repo / audit_module.PARTITION
            source.parent.mkdir(parents=True)
            source.write_bytes(b"frozen-source")
            subprocess.run(["git", "-C", str(repo), "add", audit_module.PARTITION], check=True)
            tree, count = self._expected(repo)
            self.assertEqual(count, 1)
            for lock in audit_module.LOCKS:
                target = repo / lock
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(json.dumps({
                    "experiment_id": audit_module.LOCKS[lock], "status": "LOCKED",
                    "generation": {
                        "generator_sha256": tree,
                        "partition_contract": {
                            "generator_file_sha256": audit_module.sha256(source)
                        }
                    }
                }))
            # One tracked file is deliberately insufficient even with matching hashes.
            self.assertTrue(all(not r["matches"] for r in audit_module.audit(repo)))
            for i in range(24):
                extra = source.parent / f"tracked-{i:02d}.txt"
                extra.write_bytes(b"immutable")
                subprocess.run(["git", "-C", str(repo), "add", str(extra.relative_to(repo))], check=True)
            tree, count = self._expected(repo)
            self.assertEqual(count, 25)
            for lock in audit_module.LOCKS:
                target = repo / lock
                doc = json.loads(target.read_text())
                doc["generation"]["generator_sha256"] = tree
                target.write_text(json.dumps(doc))
            self.assertTrue(all(r["matches"] for r in audit_module.audit(repo)))
            # A single altered LOCKED expectation must fail independently.
            first_lock = repo / next(iter(audit_module.LOCKS))
            original = first_lock.read_bytes()
            tampered = json.loads(original)
            tampered["generation"]["generator_sha256"] = "0" * 64
            first_lock.write_text(json.dumps(tampered))
            results = audit_module.audit(repo)
            self.assertFalse(results[0]["matches"])
            self.assertTrue(results[1]["matches"])
            first_lock.write_bytes(original)

            # The file-level partition contract must fail independently
            # even if the aggregate generator digest still matches.
            partition_tampered = json.loads(original)
            partition_tampered["generation"]["partition_contract"]["generator_file_sha256"] = "0" * 64
            first_lock.write_text(json.dumps(partition_tampered))
            results = audit_module.audit(repo)
            self.assertFalse(results[0]["matches"])
            self.assertTrue(results[1]["matches"])
            first_lock.write_bytes(original)

            # A manifest cannot be treated as frozen if status is changed.
            unlocked = json.loads(original)
            unlocked["status"] = "DRAFT"
            first_lock.write_text(json.dumps(unlocked))
            results = audit_module.audit(repo)
            self.assertFalse(results[0]["matches"])
            self.assertTrue(results[1]["matches"])
            first_lock.write_bytes(original)

            # An unexpected experiment ID cannot masquerade as the locked campaign.
            wrong_experiment = json.loads(original)
            wrong_experiment["experiment_id"] = "UNREGISTERED"
            first_lock.write_text(json.dumps(wrong_experiment))
            results = audit_module.audit(repo)
            self.assertFalse(results[0]["matches"])
            self.assertTrue(results[1]["matches"])
            first_lock.write_bytes(original)

            # Missing tracked file is a hard error, not a silent reduction.
            missing = source.parent / "tracked-00.txt"
            missing.unlink()
            with self.assertRaisesRegex(ValueError, "tracked file missing"):
                audit_module.audit(repo)
            missing.write_bytes(b"immutable")
            self.assertTrue(all(r["matches"] for r in audit_module.audit(repo)))

            source.write_bytes(b"modified-source")
            self.assertTrue(all(not r["matches"] for r in audit_module.audit(repo)))

    def test_historical_source_with_current_lock_root(self):
        # The historical generator checkout may predate both LOCK manifests.
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            historical = root / "historical"
            current = root / "current"
            historical.mkdir()
            current.mkdir()
            subprocess.run(["git", "init", "-q", str(historical)], check=True)
            source = historical / audit_module.PARTITION
            source.parent.mkdir(parents=True)
            source.write_bytes(b"historical-partition")
            subprocess.run(["git", "-C", str(historical), "add", audit_module.PARTITION], check=True)
            for i in range(24):
                extra = source.parent / f"historical-{i:02d}.txt"
                extra.write_bytes(b"historical")
                subprocess.run(["git", "-C", str(historical), "add", str(extra.relative_to(historical))], check=True)
            tree, count = self._expected(historical)
            self.assertEqual(count, 25)
            for relative, experiment_id in audit_module.LOCKS.items():
                manifest = current / relative
                manifest.parent.mkdir(parents=True, exist_ok=True)
                manifest.write_text(json.dumps({
                    "experiment_id": experiment_id, "status": "LOCKED",
                    "generation": {
                        "generator_sha256": tree,
                        "partition_contract": {"generator_file_sha256": audit_module.sha256(source)}
                    }
                }))
            self.assertFalse(any((historical / relative).exists() for relative in audit_module.LOCKS))
            self.assertTrue(all(r["matches"] for r in audit_module.audit(historical, current)))
            source.write_bytes(b"historical-tampered")
            self.assertTrue(all(not r["matches"] for r in audit_module.audit(historical, current)))

    @staticmethod
    def _expected(repo):
        files = subprocess.run(
            ["git", "-C", str(repo), "ls-files", "--", audit_module.GENERATOR],
            capture_output=True, text=True, check=True,
        ).stdout.splitlines()
        import hashlib
        lines = "".join(f"{audit_module.sha256(repo / p)}  {p}\n" for p in sorted(files))
        return hashlib.sha256(lines.encode()).hexdigest(), len(files)



class RequiredFastFrozenIdentityWiringTests(unittest.TestCase):
    def test_required_fast_executes_fail_closed_identity_audit(self):
        workflow = (Path(__file__).resolve().parents[3] /
                    ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        self.assertIn(
            "python3 Solution/scripts/audit-v9-tf-locked-generator.py --repo .",
            workflow,
        )
        self.assertNotIn(
            "python3 Solution/scripts/audit-v9-tf-locked-generator.py --repo . || true",
            workflow,
        )

if __name__ == "__main__":
    unittest.main()

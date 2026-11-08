#!/usr/bin/env python3
"""Regression: V9 physical SQL credentials are masked before any container startup."""
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/v9-physical-seed42-probe.yml"
STARTER = ROOT / "Solution/scripts/v9-tf-start-disposable-sql.sh"


class V9MaskedSqlProbeTests(unittest.TestCase):
    def test_physical_workflow_has_no_password_literal_allowlist_bypass(self):
        policy_path = ROOT / "Solution/config/security/source-sanity-policy.json"
        policy = json.loads(policy_path.read_text(encoding="utf-8"))
        workflow = ".github/workflows/v9-physical-seed42-probe.yml"
        starter = "Solution/scripts/v9-tf-start-disposable-sql.sh"
        # TLS exception remains strictly scoped to disposable CI SQL, but
        # neither entrypoint may exempt a future hard-coded SA password.
        self.assertIn(workflow, policy["allowedInsecureSqlGlobs"])
        self.assertIn(starter, policy["allowedInsecureSqlGlobs"])
        self.assertNotIn(workflow, policy["allowedPasswordLiteralGlobs"])
        self.assertNotIn(starter, policy["allowedPasswordLiteralGlobs"])

    def test_auto_physical_starts_container_only_after_sha_gate(self):
        w = WORKFLOW.read_text(encoding="utf-8")
        self.assertEqual(w.count("Start masked disposable SQL Server"), 1)
        self.assertEqual(w.count("Remove disposable SQL Server"), 1)
        self.assertEqual(w.count("run: bash Solution/scripts/v9-tf-start-disposable-sql.sh"), 1)
        self.assertNotIn("MSSQL_SA_PASSWORD: 'Jornada_", w)
        self.assertNotIn("services:", w, "Service provisioning leaks env before add-mask.")
        self.assertNotIn("workflow_dispatch:", w)
        self.assertNotIn("inputs.mode", w)
        self.assertIn("--expected-sha", w)
        self.assertIn("--mode auto_30k", w)
        self.assertIn("v9-tf-ref-provenance-gate.py", w)
        execute = w.index("Prove exact master push SHA before starting SQL")
        assert w.find("Start masked disposable SQL Server", execute) > execute
        self.assertIn("ref: ${{ github.sha }}", w, "Physical checkout must pin the integrated commit.")
        self.assertIn("--mode auto_30k", w, "Master push must prove SHA before Docker.")
        self.assertNotIn("  pull_request:\n", w, "PR is fast-only under PO 06/11 decision.")

    def test_mask_registered_before_docker_and_only_loopback_sql(self):
        code = STARTER.read_text(encoding="utf-8")
        self.assertLess(code.index("printf '::add-mask::"), code.index("docker run --detach"))
        self.assertIn("--publish 127.0.0.1:1433:1433", code)
        self.assertIn('openssl rand -hex 24', code)
        self.assertIn('>> "$GITHUB_ENV"', code)
        self.assertNotIn("github.run_id", code)
        self.assertNotIn("set -x", code)
        self.assertIn("Fail closed:", code)

    def test_missing_actions_environment_fails_before_docker(self):
        env = os.environ.copy()
        for name in ("GITHUB_ENV", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
            env.pop(name, None)
        result = subprocess.run(["bash", str(STARTER)], env=env, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("environment is mandatory", result.stderr)

    def test_fake_runner_secret_is_masked_before_docker(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            tools = root / "bin"
            tools.mkdir()
            docker_log = root / "docker-invocations"
            docker = tools / "docker"
            docker.write_text(
                "#!/usr/bin/env bash\n"
                "printf '%s\\n' \"$*\" >> \"$DOCKER_INVOCATIONS\"\n"
                "case \"$1\" in run) echo fake-container-id;; exec) exit 0;; *) exit 1;; esac\n",
                encoding="utf-8",
            )
            docker.chmod(0o700)
            openssl = tools / "openssl"
            openssl.write_text("#!/usr/bin/env bash\nprintf '%048d\\n' 0\n", encoding="utf-8")
            openssl.chmod(0o700)
            envfile = root / "github-env"
            envfile.touch()
            env = os.environ.copy()
            env.update({
                "GITHUB_ENV": str(envfile),
                "GITHUB_RUN_ID": "9876543",
                "GITHUB_RUN_ATTEMPT": "1",
                "DOCKER_INVOCATIONS": str(docker_log),
                "PATH": str(tools) + os.pathsep + env.get("PATH", ""),
            })
            result = subprocess.run(
                ["bash", str(STARTER)], env=env, capture_output=True, text=True, timeout=15
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            mask_line = next(line for line in result.stdout.splitlines()
                             if line.startswith("::add-mask::"))
            secret = mask_line.removeprefix("::add-mask::")
            self.assertGreaterEqual(len(secret), 50)
            invocations = docker_log.read_text(encoding="utf-8")
            self.assertIn("MSSQL_SA_PASSWORD=" + secret, invocations)
            self.assertIn("exec v9-probe-sql-9876543-1", invocations)
            self.assertIn("JORNADA_SQL_SA_PASSWORD=" + secret, envfile.read_text())
            self.assertNotIn(secret, result.stderr)
            self.assertIn("PASS: disposable SQL Server healthy", result.stdout)


if __name__ == "__main__":
    unittest.main()

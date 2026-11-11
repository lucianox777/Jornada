"""Offline fail-closed guard for CI E2E API image DNS-only recovery."""
import importlib.util
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
spec = importlib.util.spec_from_file_location(
    "private_api_build_dns", SCRIPTS / "e2e-private-sql-api-dns-retry-gate.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

APT_DNS_LOG = (
    "Dockerfile:63\n"
    "#9 Err:1 https://archive.ubuntu.com/ubuntu noble InRelease\n"
    "#9   Could not resolve 'archive.ubuntu.com'\n"
    "#9 Err:4 https://security.ubuntu.com/ubuntu noble-security InRelease\n"
    "#9   Could not resolve 'security.ubuntu.com'\n"
    "E: Unable to locate package jq\n"
    "E: Unable to locate package curl\n"
    "failed to solve: process apt-get exited with exit code: 100\n"
)


class PrivateSqlApiBuildDnsRecoveryTests(unittest.TestCase):
    def test_exact_known_apt_dns_signature_is_retryable(self):
        self.assertTrue(gate.is_exact_apt_dns_failure(APT_DNS_LOG))
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / "api-image-build.log"
            log.write_text(APT_DNS_LOG)
            self.assertEqual(gate.main([str(log)]), 0)

    def test_other_build_failures_never_trigger_host_build_network(self):
        for omitted in gate.SIGNATURES:
            with self.subTest(omitted=omitted):
                log = APT_DNS_LOG.replace(omitted, "[different error]")
                self.assertFalse(gate.is_exact_apt_dns_failure(log))
        for generic in ("dotnet restore failed; exit code: 100",
                        "E: Unable to locate package jq\nE: Unable to locate package curl\nexit code: 100",
                        "Could not resolve 'archive.ubuntu.com'\nexit code: 100",
                        "", "Network unavailable"):
            with self.subTest(generic=generic):
                self.assertFalse(gate.is_exact_apt_dns_failure(generic))
        with tempfile.TemporaryDirectory() as tmp:
            missing = Path(tmp) / "missing.log"
            self.assertEqual(gate.main([str(missing)]), 2)
            blank = Path(tmp) / "blank.log"
            blank.write_text("generic build error")
            self.assertEqual(gate.main([str(blank)]), 2)
            large = Path(tmp) / "large.log"
            with large.open("wb") as out:
                out.truncate(gate.MAX_LOG_BYTES + 1)
            self.assertEqual(gate.main([str(large)]), 2)
            self.assertEqual(gate.main([]), 2)

    def test_same_image_context_and_dockerfile_as_canonical_compose(self):
        # docker compose config is pure offline validation (no SQL/build/daemon).
        # The guarded second build is intentionally the same canonical image.
        if not shutil.which("docker"):
            self.skipTest("Docker CLI unavailable in this local environment")
        compose = ROOT / "install/console-dev-e2e/docker-compose.workers.yml"
        env = dict(os.environ)
        env.update(JORNADA_WORKERS_E2E_ID="contract123",
                   JORNADA_WORKERS_E2E_SQL_PASSWORD="FakeOnlyCIConfigAa9!xyz")
        result = subprocess.run(
            ["docker", "compose", "--env-file", "/dev/null", "-f",
             str(compose), "config", "--format", "json"],
            cwd=ROOT, env=env, text=True, capture_output=True,
            timeout=30, check=False)
        self.assertEqual(result.returncode, 0, result.stderr[:1000])
        api = json.loads(result.stdout)["services"]["api"]
        self.assertEqual(api["image"], "jornada-workers-e2e:test")
        self.assertEqual(Path(api["build"]["context"]).resolve(), ROOT.resolve())
        self.assertEqual(api["build"]["dockerfile"], "install/container-test/Dockerfile")
        self.assertFalse(api["build"].get("args"),
                         "fallback must mirror all Compose build args")
        self.assertNotIn("network", api["build"])

    def test_host_network_override_is_build_only_and_e2e_remains_mandatory(self):
        script = (SCRIPTS / "e2e-private-sql-bootstrap-runtime.sh").read_text()
        self.assertIn('compose build api > "$OUT/api-image-build.log" 2>&1', script)
        self.assertIn('python3 scripts/e2e-private-sql-api-dns-retry-gate.py "$OUT/api-image-build.log"', script)
        # BuildKit needs an explicit entitlement; Bake does not support the
        # partial temporary Compose override used in the previous attempt.
        self.assertIn('docker buildx build --allow=network.host --network=host --load', script)
        self.assertIn('-t "jornada-workers-e2e:${JORNADA_WORKERS_E2E_IMAGE_TAG:-test}"', script)
        self.assertIn('-f "$ROOT/install/container-test/Dockerfile" "$ROOT"', script)
        self.assertIn('> "$OUT/api-image-build-host-network.log" 2>&1', script)
        self.assertEqual(script.count('docker buildx build --allow=network.host'), 1)
        self.assertNotIn('docker buildx bake', script)
        self.assertNotIn('network_mode: host', script)
        self.assertIn('compose up -d --no-build --no-deps api resultado-api', script)
        self.assertIn('sql-bootstrap', script)
        self.assertNotIn('network_mode: host', script)
        self.assertNotIn('compose down', script)
        self.assertNotIn('docker compose down', script)
        compose = (ROOT / "install/console-dev-e2e/docker-compose.workers.yml").read_text()
        self.assertNotIn('network: host', compose)
        self.assertNotIn('network_mode: host', compose)
        self.assertIn('JornadaE2E', compose)


if __name__ == "__main__":
    unittest.main()

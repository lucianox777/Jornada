#!/usr/bin/env python3
"""Fail closed if the CI refactor silently removes mandatory validation jobs."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
REQUIRED = (
    "impact", "dependency-lock", "ddl-upgrade", "unit",
    "security-analysis", "harness-smoke", "e2e",
    "deterministic-build", "integration-sql", "dt10-evidence",
)


def test_required_ci_jobs_present():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert re.search(r"(?m)^jobs:\s*$", workflow)
    names = set(re.findall(r"(?m)^  ([a-z][a-z0-9-]*):\s*$", workflow))
    missing = set(REQUIRED) - names
    assert not missing, f"Required CI jobs removed: {sorted(missing)}"


def test_full_gate_is_fail_closed():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    for name in REQUIRED[1:]:
        block = re.search(
            rf"(?ms)^  {re.escape(name)}:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
            workflow,
        )
        assert block, f"Missing CI job: {name}"
        assert "needs:" in block.group(1), f"CI job {name} must declare dependencies"

def test_unit_parallel_contracts_and_runtime_are_both_mandatory():
    """Both exhaustive static guards and compiled unit/coverage must be green."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    def job(name):
        match = re.search(
            rf"(?ms)^  {re.escape(name)}:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
            workflow,
        )
        assert match, "Missing CI unit job: " + name
        return match.group(1)
    contracts = job("unit-contracts")
    runtime = job("unit-runtime")
    aggregate = job("unit")
    for part in (contracts, runtime):
        assert "needs: [impact, dependency-lock]" in part
        assert "needs.dependency-lock.result == 'success'" in part
        assert "dotnet restore Jornada.sln --locked-mode" in part
        assert "name: nuget-lockfiles" in part
        assert "dotnet-version: '10.0.113'" in part
        assert "persist-credentials: false" in part
    for protected in (
        "Gate source sanity and pinned workflow actions",
        "Gate destructive DDL changes",
        "Gate authorization matrix and negative mutations",
        "Gate security/data-minimization surface",
        "Gate HML contracts without fabricating approvals",
        "Gate architecture",
    ):
        if protected == "Gate architecture":
            protected = "Gate project dependency architecture"
        assert protected in contracts, "Contract removed: " + protected
    for protected in (
        "dotnet build Jornada.sln --configuration Release --no-restore",
        "Unit tests must execute without skips",
        "OpenAPI runtime conformance must execute for all 19 operations",
        "Accept measured DT-05 synthetic snapshot cost and latency",
        "T0.1e Console actual Chromium browser click acceptance",
        "Upload CycloneDX SBOM",
        "Audit transitive NuGet vulnerabilities",
    ):
        assert protected in runtime, "Runtime gate removed: " + protected
    assert "dotnet build Jornada.sln" not in contracts
    assert "Unit tests must execute without skips" not in contracts
    assert "needs: [impact, unit-contracts, unit-runtime]" in aggregate
    assert "if: always() && needs.impact.outputs.run_full == 'true'" in aggregate
    assert '"$CONTRACT_RESULT" == success' in aggregate
    assert '"$RUNTIME_RESULT" == success' in aggregate
    for name in ("unit-test-evidence", "openapi-runtime-evidence", "sbom-cyclonedx"):
        assert "name: " + name in aggregate
        assert "name: " + name in runtime
    for proof in ("unit-evidence/unit.trx", "coverage.cobertura.xml",
                  "openapi-evidence/openapi-runtime.trx", "sbom-evidence/sbom.cdx.json"):
        assert proof in aggregate, "Missing same-run evidence proof: " + proof



def test_security_guards_codeql_and_aggregate_are_independent_and_mandatory():
    workflow = WORKFLOW.read_text(encoding="utf-8")

    def job(name):
        match = re.search(
            rf"(?ms)^  {re.escape(name)}:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
            workflow,
        )
        assert match, "Missing security job: " + name
        return match.group(1)

    guards = job("security-guards")
    codeql = job("security-codeql")
    aggregate = job("security-analysis")

    for part in (guards, codeql):
        assert "needs: [impact, dependency-lock]" in part
        assert "needs.dependency-lock.result == 'success'" in part
        assert "dotnet restore Jornada.sln --locked-mode" in part
        assert "dotnet restore tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --locked-mode" in part
        assert "dotnet-version: '10.0.113'" in part
        assert "name: nuget-lockfiles" in part
        assert "persist-credentials: false" in part
        assert "if-no-files-found: error" in part

    for item in (
        "Test Bash SQL entrypoints keep local passwords out of argv",
        "Gate source sanity and workflow action pins",
        "Test secure local SQL credential bootstrap",
        "Verify SQL credentials never enter CI Docker argv",
        "Verify RC SQL commands use temporary environment credentials",
        "Test Gitleaks scanner failure paths",
        "GITLEAKS_LINUX_X64_SHA256",
        "Scan current tree with Gitleaks",
        "Audit NuGet vulnerabilities as structured evidence",
        "vulnerability-evidence-gate.py",
        "name: security-guards-evidence",
    ):
        assert item in guards, "Static security coverage dropped: " + item

    for item in (
        "security-events: write",
        "github/codeql-action/init@2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2",
        "github/codeql-action/analyze@2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2",
        "queries: security-extended",
        "build-mode: manual",
        "dotnet build Jornada.sln --configuration Release --no-restore",
        "upload: never",
        "sarif-security-gate.py",
        "name: security-codeql-evidence",
    ):
        assert item in codeql, "CodeQL protection dropped: " + item
    assert "security-events: write" not in guards
    assert "security-events: write" not in aggregate
    assert "github/codeql-action" not in guards
    assert "needs: [impact, security-guards, security-codeql]" in aggregate
    assert "if: always() && needs.impact.outputs.run_full == 'true'" in aggregate
    assert '"$GUARD_RESULT" == success' in aggregate
    assert '"$CODEQL_RESULT" == success' in aggregate
    for name in (
        "security-guards-evidence", "security-codeql-evidence",
        "security-analysis-evidence",
    ):
        assert "name: " + name in aggregate
    for name in (
        "gitleaks-current-tree.json", "vulnerability-summary.json",
        "sarif-summary.json", "*.sarif",
    ):
        assert name in aggregate, "Missing same-run security evidence: " + name



def test_dt10_reusable_job_has_same_gate_and_real_sql_acceptance():
    """DT10 moves to a reusable job without erasing the mandatory CI contract."""
    caller = WORKFLOW.read_text(encoding="utf-8")
    reusable = ROOT / ".github/workflows/dt10-evidence.yml"
    assert reusable.is_file(), "DT10 reusable workflow removed"
    evidence = reusable.read_text(encoding="utf-8")
    block = re.search(
        r"(?ms)^  dt10-evidence:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
        caller,
    )
    assert block, "DT10 caller status check missing"
    body = block.group(1)
    assert "needs: [impact, dependency-lock]" in body, "DT10 lost verified NuGet dependency"
    assert "github.event_name == 'push'" in body
    assert "needs.impact.outputs.run_dt10 == 'true'" in body
    assert "needs.dependency-lock.result == 'success'" in body
    assert "uses: ./.github/workflows/dt10-evidence.yml" in body
    assert "workflow_call:" in evidence, "DT10 is not callable"
    assert evidence.count("  dt10-evidence:") == 1
    assert "jobs:\n  dt10-evidence:" in evidence
    assert "runs-on: ubuntu-latest" in evidence
    assert "timeout-minutes: 60" in evidence
    assert "permissions:\n  contents: read" in evidence
    assert "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:" in evidence
    assert "name: nuget-lockfiles" in evidence
    assert "dotnet restore Jornada.sln --locked-mode" in evidence
    assert "dotnet build Jornada.sln --configuration Release --no-restore" in evidence
    assert "Database=JornadaSyntheticDev" in evidence
    assert "TestCategory=DT10Evidence" in evidence
    assert "--forbid-skipped --minimum-tests 4" in evidence
    assert "name: Upload DT-10 evidence" in evidence
    assert "if: always()" in evidence
    assert "if-no-files-found: error" in evidence
    assert "name: dt10-evidence" in evidence
    assert "Solution/.local/test-evidence/dt10" in evidence
    policy = json.loads(
        (ROOT / "Solution/config/security/source-sanity-policy.json").read_text(encoding="utf-8")
    )
    for key in ("allowedInsecureSqlGlobs", "allowedPasswordLiteralGlobs"):
        assert ".github/workflows/dt10-evidence.yml" in policy[key], (
            "synthetic CI-only credentials must stay explicitly audited by exact filename"
        )
    classifier = (ROOT / "Solution/scripts/ci-impact-classifier.py").read_text(encoding="utf-8")
    assert '".github/workflows/dt10-evidence.yml"' in classifier, (
        "changes to reusable DT10 must trigger its SQL evidence gate"
    )



def test_parallel_e2e_keeps_both_real_suites_and_aggregated_gate():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    def block(name):
        found = re.search(
            rf"(?ms)^  {re.escape(name)}:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
            workflow,
        )
        assert found, f"Missing E2E job {name}"
        return found.group(1)

    console = block("e2e-console")
    private = block("e2e-private-sql")
    gate = block("e2e")
    for item in (console, private):
        assert "needs: [impact, dependency-lock]" in item
        assert "needs.impact.outputs.run_full == 'true'" in item
        assert "needs.dependency-lock.result == 'success'" in item
        assert "name: nuget-lockfiles" in item
    # Browser job is pinned to a Playwright-compatible image. The SQL-only
    # branch remains independent on the latest runner.
    assert "runs-on: ubuntu-24.04" in console
    assert "runs-on: ubuntu-latest" in private
    assert "jornada-runner.py --profile e2e --allow-reset e2e" in console
    assert "JORNADA_E2E_LOCKED_RESTORE: 'true'" in console
    assert "name: local-e2e-evidence" in console
    assert "scripts/e2e-private-sql-bootstrap-runtime.sh" in private
    assert "JORNADA_WORKERS_E2E_RUNTIME_TEST: 'true'" in private
    assert "dotnet restore Jornada.sln --locked-mode" in private
    assert "dotnet build src/Jornada.DevConsole/Jornada.DevConsole.csproj --configuration Release --no-restore" in private
    assert "name: e2e-private-sql-evidence" in private
    assert "if-no-files-found: error" in private
    assert "needs: [impact, e2e-console, e2e-private-sql]" in gate
    assert "always() && needs.impact.outputs.run_full == 'true'" in gate
    assert 'test "$ZIP_RESULT" = success' in gate
    assert 'test "$PRIVATE_RESULT" = success' in gate
    assert "name: local-e2e-evidence" in gate
    assert "name: e2e-private-sql-evidence" in gate
    assert "c3-2d-sql-bootstrap/summary.json" in gate
    assert "permissions:\n      actions: read\n      contents: read" in gate



import unittest

class CiGateContractTests(unittest.TestCase):
    def test_required_jobs(self):
        test_required_ci_jobs_present()

    def test_dependencies(self):
        test_full_gate_is_fail_closed()

    def test_parallel_e2e(self):
        test_parallel_e2e_keeps_both_real_suites_and_aggregated_gate()

    def test_security_parallel(self):
        test_security_guards_codeql_and_aggregate_are_independent_and_mandatory()

    def test_unit_parallel(self):
        test_unit_parallel_contracts_and_runtime_are_both_mandatory()

    def test_dt10_reusable(self):
        test_dt10_reusable_job_has_same_gate_and_real_sql_acceptance()



class TestMasterPushPhysicalWorkflowPolicy(__import__("unittest").TestCase):
    """Run by fast PR gate as fail-closed physical-workflow preflight."""

    def test_full_master_ci_and_required_fast_pr_checks(self):
        full = WORKFLOW.read_text(encoding="utf-8")
        fast = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        self.assertIn("  push:", full)
        self.assertIn("  workflow_dispatch:", full)
        self.assertNotIn("  pull_request:", full)
        self.assertIn("  pull_request:", fast)
        for name in ("unit-runtime", "deterministic-build", "security-guards"):
            self.assertIn("  " + name + ":", fast)

    def test_physical_workflows_have_sha_and_branch_on_push(self):
        files = (
            "v9-v3-direct-physical.yml", "v9-v3-broad-train-regression.yml",
            "v9-mu-seed42-technical-regression.yml", "v9-mu-tf0-controlled-same-frame.yml",
            "v9-v3-surname-dependence-plan.yml",
        )
        for filename in files:
            with self.subTest(filename=filename):
                data = (ROOT / ".github/workflows" / filename).read_text(encoding="utf-8")
                self.assertIn("  push:", data)
                self.assertIn("github.sha", data)
                self.assertNotIn("github.event.pull_request.head.sha", data)
                self.assertNotIn("github.event.pull_request.head.repo", data)
                self.assertNotIn("github.head_ref", data)
                self.assertIn("github.ref", data)

    def test_chromium_browser_jobs_use_supported_ubuntu_2404(self):
        """Playwright 1.56 must not silently move to unsupported ubuntu26.04."""
        workflow = WORKFLOW.read_text(encoding="utf-8")
        for job in ("unit-runtime", "e2e-console"):
            with self.subTest(job=job):
                match = re.search(
                    rf"(?ms)^  {job}:\s*\n(.*?)(?=^  [a-z][a-z0-9-]*:\s*$|\Z)",
                    workflow)
                self.assertIsNotNone(match)
                self.assertIn("runs-on: ubuntu-24.04", match.group(1))
                self.assertIn("playwright install --with-deps chromium", match.group(1))
                self.assertNotIn("runs-on: ubuntu-latest", match.group(1))

    def test_windows_and_independent_verification_run_only_on_master(self):
        """Never create Windows bundles or execute the independent SQL suite on PRs."""
        for filename, job in (
            ("windows-production-installer.yml", "windows-production-bundle"),
            ("linkage-independent-validation.yml", "independent-validation"),
        ):
            with self.subTest(workflow=filename):
                data = (ROOT / ".github/workflows" / filename).read_text(encoding="utf-8")
                trigger = data.split("\njobs:\n", 1)[0]
                self.assertIn("\n  push:\n    branches:\n      - master\n", trigger)
                self.assertIn("\n  workflow_dispatch:\n", trigger)
                self.assertNotIn("\n  pull_request:", trigger,
                    "Feature PRs must not execute these expensive full-master proofs")
                self.assertIn(f"  {job}:\n    if: github.ref == 'refs/heads/master'", data)
                self.assertIn("    paths:\n", trigger,
                    "Keep existing master-push path filters; do not fan out every commit")
        fast = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        self.assertIn("\n  pull_request:\n", fast)
        for gate in ("unit-runtime", "deterministic-build", "security-guards"):
            self.assertIn(f"  {gate}:", fast)

    def test_30k_push_diff_and_job_cannot_be_skipped_silently(self):
        data = (ROOT / ".github/workflows/v9-physical-seed42-probe.yml").read_text(encoding="utf-8")
        # Never trust only the immediately previous push: earlier failed
        # physical changes remain uncovered until a real SQL 30k succeeds.
        self.assertNotIn("github.event.before", data)
        self.assertIn("v9-physical-coverage-baseline.py", data)
        self.assertIn("--no-validated-baseline", data)
        self.assertIn("git merge-base --is-ancestor", data)
        self.assertIn("BASE_SHA=", data)
        self.assertIn("CHECKOUT_SHA: ${{ github.sha }}", data)
        self.assertIn("&& github.event_name == 'push'", data)
        self.assertIn("--mode auto_30k", data)
        self.assertIn('"$PHYSICAL_RESULT" == "success"', data)
        self.assertIn("v9-tf-ref-provenance-gate.py", data)
        self.assertIn("NOT_REQUIRED_FOR_THIS_DIFF", data)
        self.assertNotIn("TF_SHA: ${{ github.event.pull_request.head.sha", data)
        fast = (ROOT / ".github/workflows/jornada-pr-required-fast.yml").read_text(encoding="utf-8")
        for tag in ("actions/checkout@v4", "actions/setup-python@v5", "actions/setup-dotnet@v4"):
            self.assertNotIn(tag, fast)


class IntegralDevWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Ubuntu provides PyYAML for system Python, independently of setup-python.
        import subprocess
        workflow = ROOT / ".github/workflows/jornada-e2e-integral-v9-dev.yml"
        parse = "import json,sys,yaml; print(json.dumps(yaml.load(sys.stdin, Loader=yaml.BaseLoader)))"
        cls.workflow = json.loads(subprocess.check_output(
            ["/usr/bin/python3", "-c", parse], input=workflow.read_text(), text=True))
        cls.steps = cls.workflow["jobs"]["dev-1k"]["steps"]

    def test_only_manual_dispatch_on_a_read_only_hosted_runner(self):
        self.assertEqual(list(self.workflow["on"]), ["workflow_dispatch"])
        inputs = self.workflow["on"]["workflow_dispatch"]["inputs"]
        self.assertEqual(list(inputs), ["expected_sha"])
        self.assertEqual(inputs["expected_sha"]["required"], "true")
        self.assertEqual(self.workflow["permissions"], {"contents": "read"})
        job = self.workflow["jobs"]["dev-1k"]
        self.assertEqual(job["runs-on"], "ubuntu-latest")
        self.assertLessEqual(int(job["timeout-minutes"]), 360)
        self.assertEqual(self.steps[0]["id"], "admission")
        self.assertEqual(self.steps[1]["with"]["ref"], "${{ github.sha }}")
        self.assertEqual(self.steps[1]["with"]["persist-credentials"], "false")

    def test_dispatch_guard_rejects_wrong_ref_sha_event_owner_or_protection(self):
        import subprocess
        sha = "a" * 40
        env = {"GITHUB_REPOSITORY": "lucianox777/Jornada", "GITHUB_EVENT_NAME": "workflow_dispatch",
               "GITHUB_REF": "refs/heads/master", "GITHUB_REF_TYPE": "branch", "GITHUB_REF_PROTECTED": "true",
               "GITHUB_ACTOR": "lucianox777", "GITHUB_TRIGGERING_ACTOR": "lucianox777",
               "GITHUB_REPOSITORY_OWNER": "lucianox777", "GITHUB_SHA": sha, "EXPECTED_SHA": sha}
        script = self.steps[0]["run"]
        self.assertEqual(subprocess.run(["bash", "-c", script], env=env).returncode, 0)
        for key, value in [("GITHUB_REF", "refs/heads/feature"), ("GITHUB_REF_TYPE", "tag"),
                           ("GITHUB_EVENT_NAME", "push"), ("GITHUB_REF_PROTECTED", "false"),
                           ("EXPECTED_SHA", "b" * 40), ("EXPECTED_SHA", "abc"),
                           ("GITHUB_REPOSITORY", "fork/Jornada"), ("GITHUB_ACTOR", "other"),
                           ("GITHUB_TRIGGERING_ACTOR", "other")]:
            with self.subTest(key=key, value=value):
                self.assertNotEqual(subprocess.run(["bash", "-c", script], env={**env, key: value}).returncode, 0)

    def test_checkout_guard_rejects_a_master_that_moved(self):
        import os
        import subprocess
        import tempfile
        guard = self.steps[2]["run"]
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp, "repo")
            subprocess.run(["git", "init", "-q", "-b", "master", str(repo)], check=True)
            def git(*args):
                return subprocess.check_output(["git", "-c", "user.name=Test", "-c", "user.email=test@localhost",
                                                *args], cwd=repo, text=True).strip()
            git("commit", "-q", "--allow-empty", "-m", "initial")
            sha = git("rev-parse", "HEAD")
            job = Path(temp, "job")
            subprocess.run(["git", "clone", "-q", str(repo), str(job)], check=True)
            def verify(value):
                return subprocess.run(["bash", "-c", guard], cwd=job, env={**os.environ, "GITHUB_SHA": value}).returncode
            self.assertEqual(verify(sha), 0)
            self.assertNotEqual(verify("f" * 40), 0)
            git("commit", "-q", "--allow-empty", "-m", "advanced")
            self.assertNotEqual(verify(sha), 0)

    def test_artifact_is_exact_receipt_and_physical_failure_is_not_ignored(self):
        upload, = [s for s in self.steps if "upload-artifact@" in s.get("uses", "")]
        self.assertEqual(upload["with"]["path"], "${{ runner.temp }}/e2e-integral-dev.json")
        self.assertEqual(upload["with"]["if-no-files-found"], "error")
        self.assertTrue(all("continue-on-error" not in step for step in self.steps))
        physical, = [s for s in self.steps if s.get("id") == "http"]
        self.assertEqual(physical["env"]["JORNADA_E2E_HTTP_VOLUME_COUNT"], "1000")
        self.assertIn("--profile e2e --allow-reset e2e", physical["run"])

    def test_workflow_only_removes_attempt_owned_volumes(self):
        job = self.workflow["jobs"]["dev-1k"]
        namespace = "jornada-e2e-${{ github.run_id }}-${{ github.run_attempt }}"
        self.assertEqual(job["env"]["COMPOSE_PROJECT_NAME"], namespace)
        self.assertEqual(job["env"]["JORNADA_SQL_CONTAINER_NAME"], namespace + "-sql")
        self.assertEqual(job["env"]["JORNADA_SQL_VOLUME_NAME"], namespace + "-data")
        cleanup, = [s for s in self.steps if s.get("id") == "cleanup"]
        self.assertIn("steps.isolation.outcome == 'success'", cleanup["if"])
        self.assertNotIn("--volumes", cleanup["run"])
        self.assertIn('for volume in "$JORNADA_SQL_VOLUME_NAME"', cleanup["run"])
        self.assertLess(cleanup["run"].index('[[ "$owner" == "$COMPOSE_PROJECT_NAME" ]]'),
                        cleanup["run"].index('docker volume rm "$volume"'))

    def test_all_compose_named_volumes_are_isolated_per_attempt(self):
        compose = (ROOT / "Solution/docker-compose.yml").read_text(encoding="utf-8")
        job = self.workflow["jobs"]["dev-1k"]
        namespace = "jornada-e2e-${{ github.run_id }}-${{ github.run_attempt }}"
        isolation, = [s for s in self.steps if s.get("id") == "isolation"]
        cleanup, = [s for s in self.steps if s.get("id") == "cleanup"]
        volumes = {
            "JORNADA_SQL_VOLUME_NAME": ("jornada_sql_data", "data"),
            "JORNADA_BRONZE_VOLUME_NAME": ("jornada_bronze", "bronze"),
            "JORNADA_STAGING_1_VOLUME_NAME": ("jornada_staging_1", "staging-1"),
            "JORNADA_STAGING_2_VOLUME_NAME": ("jornada_staging_2", "staging-2"),
            "JORNADA_LOGS_1_VOLUME_NAME": ("jornada_logs_1", "logs-1"),
            "JORNADA_LOGS_2_VOLUME_NAME": ("jornada_logs_2", "logs-2"),
            "JORNADA_NODE_DATA_1_VOLUME_NAME": ("jornada_node_data_1", "node-1"),
            "JORNADA_NODE_DATA_2_VOLUME_NAME": ("jornada_node_data_2", "node-2"),
        }
        for key, (legacy, suffix) in volumes.items():
            with self.subTest(volume=legacy):
                self.assertIn("  " + legacy + ':\n    name: "${' + key + ':-' + legacy + '}"', compose)
                self.assertEqual(job["env"][key], namespace + "-" + suffix)
                self.assertIn('"$' + key + '"', isolation["run"])
                self.assertIn('"$' + key + '"', cleanup["run"])
        self.assertIn('for volume in', isolation["run"])
        self.assertIn('for volume in', cleanup["run"])
        self.assertIn('[[ "$owner" == "$COMPOSE_PROJECT_NAME" ]]', cleanup["run"])
        self.assertIn('docker volume rm "$volume"', cleanup["run"])
        self.assertNotIn("--volumes", cleanup["run"])

    def test_canonical_ibge_generator_is_used_only_for_dev_http_bridge(self):
        # Never replace the historical AA01 1k fixture, or touch scientific seed42.
        import subprocess
        import sys
        job = self.workflow["jobs"]["dev-1k"]
        http, = [s for s in self.steps if s.get("id") == "http"]
        self.assertEqual(http["env"]["JORNADA_E2E_IBGE_1K"], "true")
        self.assertEqual(http["env"]["JORNADA_E2E_HTTP_VOLUME_COUNT"], "1000")
        local = (ROOT / "Solution/scripts/local-e2e.sh").read_text(encoding="utf-8")
        bridge = (ROOT / "Solution/scripts/v9-e2e-ibge-http.sh").read_text(encoding="utf-8")
        verify = (ROOT / "Solution/scripts/v9-e2e-ibge-bridge-preflight.py").read_text(encoding="utf-8")
        evidence = (ROOT / "Solution/scripts/e2e-integral-dev-evidence.py").read_text(encoding="utf-8")
        self.assertIn('source "$ROOT/scripts/v9-e2e-ibge-http.sh"', local)
        self.assertIn("scripts/v9-http-volume-fixture.py", local)
        self.assertIn("generate-ingestion", bridge)
        self.assertIn("Jornada.Linkage.SyntheticCorpus.csproj", bridge)
        self.assertIn('--population-profile demographic-primary', bridge)
        self.assertIn('--people 1000 --seed 355 --error-profile clean', bridge)
        self.assertIn("data/reference/ibge-nomes-2022", bridge)
        self.assertIn("data/reference/synthetic-birth-sp/", bridge)
        self.assertIn("bridge-truth.jsonl", verify)
        self.assertIn('"allowedForScoring") is False', verify)
        self.assertIn('"ibgeSyntheticHttp"', evidence)
        self.assertIn("HASHBYTES('SHA2_256'", bridge)
        self.assertIn('po.codigo_pessoa_origem + NCHAR(10)', bridge)
        self.assertIn("silverSourceIdentitySetsVerified", evidence)
        self.assertIn("SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1", verify)
        self.assertNotIn("TRAIN", bridge)
        self.assertNotIn("TEST", bridge)
        self.assertIn("rm -rf -- .local/e2e/bronze", (ROOT / ".github/workflows/jornada-e2e-integral-v9-dev.yml").read_text(encoding="utf-8"))
        self.assertIn(".local/e2e/ibge-1k", (ROOT / ".github/workflows/jornada-e2e-integral-v9-dev.yml").read_text(encoding="utf-8"))
        subprocess.run(["bash", "-n", str(ROOT / "Solution/scripts/v9-e2e-ibge-http.sh")], check=True)
        subprocess.run([sys.executable, "-m", "unittest",
                        "Solution/tests/scripts/test_v9_e2e_ibge_bridge_preflight.py"],
                       cwd=ROOT, check=True, stdout=subprocess.DEVNULL)

    def test_resultado_api_e2e_receipt_reconciles_payload_and_lots(self):
        import subprocess
        import sys
        local = (ROOT / "Solution/scripts/local-e2e.sh").read_text(encoding="utf-8")
        script = (ROOT / "Solution/scripts/e2e-resultado-entrega.py").read_text(encoding="utf-8")
        evidence = (ROOT / "Solution/scripts/e2e-integral-dev-evidence.py").read_text(encoding="utf-8")
        self.assertIn('"$OUT/resultado-api-evidence.json" "$package"', local)
        self.assertIn('hashlib.sha256()', script)
        self.assertIn('"payloadSha256"', script)
        self.assertIn('"bytesRecebidos"', script)
        self.assertIn('result.get("payloadSha256Verified") is True', evidence)
        self.assertIn('result.get("payloadBytesVerified") is True', evidence)
        self.assertIn('result.get("resumoItensConsistente") is True', evidence)
        for scenario in ('request("tipoBeneficio", filename, 403',
                         'request("cabecalhosAmbiguos", filename, 400'):
            self.assertIn(scenario, script)
        for code in ('"tipoBeneficio": 403', '"cabecalhosAmbiguos": 400'):
            self.assertIn(code, evidence)
        subprocess.run([sys.executable, "-m", "unittest",
                        "Solution/tests/scripts/test_e2e_resultado_entrega_contract.py"],
                       cwd=ROOT, check=True, stdout=subprocess.DEVNULL)

    def test_semiblind_http_contract_separates_complete_from_incomplete(self):
        # #612 is residual SQL selectivity, not a mandatory-name rule.
        # These scenarios guard only the in-memory HTTP serialization contract.
        http = (ROOT / "Solution/tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs").read_text(
            encoding="utf-8")
        for scenario in ("complete_empty", "incomplete_no_pass",
                         "incomplete_fanout", "incomplete_timeout"):
            self.assertIn('[TestCase("' + scenario + '", HttpStatusCode.OK)]', http)
        for field in ("busca_completa", "motivo_incompletude", "nenhumDestes"):
            self.assertIn('GetProperty("' + field + '")', http)
        for reason in ("NoEligiblePass", "FanoutLimitExceeded", "Timeout"):
            self.assertIn("SemiblindRetrievalReasons." + reason, http)
        self.assertIn('payload.TryGetProperty("score", out _)', http)
        self.assertIn('payload.TryGetProperty("pessoaUuid", out _)', http)
        gate = (ROOT / "Solution/src/Jornada.Api/SemiblindSearchActivationGate.cs").read_text(
            encoding="utf-8")
        self.assertIn('!string.IsNullOrWhiteSpace(databaseName)', gate)
        self.assertIn('"Development", StringComparison.Ordinal', gate)

    def test_semiblind_selectivity_sql_probe_is_real_but_optional(self):
        # #612: SQL proof is source-backed, but not a PASS unless physically run.
        source = (ROOT / "Solution/tests/Jornada.Integration.Tests/Integration/"
                  "BlockingProjectionSqlServerTests.cs").read_text(encoding="utf-8")
        self.assertIn("JORNADA_TEST_SQL_CONNECTION", source)
        self.assertIn("new ParsedPackage(manifest, [person, secondPerson], [])", source)
        self.assertIn("SemiblindCandidatePassPlanner.Plan(motherBirthRuleset, noPersonName)", source)
        self.assertIn("maxCandidates: 1", source)
        self.assertIn("MaxCandidatesPerBlock", source)
        self.assertIn('Does.Contain("combined-neighbor-year")', source)
        self.assertIn("SemiblindCandidatePassPlanner.Plan(ruleset, noEvidence)", source)
        self.assertIn("Assert.That(noMatches, Is.Empty)", source)
        # Explicit #612 residual example must be executed against real SQL.
        self.assertIn('"INTEGRATION-SEMIBLIND-FIRST-MOTHER-DOB"', source)
        self.assertIn('LinkageBlockingPass.Create("first-mother-dob"', source)
        for feature in ("BlockingFeatureNames.FirstName",
                        "BlockingFeatureNames.MotherFullName",
                        "BlockingFeatureNames.BirthDay",
                        "BlockingFeatureNames.BirthMonth",
                        "BlockingFeatureNames.BirthYear"):
            self.assertIn(feature, source)
        self.assertIn('"María"', source)
        self.assertIn('"Beatriz"', source)
        self.assertIn("BlockingRuleSetCandidatePlanner.Plan(", source)
        self.assertIn("Does.Not.Contain(\"Beatriz Soares Teste\")", source)
        self.assertIn("Does.Not.Contain(\"María Silva Teste\")", source)
        self.assertIn(".Intersect(preciseBeatriz.Select(p => p.PessoaUuid))", source)
        self.assertNotIn("SemiblindIdentitySearch:Enabled=true", source)

    def test_semiblind_pr_sql_evidence_is_ephemeral_and_non_certifying(self):
        # #612: SQL evidence must use a local Development-only connection, not
        # a new security policy exception for an arbitrary CI workflow.
        workflow = (ROOT / ".github/workflows/jornada-semiblind-sql-pr.yml").read_text(
            encoding="utf-8")
        script = (ROOT / "Solution/scripts/local-semiblind-sql-pr.sh").read_text(
            encoding="utf-8")
        for requirement in (
            "pull_request:",
            "branches: [master]",
            "github.event.pull_request.head.repo.full_name == github.repository",
            "2022-CU26-ubuntu-22.04@sha256:",
            "dotnet restore Jornada.sln --locked-mode",
            "CREATE DATABASE [JornadaSemiblind_Test_${GITHUB_RUN_ID}]",
            "bash scripts/local-semiblind-sql-pr.sh",
            "--forbid-skipped --minimum-tests 1",
            "test-evidence-gate.py",
            '"prHeadSha": os.environ["EXPECTED_PR_HEAD"]',
            '"testedMergeSha": os.environ["GITHUB_SHA"]',
            '"scientificValidation": False',
            '"institutionalAuthorizationValidated": False',
            '"productiveActivation": False',
            "path: Solution/.local/semiblind-sql/receipt.json",
            "JORNADA_RUNTIME_MODE: DEV",
            "DOTNET_ENVIRONMENT: Development",
            "ASPNETCORE_ENVIRONMENT: Development",
        ):
            self.assertIn(requirement, workflow)
        for requirement in (
            "GITHUB_EVENT_NAME:-",
            "GITHUB_REPOSITORY:-",
            "GITHUB_REF:-",
            "JORNADA_RUNTIME_MODE:-",
            "DOTNET_ENVIRONMENT:-",
            "ASPNETCORE_ENVIRONMENT:-",
            '[[ -z "${JORNADA_TEST_SQL_CONNECTION:-}" ]]',
            "Server=127.0.0.1,1433;",
            "TrustServerCertificate" + "=true;Encrypt" + "=false",
            "JornadaSemiblind_Test_${GITHUB_RUN_ID}",
            "BlockingProjectionSqlServerTests.PersistValidated_publishes_blocking_projection_in_same_processor_path",
            "dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj",
        ):
            self.assertIn(requirement, script)
        self.assertNotIn("workflow_dispatch:", workflow)
        self.assertNotIn("pull_request_target:", workflow)
        self.assertNotIn("TrustServerCertificate" + "=true", workflow)
        self.assertNotIn("Encrypt" + "=false", workflow)
        self.assertNotIn("Password=", workflow)
        self.assertNotIn("SemiblindIdentitySearch:Enabled=true", workflow + script)
        self.assertNotIn("Solution/.local/semiblind-sql/semiblind.trx", workflow)
        self.assertNotIn("test_v9_standard", workflow + script)

    def test_semiblind_dev_http_uses_real_service_with_fakes_not_sql_e2e(self):
        # #1854: service/endpoint path is real, SQL retriever and policy are not.
        http = (ROOT / "Solution/tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs").read_text(
            encoding="utf-8")
        self.assertIn("Dev_http_real_service_enforces_person_authorization_and_top_five", http)
        self.assertIn("[TestCase(false)]", http)
        self.assertIn("[TestCase(true)]", http)
        self.assertIn("AddSingleton<ISemiblindIdentitySearchService, SemiblindIdentitySearchService>()", http)
        self.assertIn("new RealServiceHttpFakeRetriever(fixture)", http)
        self.assertIn("new RealServiceHttpPersonPolicy(denied)", http)
        for field in ('"nenhumDestes"', '"busca_completa"', '"motivo_incompletude"',
                      '"candidatos"', '"nome_completo"', '"opcaoId"'):
            self.assertIn("GetProperty(" + field + ")", http)
        self.assertIn("new SemiblindIdentitySearchRequest(\n                    null,", http)
        self.assertIn("Assert.That(audit.Successful, Is.EqualTo(1)", http)
        self.assertIn("Is.EquivalentTo(new[]", http)
        self.assertIn("Does.Not.Contain(person.PessoaUuid.ToString())", http)
        self.assertNotIn("JORNADA_TEST_SQL_CONNECTION", http)
        self.assertNotIn("SemiblindIdentitySearch:Enabled=true", http)

    def test_semiblind_real_service_http_pr_proof_is_two_cases_and_no_sql(self):
        # The fast unit-runtime job compiles this TestHost fixture, but does
        # NOT run it; this independent workflow requires 2 physical HTTP tests.
        source = (ROOT / ".github/workflows/jornada-semiblind-http-pr.yml").read_text(
            encoding="utf-8")
        for expected in (
            "pull_request:",
            "branches: [master]",
            "github.event.pull_request.head.repo.full_name == github.repository",
            "EXPECTED_PR_HEAD:",
            "dotnet restore Jornada.sln --locked-mode",
            "Dev_http_real_service_enforces_person_authorization_and_top_five",
            "--forbid-skipped --minimum-tests 2",
            'exact = {"total": 2, "passed": 2, "failed": 0, "skipped": 0, "other": 0}',
            '"realSqlRetrieverValidated": False',
            '"institutionalAuthorizationValidated": False',
            '"productiveActivation": False',
            "path: Solution/.local/semiblind-http/receipt.json",
        ):
            self.assertIn(expected, source)
        self.assertNotIn("workflow_dispatch:", source)
        self.assertNotIn("pull_request_target:", source)
        self.assertNotIn("JORNADA_TEST_SQL_CONNECTION", source)
        self.assertNotIn("Solution/.local/semiblind-http/semiblind-http.trx\n          if-no-files-found", source)

    def test_e2e_receipt_rejects_ambiguous_nonfinite_symlink_and_oversized_json(self):
        # A duplicated PASS key must never hide an earlier FAIL key.
        import importlib.util
        import tempfile

        path = ROOT / "Solution/scripts/e2e-integral-dev-evidence.py"
        spec = importlib.util.spec_from_file_location("integral_dev_evidence_adversarial", path)
        evidence = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(evidence)
        with tempfile.TemporaryDirectory() as temp:
            receipt = Path(temp, "receipt.json")
            self.assertEqual(evidence.read(receipt), {})
            for payload in (
                '{"status":"FAIL","status":"PASS"}',
                '{"cases":[{"auditEvents":0,"auditEvents":1}]}',
                '{"e2eElapsedMs":NaN}',
                '{"e2eElapsedMs":Infinity}',
                "[]",
            ):
                with self.subTest(payload=payload):
                    receipt.write_text(payload, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        evidence.read(receipt)
            receipt.write_text('{"status":"PASS"}', encoding="utf-8")
            self.assertEqual(evidence.read(receipt), {"status": "PASS"})
            alias = Path(temp, "alias.json")
            alias.symlink_to(receipt)
            with self.assertRaises(ValueError):
                evidence.read(alias)
            receipt.write_text(" " * (evidence.MAX_RECEIPT_BYTES + 1), encoding="utf-8")
            with self.assertRaises(ValueError):
                evidence.read(receipt)

    def test_missing_receipts_never_approve_the_milestones(self):
        import importlib.util
        import tempfile
        from unittest.mock import patch

        script = ROOT / "Solution/scripts/e2e-integral-dev-evidence.py"
        spec = importlib.util.spec_from_file_location("integral_dev_evidence", script)
        evidence = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(evidence)
        sha = "a" * 40
        with tempfile.TemporaryDirectory() as temp:
            env = {key + "_STEP": "success" for key in (
                "ADMISSION", "CHECKOUT", "ISOLATION", "HTTP", "CONTRACTS", "APOIO", "RUNTIME", "CLEANUP")}
            env.update(RUNNER_TEMP=temp, GITHUB_SHA=sha, GITHUB_RUN_ID="1", GITHUB_RUN_ATTEMPT="2",
                       GITHUB_JOB="dev-1k", GITHUB_REF="refs/heads/master", GITHUB_EVENT_NAME="workflow_dispatch",
                       GITHUB_REPOSITORY="lucianox777/Jornada", GITHUB_STEP_SUMMARY=str(Path(temp, "summary.md")))
            with patch.dict("os.environ", env), patch.object(evidence, "OUT", Path(temp)):
                self.assertEqual(evidence.main(), 1)
            receipt = json.loads(Path(temp, "e2e-integral-dev.json").read_text())
            self.assertFalse(receipt["E2E_NUCLEO_PASS"])
            self.assertFalse(receipt["E2E_INTEGRAL_PASS"])
            self.assertTrue(receipt["failedOrMissing"])
            rows = {row.get("measurement"): row for row in receipt["coverage"]}
            for measurement in ("counts", "ibgeSyntheticHttp", "resultadoHttp", "semiblindHttpDenials"):
                self.assertEqual(rows[measurement]["status"], "NAO_VALIDADO")
            self.assertTrue(all(row["sha"] == sha for row in receipt["coverage"]))
            self.assertEqual({row["scope"] for row in receipt["coverage"] if row["status"] == "BLOQUEADO"},
                             {"nucleo", "integral"})
            # #612 is residual selectivity/completeness, not mandatory-name policy;
            # #539 is the separate institution/attribute visibility gate.
            integral = [row for row in receipt["coverage"]
                        if row.get("scope") == "integral" and row["status"] == "BLOQUEADO"]
            self.assertTrue(any("#1854/#612" in row["reason"]
                                and "busca_completa" in row["reason"] for row in integral))
            self.assertTrue(any("#1854/#539/#378" in row["reason"]
                                and "pessoa restrita" in row["reason"] for row in integral))
            self.assertFalse(any("#1348" in row["reason"] for row in integral))

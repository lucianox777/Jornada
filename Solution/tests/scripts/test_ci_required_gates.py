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
    assert "github.event_name == 'pull_request'" in body
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
        assert "runs-on: ubuntu-latest" in item
        assert "name: nuget-lockfiles" in item
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

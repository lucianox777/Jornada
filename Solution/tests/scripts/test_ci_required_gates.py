#!/usr/bin/env python3
"""Fail closed if the CI refactor silently removes mandatory validation jobs."""
from pathlib import Path
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

def test_dt10_reusable_job_has_same_gate_and_real_sql_acceptance():
    """DT10 moves to a reusable job without erasing the mandatory CI contract."""
    caller = WORKFLOW.read_text(encoding="utf-8")
    reusable = ROOT / ".github/workflows/dt10-evidence.yml"
    assert reusable.is_file(), "DT10 reusable workflow removed"
    evidence = reusable.read_text(encoding="utf-8")
    block = re.search(
        r"(?ms)^  dt10-evidence:\\s*\\n(.*?)(?=^  [a-z][a-z0-9-]*:\\s*$|\\Z)",
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
    classifier = (ROOT / "Solution/scripts/ci-impact-classifier.py").read_text(encoding="utf-8")
    assert '".github/workflows/dt10-evidence.yml"' in classifier, (
        "changes to reusable DT10 must trigger its SQL evidence gate"
    )


import unittest

class CiGateContractTests(unittest.TestCase):
    def test_required_jobs(self):
        test_required_ci_jobs_present()

    def test_dependencies(self):
        test_full_gate_is_fail_closed()

    def test_dt10_reusable(self):
        test_dt10_reusable_job_has_same_gate_and_real_sql_acceptance()

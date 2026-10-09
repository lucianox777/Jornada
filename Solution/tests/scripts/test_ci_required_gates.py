#!/usr/bin/env python3
"""Fail closed if the CI refactor silently removes mandatory validation jobs."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
REQUIRED = (
    "impact", "dependency-lock", "ddl-upgrade", "unit",
    "security-analysis", "harness-smoke", "e2e",
    "deterministic-build", "integration-sql",
)


def test_required_ci_jobs_present():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert re.search(r"(?m)^jobs:\\s*$", workflow)
    names = set(re.findall(r"(?m)^  ([a-z][a-z0-9-]*):\\s*$", workflow))
    missing = set(REQUIRED) - names
    assert not missing, f"Required CI jobs removed: {sorted(missing)}"


def test_full_gate_is_fail_closed():
    workflow = WORKFLOW.read_text(encoding="utf-8")
    for name in REQUIRED[1:]:
        block = re.search(
            rf"(?ms)^  {re.escape(name)}:\\s*\\n(.*?)(?=^  [a-z][a-z0-9-]*:\\s*$|\\Z)",
            workflow,
        )
        assert block, f"Missing CI job: {name}"
        assert "needs:" in block.group(1), f"CI job {name} must declare dependencies"

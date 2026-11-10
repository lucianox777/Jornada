#!/usr/bin/env python3
"""A2 black-box subprocess canaries: fail before SQL/Gold/TEST on bad arity.

These complement NUnit fault-injection (which embeds the canary in the
exception itself). Every public CLI route and the host bootstrap must exit
nonzero and emit ONLY a fixed error code, without stack/args/secrets.
"""
from __future__ import annotations
import re
import subprocess
import sys
from pathlib import Path

CANARY = "SENHA_CANARY_2026_CPF_11122233344_DB_PASSWORD_SECRET"
FLAGS = (
    "--v9-tf-export-ibge-pin",
    "--v9-prepare-gold-runner",
    "--v9-calibrate",
    "--v9-lite-benchmark",
    "--v9-validate-finalists",
    "--v9-compare-calibration-plans",
    "--v9-adaptive-study",
    "--v9-adaptive-census",
    "--v9-frozen-transfer",
    "--v9-audit-ref-inventory",
    "--v9-audit-ref-frozen-snapshot",
    "--v9-derive-joint-v3-from-frozen-train",
    "--v9-preflight-frozen-v2-v3-pair",
    "--v9-compare-frozen-v2-v3-independent",
    "--v9-gold-frozen-tf-preflight",
    "--v9-inspect-fitted-snapshot",
    "--v9-physical-probe-seed42",
)
SAFE = re.compile(
    r"V9_CALIBRATOR_FAILED:(?:INPUT_NOT_FOUND|INVALID_DATA|IO_ERROR|INVALID_ARGUMENTS|UNEXPECTED)\n?",
    re.ASCII,
)


def run_one(dll: Path, label: str, args: list[str]) -> None:
    try:
        proc = subprocess.run(
            ["dotnet", str(dll), *args], text=True, encoding="utf-8",
            errors="replace", capture_output=True, timeout=20, check=False,
            env=__import__("os").environ | {"DOTNET_NOLOGO": "1"},
        )
    except subprocess.TimeoutExpired as error:
        raise AssertionError(f"{label}: timeout during fail-closed preflight") from error
    if proc.returncode == 0:
        raise AssertionError(f"{label}: failure path incorrectly returned exit code 0")
    if not SAFE.fullmatch(proc.stderr):
        # Do NOT copy raw stderr to logs: an actual leak is sensitive.
        raise AssertionError(
            f"{label}: stderr was not exactly one fixed safe code "
            f"(length={len(proc.stderr)}, lines={len(proc.stderr.splitlines())})")
    if CANARY in proc.stderr or CANARY in proc.stdout:
        raise AssertionError(f"{label}: secret/CPF canary leaked in public output")
    if proc.stdout:
        # No success-output is expected before these argument/host guards.
        raise AssertionError(f"{label}: unexpected stdout before fail-closed guard")
    print(f"PASS: {label} nonzero + safe-code-only")


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: test_v9_worker_public_cli_process_canaries.py <worker.dll>")
    dll = Path(sys.argv[1]).resolve(strict=True)
    for flag in FLAGS:
        # Two arguments cause a deliberate arity failure in each command
        # before any SQL, file loading, Gold, or TEST access can begin.
        run_one(dll, flag, [flag, CANARY])
    # Host fallback: valid connection-string configuration with an unknown
    # provider must fail in the startup adapter factory, before connection.
    run_one(dll, "HOST_DEFAULT_NO_CLI_FLAG", [
        "--ConnectionStrings:Jornada=" + CANARY,
        "--Database:Provider=UNRECOGNIZED_PROVIDER",
    ])
    print(f"PASS: {len(FLAGS) + 1} real CLI/host process canaries")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

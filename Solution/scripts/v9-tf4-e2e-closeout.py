#!/usr/bin/env python3
"""Fail-closed physical E2E closeout for the manual V9 100k workflow.

Runs only AFTER separate TRAIN/AUDIT/VALIDATION jobs. On an optional TEST
request it requires a passing independent TEST with demonstrated full
production-runner parity. This script never opens corpus, FIT or secrets.
A skipped scientific stage is NEVER translated into scientific SUCCESS.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

HEX64 = re.compile(r"[0-9a-f]{64}\Z")
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
RESULTS = {"success", "failure", "cancelled", "skipped"}


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise ValueError("V9_E2E_CLOSEOUT_" + reason)


def validate_jobs(
    physical: str, audit: str, validation: str,
    preparation: str, test: str, test_requested: bool,
) -> None:
    require(all(x in RESULTS for x in (
        physical, audit, validation, preparation, test,
    )), "UNRECOGNIZED_JOB_STATUS")
    require(physical == "success", "TRAIN_NOT_SUCCESS")
    require(audit == "success", "AUDIT_NOT_SUCCESS")
    require(validation == "success", "VALIDATION_NOT_SUCCESS")
    if test_requested:
        require(preparation == "success", "TEST_PREPARATION_NOT_SUCCESS")
        require(test == "success", "TEST_NOT_SUCCESS")
    else:
        require(preparation == "skipped", "UNREQUESTED_TEST_PREPARATION")
        require(test == "skipped", "UNREQUESTED_TEST_EXPOSURE")


def limited_json(path: Path) -> dict:
    require(path.is_file() and not path.is_symlink(), "MISSING_RECEIPT")
    require(path.stat().st_size <= 8 * 1024 * 1024, "RECEIPT_TOO_LARGE")
    doc = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(doc, dict), "INVALID_RECEIPT_TYPE")
    return doc


def validate_scientific_validation(path: Path) -> dict:
    r = limited_json(path)
    require(r.get("testUsed") is False
            and r.get("refitted") is False
            and r.get("certified") is False
            and r.get("activated") is False,
            "VALIDATION_SCIENTIFIC_INVARIANTS")
    require(isinstance(r.get("selectionEnvelopeSha256"), str)
            and HEX64.fullmatch(r["selectionEnvelopeSha256"]) is not None,
            "VALIDATION_ENVELOPE_SHA")
    n = r.get("comparedConfigurations")
    require(type(n) is int and 1 <= n <= 3
            and type(r.get("measured")) is list
            and len(r["measured"]) == n,
            "VALIDATION_FINALIST_CARDINALITY")
    status = r.get("status")
    winner = r.get("selectedValidationSnapshotSha256")
    require(status in {
        "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED",
        "NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES",
    }, "VALIDATION_STATUS_UNKNOWN")
    if status == "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED":
        require(isinstance(winner, str)
                and HEX64.fullmatch(winner) is not None,
                "VALIDATION_WINNER_SHA")
    else:
        require(winner is None, "VALIDATION_WINNER_STATUS_CONFLICT")
    return {
        "validationStatus": status, "winnerSha256": winner,
        "validationFinalists": n,
    }


def validate_optional_test(path: Path, winner_sha: str | None) -> None:
    require(isinstance(winner_sha, str)
            and HEX64.fullmatch(winner_sha) is not None,
            "TEST_REQUIRES_VALIDATION_WINNER")
    r = limited_json(path)
    require(r.get("schema") == "V9_TF4_INDEPENDENT_TEST_ONE_WINNER_PHYSICAL_V1"
            and r.get("winnerSnapshotSha256") == winner_sha,
            "TEST_WINNER_CONTRACT_DRIFT")
    require(r.get("operationalModelContract")
            == "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1"
            and r.get("operationalModelByteExactRoundTrip") is True
            and r.get("operationalModelActive") is False
            and r.get("operationalModelPublished") is False,
            "TEST_MODEL_NOT_BYTE_IDENTICAL_OR_NONPUBLISHING")
    require(r.get("trainPlanRead") is False
            and r.get("validationResultsRead") is False
            and r.get("refitted") is False
            and r.get("certified") is False
            and r.get("activated") is False
            and r.get("testUsed") is True
            and r.get("configurationsEvaluated") == 1,
            "TEST_INDEPENDENCE_INVARIANTS")
    # False in the current V9IndependentTestCli: a SQL round-trip and a
    # nonpublishing synthetic rehearsal are not full production RunAsync parity.
    require(r.get("productionRunnerFullPipelineParityCertified") is True,
            "PRODUCTION_RUNASYNC_PARITY_UNPROVEN")


def closeout(
    physical: str, audit: str, validation: str,
    preparation: str, test: str, test_requested: bool,
    validation_file: Path, test_file: Path | None,
) -> dict:
    validate_jobs(physical, audit, validation, preparation, test,
                  test_requested)
    info = validate_scientific_validation(validation_file)
    if test_requested:
        require(test_file is not None, "TEST_RECEIPT_NOT_GIVEN")
        validate_optional_test(test_file, info["winnerSha256"])
        status = "E2E_PHYSICAL_CHAIN_AND_PRODUCTION_EQUIVALENT_TEST_COMPLETE"
    else:
        require(test_file is None, "UNREQUESTED_TEST_RECEIPT")
        status = ("E2E_PHYSICAL_CORE_FINISHED_WITH_WINNER_TEST_NOT_REQUESTED"
                  if info["winnerSha256"] else
                  "E2E_PHYSICAL_CORE_FINISHED_NO_WINNER_TEST_NOT_REQUESTED")
    return {"schema": "V9_TF4_E2E_CLOSEOUT_V1", "status": status,
            "testRequested": test_requested, "trainResult": physical,
            "auditResult": audit, "validationResult": validation,
            "testPreparationResult": preparation, "testResult": test,
            **info, "certified": False, "activated": False,
            "productionPublication": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("physical", "audit", "validation", "preparation", "test"):
        parser.add_argument("--" + name, required=True, choices=sorted(RESULTS))
    parser.add_argument("--test-requested", choices=["true", "false"],
                        required=True)
    parser.add_argument("--validation-file", type=Path, required=True)
    parser.add_argument("--test-file", type=Path)
    parser.add_argument("--code-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    # Only one bounded, sanitized receipt is written (no private rows or truth).
    require(HEX40.fullmatch(args.code_sha or "") is not None,
            "CODE_SHA_INVALID")
    require(args.run_id > 0, "RUN_ID_INVALID")
    info = {"schema": "V9_TF4_E2E_CLOSEOUT_V1",
            "codeSha": args.code_sha, "workflowRunId": args.run_id,
            "testRequested": args.test_requested == "true",
            "certified": False, "activated": False,
            "productionPublication": False}
    exitcode = 0
    try:
        result = closeout(
            args.physical, args.audit, args.validation, args.preparation,
            args.test, args.test_requested == "true",
            args.validation_file, args.test_file,
        )
        info.update(result)
    except (ValueError, json.JSONDecodeError, OSError) as exc:
        info.update(status="E2E_INCOMPLETE_FAIL_CLOSED",
                    reason=str(exc).split(":")[0][:100])
        exitcode = 1
    require(not args.out.exists(), "OUTPUT_ALREADY_EXISTS")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(info, sort_keys=True, indent=2) + "\n",
                        encoding="utf-8")
    print("V9_E2E_CLOSEOUT " + info["status"] +
          (" " + info.get("reason", "") if exitcode else ""))
    return exitcode


if __name__ == "__main__":
    sys.exit(main())

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
    require(r.get("schema") == "V9_VALIDATION_E2E_STATUS_V1",
            "VALIDATION_RECEIPT_SCHEMA")
    require(isinstance(r.get("selectionEnvelopeSha256"), str)
            and HEX64.fullmatch(r["selectionEnvelopeSha256"]) is not None,
            "VALIDATION_ENVELOPE_SHA")
    require(isinstance(r.get("validationResultSha256"), str)
            and HEX64.fullmatch(r["validationResultSha256"]) is not None,
            "VALIDATION_RESULT_SHA")
    n = r.get("comparedConfigurations")
    require(type(n) is int and 1 <= n <= 3
            and type(r.get("measuredCount")) is int
            and r["measuredCount"] == n,
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
        require(r.get("selectedValidationOperationalModelFileName")
                == winner + ".operational.json"
                and r.get("selectedValidationOperationalModelContract")
                == "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1",
                "VALIDATION_OPERATIONAL_MODEL_MISSING_OR_MISMATCHED")
    else:
        require(winner is None
                and r.get("selectedValidationOperationalModelFileName") is None,
                "VALIDATION_WINNER_STATUS_CONFLICT")
    return {
        "validationStatus": status, "winnerSha256": winner,
        "operationalModelFileName": r.get("selectedValidationOperationalModelFileName"),
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
            and r.get("operationalModelPublished") is False
            and r.get("validatedOperationalModelFileName")
                == winner_sha + ".operational.json"
            and r.get("validatedOperationalJsonSchema")
                == "V9_FITTED_SNAPSHOT_V3"
            and r.get("sameOperationalJsonBytesAsProductionSideSqlPayload") is True,
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


def validate_existing_database_diagnostic(
    path: Path, database_name: str, winner_sha: str | None,
) -> dict:
    """This is operational-only: never turn unlabelled rows into scientific TEST."""
    require(isinstance(winner_sha, str) and HEX64.fullmatch(winner_sha),
            "EXTERNAL_DB_REQUIRES_VALIDATION_WINNER")
    require(bool(re.fullmatch(r"[A-Za-z][A-Za-z0-9_]{0,127}", database_name))
            and database_name.lower()
                not in {"master", "tempdb", "model", "msdb", "jornadaharness"},
            "EXTERNAL_DB_INVALID")
    r = limited_json(path)
    require(r.get("schema") == "V9_EXTERNAL_DATABASE_READONLY_OPERATIONAL_DIAGNOSTIC_V1"
            and r.get("status")
                == "EXTERNAL_DATABASE_NONPUBLISHING_DIAGNOSTIC_NOT_SCIENTIFIC_TEST"
            and r.get("databaseName") == database_name
            and r.get("databaseOrigin")
                == "EXISTING_DATABASE_NO_SYNTHETIC_GENERATION"
            and r.get("winnerSnapshotSha256") == winner_sha
            and r.get("operationalModelFileName")
                == winner_sha + ".operational.json"
            and r.get("operationalModelContract")
                == "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1",
            "EXTERNAL_DB_MODEL_OR_DATA_ORIGIN_MISMATCH")
    n = r.get("observationCount")
    require(type(n) is int and 2 <= n <= 100000
            and type(r.get("candidateEdgeCount")) is int
            and 0 <= r["candidateEdgeCount"] <= 2000000
            and type(r.get("resolved")) is int
            and type(r.get("unresolved")) is int
            and type(r.get("conflicts")) is int
            and r["resolved"] + r["unresolved"] + r["conflicts"] == n
            and isinstance(r.get("aggregateSha256"), str)
            and HEX64.fullmatch(r["aggregateSha256"]),
            "EXTERNAL_DB_AGGREGATES_INVALID")
    require(all(r.get(field) is value for field, value in {
        "inputReadOnly": True,
        "generatedSyntheticData": False,
        "independentScientificTest": False,
        "goldTruthAvailable": False,
        "tpFpFnFdrRecallEvaluated": False,
        "candidateGraphParityWithProductionCertified": False,
        "productionRunnerFullPipelineParityCertified": False,
        "operationallyCertified": False,
        "testUsed": False,
        "refitted": False,
        "published": False,
        "activated": False,
    }.items()), "EXTERNAL_DB_FALSE_CERTIFICATION_OR_WRITE")
    return {"existingDatabaseName": database_name,
            "externalDiagnosticObservationCount": n,
            "externalDiagnosticResult": "success",
            "scientificTestExecuted": False}


def closeout(
    physical: str, audit: str, validation: str,
    preparation: str, test: str, test_requested: bool,
    validation_file: Path, test_file: Path | None,
    external_database: str | None = None,
    external_preflight: str = "skipped",
    external_test: str = "skipped",
    external_file: Path | None = None,
) -> dict:
    if external_database:
        require(test_requested, "EXTERNAL_DB_REQUIRES_TEST_TOGGLE")
        require(preparation == "skipped" and test == "skipped",
                "EXTERNAL_DB_MUST_NOT_EXPOSE_SYNTHETIC_TEST")
        require(external_preflight == "success" and external_test == "success",
                "EXTERNAL_DB_JOB_NOT_SUCCESS")
        require(physical == "success" and audit == "success"
                and validation == "success", "MANDATORY_SCIENTIFIC_STAGE_NOT_SUCCESS")
        require(test_file is None and external_file is not None,
                "EXTERNAL_DB_RECEIPT_NOT_GIVEN")
        info = validate_scientific_validation(validation_file)
        detail = validate_existing_database_diagnostic(
            external_file, external_database, info["winnerSha256"])
        return {"schema": "V9_TF4_E2E_CLOSEOUT_V1",
                "status": "E2E_CORE_WITH_EXISTING_DB_READONLY_DIAGNOSTIC_NOT_SCIENTIFIC_TEST",
                "testRequested": True, "testMode": "EXISTING_DATABASE_READONLY",
                "trainResult": physical, "auditResult": audit,
                "validationResult": validation, "testPreparationResult": preparation,
                "testResult": test, "externalPreflightResult": external_preflight,
                "externalTestResult": external_test,
                **info, **detail, "certified": False, "activated": False,
                "productionPublication": False}
    require(external_preflight == "skipped" and external_test == "skipped"
            and external_file is None,
            "UNREQUESTED_EXTERNAL_DATABASE_ACCESS")
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
    parser.add_argument("--external-database")
    parser.add_argument("--external-preflight", choices=sorted(RESULTS),
                        default="skipped")
    parser.add_argument("--external-test", choices=sorted(RESULTS),
                        default="skipped")
    parser.add_argument("--external-file", type=Path)
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
            args.external_database, args.external_preflight,
            args.external_test, args.external_file,
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

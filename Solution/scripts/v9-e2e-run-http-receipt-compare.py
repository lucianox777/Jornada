#!/usr/bin/env python3
"""Fail-closed, offline comparison of a V9 SQL run ledger and HTTP ZIP receipts.

Neither receipt is cryptographically authenticated by this verifier. In
particular, matching two supplied JSON files is NEVER an E2E certification.
The SQL ledger must be produced by the separate TEST_ONLY runtime method.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import uuid


SCRIPTS = Path(__file__).resolve().parent


def imported(name, filename):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


LINEAGE = imported("v9_ibge_http_lineage_runjoin", "v9-e2e-ibge-http-lineage.py")
ORACLE = imported("v9_grouping_oracle_runjoin", "e2e-v9-grouping-oracle.py")
SQL_SCHEMA = "E2E_V9_RUN_DELIVERY_SQL_LEDGER_TEST_ONLY_V1"
OUTPUT_SCHEMA = "E2E_V9_RUN_HTTP_RECEIPTS_COMPARISON_DEV_V1"
SQL_FIELDS = {
    "schema", "status", "runId", "commitSha", "modelWinnerSha256",
    "observationCount", "sourceSetDigestMethod", "sourceSetSha256Utf16le",
    "deliveryCount", "deliveryProofs", "sqlRunTerminalVerified",
    "httpDeliveryProven", "goldIdentityStatesAvailable", "certified",
    "E2E_NUCLEO_PASS", "E2E_INTEGRAL_PASS",
}
DELIVERY_FIELDS = {
    "gestor", "deliveryIdSha256", "packageSha256", "zipBytes", "silverPersons",
    "silverSourceSetSha256Utf16le", "sqlPayloadMetadataVerified",
    "sourceDeliveryIdMatches",
}
STAGE_FIELDS = {
    "schema", "status", "runId", "commitSha", "modelWinnerSha256",
    "observationCount", "sourceSetDigestMethod", "sourceSetSha256Utf16le",
    "identityStatesAvailable", "certified", "E2E_NUCLEO_PASS",
    "E2E_INTEGRAL_PASS", "rows",
}


def require(value, reason):
    if not value:
        raise ValueError(reason)


def canonical_uuid(value):
    if not isinstance(value, str):
        return False
    try:
        return str(uuid.UUID(value)) == value and value != str(uuid.UUID(int=0))
    except ValueError:
        return False


def comparable(proof):
    return {name: proof[name] for name in (
        "gestor", "deliveryIdSha256", "packageSha256", "zipBytes",
        "silverPersons", "silverSourceSetSha256Utf16le")}


def compare(preflight, http, sql, staging, commit, winner):
    require(isinstance(preflight, dict) and isinstance(http, dict)
            and isinstance(sql, dict) and isinstance(staging, dict),
            "required four independent receipt objects")
    require(LINEAGE.verify(preflight, http, commit) is True,
            "HTTP four-manager receipt rejected")
    require(isinstance(winner, str)
            and re.fullmatch("[0-9A-F]{64}", winner) is not None,
            "expected pinned winner digest must be uppercase SHA256")

    require(set(sql) == SQL_FIELDS and sql["schema"] == SQL_SCHEMA
            and sql["status"] == "SQL_RUN_LEDGER_DIAGNOSTIC_NOT_CERTIFIED"
            and sql["sqlRunTerminalVerified"] is True
            and sql["httpDeliveryProven"] is False
            and sql["goldIdentityStatesAvailable"] is False
            and sql["certified"] is False
            and sql["E2E_NUCLEO_PASS"] is False
            and sql["E2E_INTEGRAL_PASS"] is False,
            "SQL ledger shape or noncertification flags invalid")
    require(set(staging) == STAGE_FIELDS
            and staging["schema"] == ORACLE.STAGING_SCHEMA_V2
            and staging["status"] == "STAGING_NOT_CERTIFIED"
            and staging["identityStatesAvailable"] is False
            and staging["certified"] is False
            and staging["E2E_NUCLEO_PASS"] is False
            and staging["E2E_INTEGRAL_PASS"] is False,
            "V9 staging version or noncertification flags invalid")
    run_id = sql["runId"]
    require(canonical_uuid(run_id) and staging["runId"] == run_id,
            "staging and SQL run ID diverged")
    require(sql["commitSha"] == staging["commitSha"] == http["commitSha"] == commit
            and sql["modelWinnerSha256"] == staging["modelWinnerSha256"] == winner,
            "SQL/staging/HTTP checkout or selected model differ")
    require(sql["sourceSetDigestMethod"] == staging["sourceSetDigestMethod"]
            == LINEAGE.METHOD,
            "source digest method mismatch")
    sources = (sql["sourceSetSha256Utf16le"], staging["sourceSetSha256Utf16le"],
               http["sourceSetSha256Utf16le"], preflight["sourceSetSha256Utf16le"])
    require(all(LINEAGE.digest(value) for value in sources)
            and len(set(sources)) == 1,
            "frozen SQL/staging/HTTP/ZIP source sets differ")
    rows = staging["rows"]
    count = preflight["sourceObservations"]
    require(type(sql["observationCount"]) is int
            and type(staging["observationCount"]) is int
            and sql["observationCount"] == staging["observationCount"] == count
            and isinstance(rows, list) and len(rows) == count,
            "run observation coverage invalid")
    observed_sources = set()
    for row in rows:
        require(isinstance(row, dict) and set(row) == {"opaquePersonId", "groupId"}
                and isinstance(row["opaquePersonId"], str)
                and re.fullmatch(r"SYNTH-[0-9A-F]{32}", row["opaquePersonId"])
                and row["opaquePersonId"] not in observed_sources
                and canonical_uuid(row["groupId"]),
                "invalid, repeated, or noncanonical runtime staging source")
        observed_sources.add(row["opaquePersonId"])
    expected_digest = hashlib.sha256(
        "".join(source + "\n" for source in sorted(observed_sources))
        .encode("utf-16le")).hexdigest().upper()
    require(expected_digest == sources[0], "staging source rows not bound to SQL ledger")
    actual_proofs = sql["deliveryProofs"]
    http_proofs = http["deliveryProofs"]
    require(type(sql["deliveryCount"]) is int
            and sql["deliveryCount"] == len(LINEAGE.MANAGERS)
            and isinstance(actual_proofs, list)
            and len(actual_proofs) == len(http_proofs) == len(LINEAGE.MANAGERS),
            "run does not cover all four physical HTTP deliveries")
    keyed_sql = {}
    total = 0
    for proof in actual_proofs:
        require(isinstance(proof, dict) and set(proof) == DELIVERY_FIELDS,
                "SQL run delivery proof malformed")
        manager = proof["gestor"]
        require(manager in LINEAGE.MANAGERS and manager not in keyed_sql
                and LINEAGE.digest(proof["deliveryIdSha256"])
                and LINEAGE.digest(proof["packageSha256"])
                and LINEAGE.digest(proof["silverSourceSetSha256Utf16le"])
                and type(proof["zipBytes"]) is int and proof["zipBytes"] > 0
                and type(proof["silverPersons"]) is int and proof["silverPersons"] > 0
                and proof["sqlPayloadMetadataVerified"] is True
                and proof["sourceDeliveryIdMatches"] is True,
                "SQL run lacks verified HTTP delivery/observation metadata")
        keyed_sql[manager] = proof
        total += proof["silverPersons"]
    require(total == count and set(keyed_sql) == set(LINEAGE.MANAGERS),
            "SQL run partial or duplicate delivery coverage")
    for proof in http_proofs:
        manager = proof["gestor"]
        require(comparable(keyed_sql[manager]) == comparable(proof),
                "SQL frozen run did not consume exactly the HTTP delivery receipt")
    return {
        "schema": OUTPUT_SCHEMA,
        "status": "RECEIPT_FIELDS_MATCHED_NOT_AUTHENTICATED",
        "scope": "READ_ONLY_RUN_VS_HTTP_RECEIPTS_COMPARISON_NOT_PHYSICAL_CERTIFICATION",
        "runIdSha256": hashlib.sha256(run_id.encode("ascii")).hexdigest().upper(),
        "commitSha": commit, "modelWinnerSha256": winner,
        "sourceSetSha256Utf16le": expected_digest,
        "sourceObservations": count,
        "deliveryComparisons": len(LINEAGE.MANAGERS),
        "receiptsFieldConsistencyVerified": True,
        "sourceSqlRuntimeAuthenticatedExternally": False,
        "httpRunExecutionAuthenticatedExternally": False,
        "goldIdentityStatesAvailable": False,
        "scientificValidation": False,
        "operationalParityCertified": False,
        "activated": False,
        "E2E_NUCLEO_PASS": False,
        "E2E_INTEGRAL_PASS": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preflight", type=Path, required=True)
    parser.add_argument("--http", type=Path, required=True)
    parser.add_argument("--sql-ledger", type=Path, required=True)
    parser.add_argument("--staging", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-winner", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    inputs = (args.preflight, args.http, args.sql_ledger, args.staging)
    require(len({p.resolve() for p in inputs + (args.receipt,)}) == 5
            and not args.receipt.exists() and not args.receipt.is_symlink(),
            "cannot overwrite or alias run/HTTP receipts")
    data = [LINEAGE.load(p) for p in inputs]
    result = compare(*data, args.expected_sha, args.expected_winner)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    with args.receipt.open("x", encoding="utf-8") as target:
        target.write(json.dumps(result, indent=2, sort_keys=True) + "\n")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Offline joint DEV E2E evidence: ZIP bytes + independent truth + HTTP/SQL.

Combines two separately fail-closed validators on the SAME IBGE root, staging,
run, checkout and pinned winner. Never authenticates input producers, observes
Gold, sends HTTP, reads production data or certifies productive V9.
"""
import argparse
import json
from pathlib import Path
import importlib.util


def sibling(name, script):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(script))
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


BRIDGE = sibling("v9_composite_bridge", "e2e-v9-bridge-staging-reconcile.py")
RUN = sibling("v9_composite_run_http", "v9-e2e-run-http-receipt-compare.py")
SCHEMA = "E2E_V9_IBGE_ALL_RECEIPTS_COMPOSITE_DEV_V1"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def combine(root, http_path, sql_path, staging_path, expected_sha, expected_winner):
    """Compare only independently supplied receipts; never claim actual E2E."""
    root, http_path, sql_path, staging_path = (
        Path(root), Path(http_path), Path(sql_path), Path(staging_path))
    inputs = (http_path, sql_path, staging_path)
    require(root.is_dir() and not root.is_symlink(), "missing bridge root")
    require(all(p.is_file() and not p.is_symlink()
                and not p.resolve().is_relative_to(root.resolve()) for p in inputs),
            "HTTP/SQL/staging must be outside the private bridge generator tree")
    require(len({p.resolve() for p in inputs}) == 3,
            "HTTP/SQL/staging inputs must be distinct physical files")

    # First: read all real ZIP bytes, validate complete truth sidecar and run
    # the independent grouping oracle (no generated truth ever enters scorer).
    grouping = BRIDGE.reconcile(root, staging_path,
                                expected_sha, expected_winner)
    require(grouping["schema"] == "E2E_V9_IBGE_BRIDGE_GROUPING_RECONCILIATION_DEV_V1"
            and grouping["status"] == "RECONCILED_OFFLINE_NOT_CERTIFIED"
            and grouping["bridgeZipBytesAndSourceSetVerified"] is True
            and grouping["independentSqlToZipSourceSetDigestMatched"] is True
            and grouping["identityStatesAvailable"] is False
            and grouping["linkageSqlRunProvenanceCertified"] is False
            and grouping["bridgeToSqlHttpDeliveryProven"] is False
            and grouping["scientificValidation"] is False
            and grouping["operationalParityCertified"] is False
            and grouping["E2E_NUCLEO_PASS"] is False
            and grouping["E2E_INTEGRAL_PASS"] is False,
            "bridge grouping result is nonconforming or falsely certified")

    # Second: independently reopen ZIP members and reconcile all four HTTP
    # deliveries to the exact run-item -> SQL delivery ledger + V2 staging.
    preflight = BRIDGE.PREFLIGHT.verify(root)
    http = RUN.LINEAGE.load(http_path)
    sql = RUN.LINEAGE.load(sql_path)
    staging = RUN.LINEAGE.load(staging_path)
    run_match = RUN.compare(preflight, http, sql, staging,
                            expected_sha, expected_winner)
    require(run_match["schema"] == RUN.OUTPUT_SCHEMA
            and run_match["status"] == "RECEIPT_FIELDS_MATCHED_NOT_AUTHENTICATED"
            and run_match["receiptsFieldConsistencyVerified"] is True
            and run_match["sourceSqlRuntimeAuthenticatedExternally"] is False
            and run_match["httpRunExecutionAuthenticatedExternally"] is False
            and run_match["goldIdentityStatesAvailable"] is False
            and run_match["E2E_NUCLEO_PASS"] is False
            and run_match["E2E_INTEGRAL_PASS"] is False
            and run_match["operationalParityCertified"] is False
            and run_match["scientificValidation"] is False,
            "HTTP/SQL run receipt claimed unproven certification")
    require(run_match["sourceObservations"] == grouping["sourceObservations"]
            and run_match["sourceSetSha256Utf16le"]
                == preflight["sourceSetSha256Utf16le"]
            and run_match["commitSha"] == grouping["checkoutSha"]
            and run_match["modelWinnerSha256"] == grouping["modelWinnerSha256"]
            and grouping["sourceBridgeManifestSha256"]
                == preflight["bridgeManifestSha256"]
            and grouping["sourceTruthSidecarSha256"] == preflight["truthSha256"]
            and run_match["deliveryComparisons"] == grouping["packages"] == 4,
            "independent bridge/oracle versus run/HTTP receipt drift")
    pairs = grouping["sourceGroupPairs"]
    require(isinstance(pairs, dict)
            and set(pairs) == {"truePositive", "falsePositive", "falseNegative",
                               "recallPercent", "observedFdrPercent"}
            and all(type(pairs[x]) is int and pairs[x] >= 0
                    for x in ("truePositive", "falsePositive", "falseNegative")),
            "independent grouping pair metrics malformed")
    # Only aggregate metrics + digests; do not export source IDs, group UUID,
    # identities or untrusted claims of Gold, HTTP execution, NAS or science.
    return {
        "schema": SCHEMA,
        "status": "CONSISTENT_RECEIPTS_OFFLINE_NOT_AUTHENTICATED",
        "scope": "DEV_OFFLINE_ONLY_NOT_RUNNER_PRODUCTIVE_E2E_PROOF",
        "runIdSha256": run_match["runIdSha256"],
        "commitSha": expected_sha,
        "modelWinnerSha256": expected_winner.upper(),
        "bridgeManifestSha256": grouping["sourceBridgeManifestSha256"],
        "sourceSetSha256Utf16le": run_match["sourceSetSha256Utf16le"],
        "sourceObservations": grouping["sourceObservations"],
        "baseIdentities": grouping["baseIdentities"],
        "zipPackagesVerified": grouping["packages"],
        "sqlHttpDeliveryReceiptsMatched": run_match["deliveryComparisons"],
        "groupingPairCounts": pairs,
        "zipBridgeAndIndependentOracleChecked": True,
        "sqlHttpRunReceiptFieldsChecked": True,
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
    parser.add_argument("--ibge-root", type=Path, required=True)
    parser.add_argument("--http", type=Path, required=True)
    parser.add_argument("--sql-ledger", type=Path, required=True)
    parser.add_argument("--staging", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-winner", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    existing = (args.http, args.sql_ledger, args.staging)
    require(not args.receipt.is_symlink()
            and not args.receipt.exists()
            and not args.receipt.resolve().is_relative_to(args.ibge_root.resolve())
            and all(args.receipt.resolve() != source.resolve()
                    for source in existing),
            "cannot overwrite private ZIP, staging, SQL or HTTP evidence")
    receipt = combine(args.ibge_root, args.http, args.sql_ledger,
                      args.staging, args.expected_sha, args.expected_winner)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    with args.receipt.open("x", encoding="utf-8") as output:
        output.write(json.dumps(receipt, indent=2, sort_keys=True) + "\n")


if __name__ == "__main__":
    main()

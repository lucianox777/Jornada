#!/usr/bin/env python3
"""Offline, fail-closed join of the real IBGE ZIP bridge and V9 grouping staging.

The runtime/scorer NEVER sees the bridge's independent truth. This verifier
operates after both producers, and NEVER certifies V9 or SQL provenance.
"""
import argparse
import importlib.util
import json
from pathlib import Path


def load_sibling(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


SCRIPTS = Path(__file__).resolve().parent
PREFLIGHT = load_sibling(
    "v9_bridge_preflight_integrator",
    SCRIPTS / "v9-e2e-ibge-bridge-preflight.py")
ORACLE = load_sibling(
    "v9_grouping_oracle_integrator",
    SCRIPTS / "e2e-v9-grouping-oracle.py")


def require(ok, message):
    if not ok:
        raise ValueError(message)


def reconcile(root, observed, expected_commit, expected_winner):
    """Validate real ZIP bytes, bridge provenance and exactly the staged set."""
    root = Path(root)
    observed = Path(observed)
    require(root.is_dir() and not root.is_symlink()
            and observed.is_file() and not observed.is_symlink(),
            "missing or symlinked bridge/staging input")
    # The SQL staging file must never be the external truth sidecar nor live
    # in the ZIP/generator tree. The bridge owns the input identities and
    # sidecar; the scorer owns the independently sourced SQL staging.
    require(not observed.resolve().is_relative_to(root.resolve()),
            "SQL runtime staging must be outside the IBGE bridge tree")
    bridge_manifest = root / "ingestion/bridge-manifest.json"
    truth_sidecar = root / "ingestion/bridge-truth.jsonl"
    bridge = PREFLIGHT.verify(root)
    require(bridge["schema"] == "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1"
            and bridge["status"] == "PASS"
            and bridge["excludedObservations"] == 0
            and bridge["sourceObservations"] == bridge["materializedObservations"]
            and bridge["baseIdentities"] == PREFLIGHT.PEOPLE
            and bridge["packages"] == 4,
            "bridge preflight coverage invalid")
    receipt = ORACLE.compare_groupings(
        bridge_manifest, truth_sidecar, observed,
        expected_commit, expected_winner)
    require(receipt["sourceSchema"] == ORACLE.STAGING_SCHEMA
            and receipt["status"] == "MEASURED_NOT_CERTIFIED"
            and receipt["identityStatesAvailable"] is False
            and receipt["producerBridgeBinding"] is False
            and receipt["identityStates"] is None
            and receipt["observations"] == bridge["sourceObservations"]
            and receipt["bridgeManifestSha256"] == bridge["bridgeManifestSha256"]
            and receipt["modelWinnerSha256"] == expected_winner.upper()
            and receipt["scientificValidation"] is False
            and receipt["operationalParityCertified"] is False
            and receipt["E2E_NUCLEO_PASS"] is False
            and receipt["E2E_INTEGRAL_PASS"] is False,
            "staging and bridge did not reconcile safely")
    # No source person IDs, group UUIDs, CPF, truth rows or ZIPs enter receipt.
    return {
        "schema": "E2E_V9_IBGE_BRIDGE_GROUPING_RECONCILIATION_DEV_V1",
        "status": "RECONCILED_OFFLINE_NOT_CERTIFIED",
        "bridgeZipBytesAndSourceSetVerified": True,
        "sourceBridgeManifestSha256": bridge["bridgeManifestSha256"],
        "sourceTruthSidecarSha256": bridge["truthSha256"],
        "checkoutSha": expected_commit,
        "modelWinnerSha256": expected_winner.upper(),
        "sourceObservations": receipt["observations"],
        "baseIdentities": bridge["baseIdentities"],
        "packages": bridge["packages"],
        "sourceGroupPairs": {
            "truePositive": receipt["truePositivePairs"],
            "falsePositive": receipt["falsePositivePairs"],
            "falseNegative": receipt["falseNegativePairs"],
            "recallPercent": receipt["pairwiseRecallPercent"],
            "observedFdrPercent": receipt["observedPairwiseFdrPercent"],
        },
        "identityStatesAvailable": False,
        "linkageSqlRunProvenanceCertified": False,
        "bridgeToSqlHttpDeliveryProven": False,
        "scientificValidation": False,
        "operationalParityCertified": False,
        "activated": False,
        "E2E_NUCLEO_PASS": False,
        "E2E_INTEGRAL_PASS": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ibge-root", type=Path, required=True)
    parser.add_argument("--staging", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-winner", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    require(not args.receipt.is_symlink()
            and not args.receipt.exists()
            and not args.receipt.resolve().is_relative_to(args.ibge_root.resolve())
            and args.receipt.resolve() != args.staging.resolve(),
            "receipt cannot alias or overwrite bridge/staging evidence")
    receipt = reconcile(
        args.ibge_root, args.staging,
        args.expected_sha, args.expected_winner)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation: never overwrite preexisting evidence from another run.
    with args.receipt.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(receipt, indent=2, sort_keys=True) + "\n")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Independent, nonpublishing synthetic 1k truth versus physical SQL decisions.

The scorer does not read this truth, and the oracle never sees names, CPFs,
raw person attributes, score inputs or model parameter data. No scientific
TEST, TRAIN, 10k/100k, HML or PROD permissions are implied.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import tempfile
import uuid

OPAQUE = re.compile(r"SYNTH-[0-9A-F]{32}")
HEX64 = re.compile(r"[0-9A-F]{64}")
SCOPE = "DEV_IBGE_1K_R1_POSTPROCESSOR_NOT_SCIENTIFIC_TEST"


def require(accepted, label):
    if not accepted:
        raise ValueError("V9_R1_E2E_ORACLE_" + label)


def check(truth_file: Path, preflight_file: Path, observed_file: Path, run_id: str):
    try:
        parsed_run = str(uuid.UUID(run_id))
    except (ValueError, AttributeError) as exc:
        raise ValueError("V9_R1_E2E_ORACLE_RUN_ID_INVALID") from exc
    for path in (truth_file, preflight_file, observed_file):
        require(path.is_file() and not path.is_symlink()
                and 0 < path.stat().st_size < 32 * 1024 * 1024,
                "FILE_NOT_PRIVATE_AND_BOUNDED")
    raw_truth = truth_file.read_bytes()
    digest = hashlib.sha256(raw_truth).hexdigest().upper()
    receipt = json.loads(preflight_file.read_text(encoding="utf-8"))
    require(receipt.get("schema") == "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1"
            and receipt.get("status") == "PASS"
            and receipt.get("scope") ==
                "DEV_SYNTHETIC_INGESTION_ONLY_NOT_V9_CERTIFICATION"
            and receipt.get("seed") == 355
            and receipt.get("baseIdentities") == 1000
            and receipt.get("truthSha256") == digest
            and HEX64.fullmatch(digest)
            and receipt.get("scientificValidation") is False
            and receipt.get("activated") is False,
            "GABARITO_CONTENT_SEAL_MISMATCH")
    truth = {}
    base_ids = set()
    for line in raw_truth.decode("utf-8").splitlines():
        row = json.loads(line)
        require(row.get("Status") == "MATERIALIZADA"
                and isinstance(row.get("BasePersonId"), str)
                and bool(row["BasePersonId"])
                and isinstance(row.get("Partition"), str)
                and bool(row["Partition"]),
                "SOURCE_TRUTH_LINE_INVALID")
        # This is a non-scientific ingestion fixture: the partition is only
        # historical source metadata, never authorization to run TEST/TRAIN.
        opaque = row.get("OpaquePersonId")
        require(isinstance(opaque, str) and OPAQUE.fullmatch(opaque)
                and opaque not in truth,
                "SOURCE_OPAQUE_ID_INVALID_OR_DUPLICATE")
        base_ids.add(row["BasePersonId"])
        truth[opaque] = row["BasePersonId"]
    require(len(truth) == receipt.get("materializedObservations")
            and len(base_ids) == 1000,
            "SOURCE_TRUTH_POPULATION_MISMATCH")
    actual = {}
    for line in observed_file.read_text(encoding="utf-8").splitlines():
        parts = line.strip().split("|")
        require(len(parts) == 2, "SQL_LINE_FORMAT")
        opaque, raw_uuid = parts
        require(OPAQUE.fullmatch(opaque) and opaque not in actual,
                "SQL_OPAQUE_ID_INVALID_OR_DUPLICATE")
        if raw_uuid == "NULL":
            resolved = None
        else:
            try:
                resolved = str(uuid.UUID(raw_uuid))
            except ValueError as exc:
                raise ValueError("V9_R1_E2E_ORACLE_SQL_UUID_INVALID") from exc
        actual[opaque] = resolved
    require(set(actual) == set(truth), "SQL_SOURCE_UNIVERSE_NOT_EQUAL")
    uuid_to_base = {}
    base_to_uuid = {}
    undefined = 0
    for opaque, base in truth.items():
        resolved = actual[opaque]
        if resolved is None:
            undefined += 1
            continue
        uuid_to_base.setdefault(resolved, set()).add(base)
        base_to_uuid.setdefault(base, set()).add(resolved)
    false_fusions = sum(len(bases) - 1 for bases in uuid_to_base.values()
                        if len(bases) > 1)
    split_bases = sum(len(uuids) > 1 for uuids in base_to_uuid.values())
    # r1 prioritizes false-fusion prevention. Incomplete unique linkage is
    # reported, not silently promoted into an inherited group-level UUID.
    require(false_fusions == 0, "FALSE_FUSION_DETECTED")
    return {
        "schema": "V9_DEV_E2E_R1_INDEPENDENT_ORACLE_V1",
        "status": "PASS",
        "scope": SCOPE,
        "runId": parsed_run,
        "truthSha256": digest,
        "baseIdentities": 1000,
        "sourceObservations": len(truth),
        "physicallyObservedSources": len(actual),
        "resolvedSources": len(actual) - undefined,
        "undefinedSources": undefined,
        "uniquePublishedIdentities": len(uuid_to_base),
        "falseFusionIdentities": false_fusions,
        "splitBaseIdentities": split_bases,
        "scientificValidation": False,
        "modelActivated": False,
    }


def self_test():
    with tempfile.TemporaryDirectory(prefix="jornada-v9-r1-dev-oracle-") as root:
        directory = Path(root)
        truth = directory / "truth.jsonl"
        pre = directory / "pre.json"
        obs = directory / "obs.txt"
        lines = [
            {"Status": "MATERIALIZADA", "BasePersonId": f"B{i}",
             "Partition": "TRAIN", "OpaquePersonId": "SYNTH-" + f"{i:032X}"}
            for i in range(1000)
        ]
        truth.write_text("".join(json.dumps(x) + "\n" for x in lines),
                         encoding="utf-8")
        digest = hashlib.sha256(truth.read_bytes()).hexdigest().upper()
        pre.write_text(json.dumps({
            "schema": "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1",
            "status": "PASS",
            "scope": "DEV_SYNTHETIC_INGESTION_ONLY_NOT_V9_CERTIFICATION",
            "seed": 355, "baseIdentities": 1000,
            "materializedObservations": 1000,
            "truthSha256": digest,
            "scientificValidation": False,
            "activated": False
        }), encoding="utf-8")
        observations = ["SYNTH-" + f"{i:032X}" + "|" +
                        str(uuid.UUID(int=i+1)) for i in range(1000)]
        obs.write_text("\n".join(observations)+"\n", encoding="utf-8")
        run = str(uuid.uuid4())
        require(check(truth, pre, obs, run)["falseFusionIdentities"] == 0,
                "SELFTEST_POSITIVE")
        # Two different base identities sharing one Gold UUID is prohibited.
        observations[1] = observations[0].split("|")[0].replace(
            "SYNTH-" + f"{0:032X}", "SYNTH-" + f"{1:032X}") + "|" + \
            observations[0].split("|")[1]
        obs.write_text("\n".join(observations)+"\n", encoding="utf-8")
        try:
            check(truth, pre, obs, run)
            raise AssertionError("false fusion self-test did not reject")
        except ValueError as exc:
            require("FALSE_FUSION_DETECTED" in str(exc), "SELFTEST_FALSE_FUSION")
        obs.write_text("\n".join(observations[:-1])+"\n",encoding="utf-8")
        try:
            check(truth, pre, obs, run)
            raise AssertionError("missing source self-test did not reject")
        except ValueError as exc:
            require("SQL_SOURCE_UNIVERSE_NOT_EQUAL" in str(exc),
                    "SELFTEST_MISSING_SOURCE")
        pre.write_text(pre.read_text(encoding="utf-8").replace(
            digest, "0" * 64), encoding="utf-8")
        try:
            check(truth, pre, obs, run)
            raise AssertionError("SHA tamper self-test did not reject")
        except ValueError as exc:
            require("GABARITO_CONTENT_SEAL_MISMATCH" in str(exc),
                    "SELFTEST_SHA_TAMPER")
    print("V9_DEV_E2E_INDEPENDENT_ORACLE_SELF_TEST: PASS")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--truth", type=Path)
    parser.add_argument("--preflight", type=Path)
    parser.add_argument("--observed", type=Path)
    parser.add_argument("--run-id")
    parser.add_argument("--receipt", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not all((args.truth, args.preflight, args.observed,
                args.run_id, args.receipt)):
        parser.error("all --truth/--preflight/--observed/--run-id/--receipt required")
    require(not args.receipt.exists() and not args.receipt.is_symlink(),
            "RECEIPT_MUST_BE_NEW")
    result = check(args.truth, args.preflight, args.observed, args.run_id)
    args.receipt.write_text(json.dumps(result, sort_keys=True, indent=2) + "\n",
                            encoding="utf-8")
    print("V9_DEV_E2E_R1_INDEPENDENT_ORACLE: PASS (aggregate-only)")


if __name__ == "__main__":
    main()

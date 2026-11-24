#!/usr/bin/env python3
"""Stage-8 DEV 1k identity-only oracle, based on independently generated truth.

No Runner scoring, no ReplayTest, no scientific TEST, no identifier in output.
Does NOT certify the stronger edge/score/decision oracle or model #38.
"""
import argparse
import hashlib
import json
import re
import uuid
from collections import Counter
from math import comb
from pathlib import Path

MAX_SIZE = 8 * 1024 * 1024
STATUS = {"ASSOCIACAO_EXISTENTE", "NOVA_IDENTIDADE", "INDEFINIDA"}
SHA = re.compile(r"^[0-9A-F]{64}$")
OPAQUE = re.compile(r"^SYNTH-[0-9A-F]{32}$")


def strict_pairs(pairs):
    output = {}
    for key, value in pairs:
        if key in output:
            raise ValueError("duplicate JSON key")
        output[key] = value
    return output


def no_nonfinite(value):
    raise ValueError("nonfinite JSON value")


def read_bytes(path):
    path = Path(path)
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_SIZE:
        raise ValueError("invalid or oversized private truth/SQL input")
    return path.read_bytes()


def read_json(raw):
    return json.loads(raw, object_pairs_hook=strict_pairs,
                      parse_constant=no_nonfinite)


def compare(bridge, truth_bytes, sql_text, runner, required_identities=1000):
    if not isinstance(bridge, dict) or not isinstance(runner, dict):
        raise ValueError("oracle envelopes must be JSON objects")
    sidecar = bridge.get("truthSidecar", {})
    if (not isinstance(sidecar, dict)
        or sidecar.get("path") != "bridge-truth.jsonl"
        or sidecar.get("containsSyntheticGroundTruth") is not True
        or sidecar.get("allowedForScoring") is not False
        or not isinstance(sidecar.get("sha256"), str)
        or not SHA.fullmatch(sidecar["sha256"])
        or hashlib.sha256(truth_bytes).hexdigest().upper() != sidecar["sha256"]):
        raise ValueError("independent sidecar byte SHA/metadata mismatch")
    if runner.get("schema") != "V9_E2E_DEV_1K_FULL_R1_V1" or (
        runner.get("status") != "PASS"
        or runner.get("scope") != "HTTP_1K_PROCESSOR_TO_V9_FULL_DT10_DEV_ONLY"
        or runner.get("mode") != "FULL"
        or runner.get("publish") is not True
        or runner.get("scientificTest") is not False
        or runner.get("activation") is not False
        or runner.get("productionCertified") is not False):
        raise ValueError("Runner receipt does not prove controlled FULL")
    expected = {}
    base_ids = set()
    for raw in truth_bytes.splitlines():
        row = read_json(raw)
        if (not isinstance(row, dict) or row.get("Status") != "MATERIALIZADA"
            or row.get("ExclusionReason") is not None
            or row.get("Partition") not in ("TRAIN", "VALIDATION", "TEST")
            or row.get("GestorCodigo") not in ("SEHAB", "SMADS", "SMDET", "SMS")
            or not isinstance(row.get("BasePersonId"), str)
            or not row["BasePersonId"].strip()
            or not isinstance(row.get("OpaquePersonId"), str)
            or not OPAQUE.fullmatch(row["OpaquePersonId"])
            or row["OpaquePersonId"] in expected):
            raise ValueError("missing/duplicate/invalid generated truth observation")
        expected[row["OpaquePersonId"]] = row["BasePersonId"]
        base_ids.add(row["BasePersonId"])
    if (len(base_ids) != required_identities
        or len(expected) < required_identities
        or len(expected) > 4 * required_identities):
        raise ValueError("stage8 sidecar not exact 1k synthetic base population")
    observed = {}
    for raw in sql_text.splitlines():
        parts = raw.split("\t")
        if len(parts) != 3:
            raise ValueError("SQL must emit exact three-column TSV")
        source, destination, status = [x.strip() for x in parts]
        if source in observed or not OPAQUE.fullmatch(source) or status not in STATUS:
            raise ValueError("physical SQL source/status missing or duplicated")
        if status == "INDEFINIDA":
            if destination:
                raise ValueError("abstention cannot publish a destination")
            observed[source] = None
            continue
        try:
            gid = uuid.UUID(destination)
        except (ValueError, AttributeError) as exc:
            raise ValueError("resolved publication missing UUID") from exc
        if gid.int == 0:
            raise ValueError("empty Gold identity UUID")
        observed[source] = str(gid)
    if (set(observed) != set(expected)
        or type(runner.get("eligible")) is not int
        or type(runner.get("evaluated")) is not int
        or runner["eligible"] != len(expected)
        or runner["evaluated"] != len(expected)):
        raise ValueError("physical SQL candidate/universe census diverges from external truth")

    gold_bases = {}
    person_golds = {}
    abstentions = 0
    for source, base in expected.items():
        dest = observed[source]
        if dest is None:
            abstentions += 1
            continue
        gold_bases.setdefault(dest, set()).add(base)
        person_golds.setdefault(base, set()).add(dest)
    # Independent truth vs physically published source-pairs. Abstentions
    # are lost true positive pairs, not false predicted pairs. No raw IDs
    # in output or O(n²) pair allocation.
    true_per_base = Counter(expected.values())
    observed_per_gold = Counter(x for x in observed.values() if x is not None)
    intersection = Counter((expected[src], gid) for src, gid in observed.items()
                           if gid is not None)
    expected_pairs = sum(comb(n, 2) for n in true_per_base.values())
    predicted_pairs = sum(comb(n, 2) for n in observed_per_gold.values())
    tp = sum(comb(n, 2) for n in intersection.values())
    fp, fn = predicted_pairs - tp, expected_pairs - tp
    if min(tp, fp, fn) < 0:
        raise ValueError("negative pair metrics from SQL and generator truth")
    pair_metrics = {
        "truePositive": tp, "falsePositive": fp, "falseNegative": fn,
        "recallPercent": (round(100 * tp / expected_pairs, 6)
                          if expected_pairs else None),
        "observedFdrPercent": (round(100 * fp / predicted_pairs, 6)
                               if predicted_pairs else None),
    }
    false_fused = sum(1 for originals in gold_bases.values() if len(originals) > 1)
    false_split = sum(1 for resolved in person_golds.values() if len(resolved) > 1)
    # A complete, physical 1k identity census with no erroneous merge is an
    # identity-only gate. More conservative undefined or split may be accepted
    # here; scientific quality/TF FS/recall remain UNPROVEN, never certified.
    return {
        "schema": "V9_E2E_1K_EXTERNAL_TRUTH_IDENTITY_ORACLE_V1",
        "status": "PASS" if false_fused == 0 else "FAIL",
        "scope": "PHYSICAL_SQL_IDENTITY_SETS_ONLY_NO_SCIENTIFIC_SCORE_ORACLE",
        "baseIdentities": len(base_ids),
        "sourceObservations": len(expected),
        "goldIdentityCount": len(gold_bases),
        "abstentions": abstentions,
        "falseFusedGoldIdentityCount": false_fused,
        "splitBaseIdentityCount": false_split,
        "sourcePairMetrics": pair_metrics,
        "pairMetricScope": "SYNTHETIC_SOURCE_PAIRS_NOT_SCIENTIFIC_FS_CERTIFICATION",
        "truthSha256": hashlib.sha256(truth_bytes).hexdigest(),
        "scientificCertified": False,
        "e2eSystemCertified": False
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bridge", type=Path, required=True)
    ap.add_argument("--truth", type=Path, required=True)
    ap.add_argument("--sql", type=Path, required=True)
    ap.add_argument("--runner", type=Path, required=True)
    ap.add_argument("--receipt", type=Path, required=True)
    a = ap.parse_args()
    inputs = (a.bridge, a.truth, a.sql, a.runner)
    if (a.receipt.is_symlink() or any(a.receipt.resolve() == x.resolve() for x in inputs)
        or (a.receipt.exists() and a.receipt.stat().st_size > 0)):
        raise ValueError("output receipt must be a new independent path")
    report = compare(
        read_json(read_bytes(a.bridge)), read_bytes(a.truth),
        read_bytes(a.sql).decode("utf-8"),
        read_json(read_bytes(a.runner)))
    a.receipt.write_text(json.dumps(report, sort_keys=True, indent=2) + "\n",
                         encoding="utf-8")
    if report["status"] != "PASS":
        raise SystemExit("V9_E2E_1K_ORACLE_FALSE_FUSION_OR_INCOMPLETE")


if __name__ == "__main__":
    main()

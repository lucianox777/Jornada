#!/usr/bin/env python3
"""DEV 1k identity census oracle; independent of scoring and publication.

Inputs are private: bridge manifest + sealed truth for each delivery wave,
a SQL census after publication, and the aggregate Runner receipt. Only the
aggregate report may leave the disposable attempt. This is not the stronger
candidate/edge/score oracle required for full E2E acceptance.
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from math import comb
from pathlib import Path
import re
import uuid

MAX_BYTES = 32 * 1024 * 1024
WINNER = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"
MANAGERS = {"SEHAB", "SMADS", "SMDET", "SMS"}
OPAQUE = re.compile(r"SYNTH-[0-9A-F]{32}")
PROTECTED = {"CPF_DETERMINISTICO", "UUID_JORNADA_RETROALIMENTACAO",
             "CORRECAO_GOVERNADA", "CONFLITO_GOVERNADO"}
SCOPE = "DEV_1K_IDENTITY_CENSUS_NOT_SCORE_ORACLE"


def require(condition, label):
    if not condition:
        raise ValueError("V9_E2E_ORACLE_" + label)


def strict_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "DUPLICATE_JSON_KEY")
        result[key] = value
    return result


def nonfinite(_):
    raise ValueError("V9_E2E_ORACLE_NONFINITE")


def load(raw):
    return json.loads(raw, object_pairs_hook=strict_pairs, parse_constant=nonfinite)


def read(path):
    require(path.is_file() and not path.is_symlink(), "PRIVATE_INPUT_TYPE")
    with path.open("rb") as stream:
        raw = stream.read(MAX_BYTES + 1)
    require(0 < len(raw) <= MAX_BYTES, "PRIVATE_INPUT_SIZE")
    return raw


def version_key(manager, package, opaque):
    require(manager in MANAGERS and isinstance(package, str)
            and 0 < len(package) <= 250 and package.endswith(".zip")
            and "/" not in package and chr(92) not in package
            and isinstance(opaque, str) and OPAQUE.fullmatch(opaque),
            "VERSION_KEY_INVALID")
    return manager, package, opaque


def canonical_uuid(value):
    if value is None:
        return None
    require(isinstance(value, str), "UUID_INVALID")
    try:
        parsed = uuid.UUID(value)
    except ValueError:
        raise ValueError("V9_E2E_ORACLE_UUID_INVALID") from None
    require(parsed.int != 0 and str(parsed) == value.lower(), "UUID_INVALID")
    return str(parsed)


def compare(waves, observed, runner, required_identities=1000):
    # A version is identified by manager + ZIP basename + opaque source ID.
    # A persistent origin is manager + opaque ID across the sealed waves.
    # Never collapse multiple versions to one observation or pass truth to FS.
    expected = {}
    source_bases = {}
    seals = []
    require(0 < len(waves) <= 16, "WAVE_COUNT_INVALID")
    for manifest, raw in waves:
        sidecar = manifest.get("truthSidecar", {})
        digest = hashlib.sha256(raw).hexdigest().upper()
        require(sidecar.get("path") == "bridge-truth.jsonl"
                and sidecar.get("containsSyntheticGroundTruth") is True
                and sidecar.get("allowedForScoring") is False
                and sidecar.get("sha256") == digest
                and digest not in seals, "TRUTH_SEAL_INVALID")
        seals.append(digest)
        for line in raw.splitlines():
            row = load(line)
            require(row.get("Status") == "MATERIALIZADA"
                    and row.get("ExclusionReason") is None
                    and row.get("Partition") in {"TRAIN", "VALIDATION", "TEST"}
                    and isinstance(row.get("BasePersonId"), str)
                    and bool(row["BasePersonId"]), "TRUTH_ROW_INVALID")
            key = version_key(row["GestorCodigo"], row["PackageFileName"],
                              row["OpaquePersonId"])
            base = row["BasePersonId"]
            require(key not in expected, "DUPLICATE_TRUTH_VERSION")
            origin = key[0], key[2]
            require(source_bases.setdefault(origin, base) == base, "TRUTH_ORIGIN_DRIFT")
            expected[key] = base
    require(len(set(expected.values())) == required_identities
            and required_identities <= len(expected) <= required_identities * 16,
            "TRUTH_POPULATION_INVALID")
    require(isinstance(observed, list) and len(observed) == len(expected),
            "SQL_CENSUS_COUNT")
    require(runner.get("mode") == "FULL" and runner.get("status") == "PUBLICADO"
            and runner.get("winnerSha256") == WINNER
            and runner.get("scientificTest") is False
            and runner.get("activated") is False
            and type(runner.get("eligible")) is int
            and type(runner.get("evaluated")) is int
            and runner["eligible"] == runner["evaluated"], "RUNNER_RECEIPT_INVALID")
    run_id = canonical_uuid(runner.get("runId"))
    require(run_id is not None, "RUN_ID_MISSING")
    actual = {}
    origins = {}
    reverse_origins = {}
    cpf_origins = Counter()
    cpf_refs = defaultdict(set)
    noncpf_associations = []
    decisions = Counter()
    reasons = Counter()
    eligible = 0
    for row in observed:
        key = version_key(row["manager"], row["package"], row["opaque"])
        require(key in expected and key not in actual, "SQL_VERSION_CENSUS")
        origin = key[0], key[2]
        origin_id = row.get("originId")
        require(type(origin_id) is int and origin_id > 0
                and origins.setdefault(origin, origin_id) == origin_id
                and reverse_origins.setdefault(origin_id, origin) == origin,
                "SQL_ORIGIN_IDENTITY_DRIFT")
        has_cpf = row.get("hasCpf")
        require(type(has_cpf) is bool, "CPF_FLAG_INVALID")
        current = canonical_uuid(row.get("currentUuid"))
        status = row.get("currentStatus")
        method = row.get("currentMethod")
        require(status in {"RESOLVIDO", "NAO_RESOLVIDO", "CONFLITO"},
                "CURRENT_STATUS_INVALID")
        if status == "RESOLVIDO":
            require(current is not None and row.get("goldState") == "REFERENCIA",
                    "RESOLVED_WITHOUT_GOLD_REFERENCE")
        else:
            # Provisional Gold may have a UUID; it is not a resolved identity.
            current = None
        result = row.get("runResult")
        published = canonical_uuid(row.get("publishedUuid"))
        if has_cpf:
            require(result is None and published is None and row.get("resultRunId") is None
                    and row.get("publicationReason") is None,
                    "CPF_MUST_NOT_ENTER_FULL")
            require(status == "RESOLVIDO" and method == "CPF_DETERMINISTICO",
                    "CPF_DETERMINISTIC_REFERENCE_REQUIRED")
            cpf_origins[origin] += 1
            cpf_refs[expected[key]].add(current)
        else:
            eligible += 1
            require(canonical_uuid(row.get("resultRunId")) == run_id
                    and result in {"ASSOCIACAO_EXISTENTE", "NOVA_IDENTIDADE", "INDEFINIDA"},
                    "FULL_RESULT_MISSING")
            reason = row.get("publicationReason")
            require(isinstance(reason, str) and re.fullmatch(r"[A-Z0-9_]{1,160}", reason),
                    "PUBLICATION_REASON_INVALID")
            decisions[result] += 1
            if result == "INDEFINIDA":
                require(published is None, "ABSTENTION_WITH_UUID")
                require(current is None or method in PROTECTED, "UNPROTECTED_ABSTENTION_RESOLVED")
                reasons[reason] += 1
            else:
                require(published is not None and published == current,
                        "PUBLISHED_CURRENT_UUID_DIVERGENCE")
                if result == "ASSOCIACAO_EXISTENTE":
                    noncpf_associations.append((key, current))
        actual[key] = current
    require(set(actual) == set(expected) and eligible == runner["eligible"] and eligible > 0,
            "FULL_ELIGIBLE_CENSUS_MISMATCH")
    # Cross-origin matches to CPF references are publication evidence, not
    # proof of candidate membership or a score oracle.
    cpf_cross = sum(any(k != (key[0], key[2]) and source_bases[k] == expected[key]
                        for k in cpf_origins)
                    and dest in cpf_refs[expected[key]]
                    for key, dest in noncpf_associations)
    multi = sum(n > 1 for n in cpf_origins.values())
    true_sizes = Counter(expected.values())
    predicted_sizes = Counter(v for v in actual.values() if v is not None)
    joint = Counter((expected[k], v) for k, v in actual.items() if v is not None)
    true_pairs = sum(comb(n, 2) for n in true_sizes.values())
    predicted_pairs = sum(comb(n, 2) for n in predicted_sizes.values())
    tp = sum(comb(n, 2) for n in joint.values())
    fp, fn = predicted_pairs - tp, true_pairs - tp
    fused = sum(n > 1 for n in Counter(gold for _, gold in joint).values())
    split = sum(n > 1 for n in Counter(base for base, _ in joint).values())
    require(min(tp, fp, fn) >= 0, "PAIR_COUNTS_INVALID")
    return {
        "schema": "V9_E2E_1K_VERSIONED_IDENTITY_CENSUS_V1", "scope": SCOPE,
        "status": "PASS" if fused == 0 and multi > 0 and cpf_cross > 0 else "FAIL",
        "runId": run_id, "winnerSha256": WINNER, "truthSha256ByWave": seals,
        "baseIdentities": required_identities, "sourceVersions": len(expected),
        "persistentOrigins": len(origins), "runnerEligible": eligible,
        "cpfVersions": len(expected) - eligible, "cpfMultiversionOrigins": multi,
        "nonCpfCrossOriginAssociationsToCpf": cpf_cross,
        "resolvedVersions": sum(v is not None for v in actual.values()),
        "unresolvedVersions": sum(v is None for v in actual.values()),
        "publicationDecisions": dict(decisions), "indefiniteReasons": dict(reasons),
        "falseFusedGoldIdentities": fused, "splitBaseIdentities": split,
        "pairMetrics": {"tp": tp, "fp": fp, "fn": fn,
                        "truePairs": true_pairs, "predictedPairs": predicted_pairs,
                        "recall": tp / true_pairs if true_pairs else None,
                        "fdr": fp / predicted_pairs if predicted_pairs else None},
        "candidateEdgeScoreOracleVerified": False,
        "scientificCertified": False, "e2eSystemCertified": False
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wave", nargs=2, action="append", required=True, type=Path,
                        metavar=("MANIFEST", "TRUTH"))
    parser.add_argument("--sql", type=Path, required=True)
    parser.add_argument("--runner", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = compare([(load(read(m)), read(t)) for m, t in args.wave],
                         load(read(args.sql)), load(read(args.runner)))
        with args.receipt.open("x", encoding="utf-8") as stream:
            json.dump(report, stream, sort_keys=True, indent=2)
    except (ValueError, OSError, KeyError, TypeError, AttributeError):
        raise SystemExit("V9_E2E_IDENTITY_CENSUS_REJECTED") from None
    if report["status"] != "PASS":
        raise SystemExit("V9_E2E_IDENTITY_CENSUS_FAIL")
    print("V9_E2E_IDENTITY_CENSUS_PASS_NOT_FULL_E2E")


if __name__ == "__main__":
    main()

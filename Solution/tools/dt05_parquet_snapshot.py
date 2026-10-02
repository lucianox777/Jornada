#!/usr/bin/env python3
"""DT-05: immutable content-addressed Parquet capture/verification for a NAS volume.

Input: a frozen, sorted NDJSON export of the *actual* linkage input universe.
This utility does not export SQL, change the Runner or authorize publication.
Requires pyarrow. No personal data is placed in manifests or logs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import tempfile
from datetime import datetime, timezone

SCHEMA_VERSION = 2
SUPPORTED_SCHEMA_VERSIONS = {1, 2}
SNAPSHOT_KINDS = {"input-universe": "observation_key", "candidate-state": "candidate_uuid"}


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def atomic_bytes(path, payload):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".dt05-", delete=False) as tmp:
        name = tmp.name
        tmp.write(payload)
        tmp.flush()
        os.fsync(tmp.fileno())
    try:
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def capture(root, source, run_id, versions, chunk_size, snapshot_kind="input-universe"):
    import pyarrow as pa
    import pyarrow.parquet as pq

    root = Path(root).resolve()
    if chunk_size < 1:
        raise ValueError("chunk_size must be positive")
    if snapshot_kind not in SNAPSHOT_KINDS:
        raise ValueError("unsupported snapshot_kind")
    key_field = SNAPSHOT_KINDS[snapshot_kind]
    partitions = []
    rows = []
    total = 0
    previous_key = None

    def flush():
        nonlocal total
        if not rows:
            return
        logical_hash = sha256(b"\n".join(canonical(row) for row in rows))
        index_path = root / "logical-index" / (logical_hash + ".json")
        if index_path.exists():
            entry = json.loads(index_path.read_text(encoding="utf-8"))
            physical = root / entry["path"]
            if not physical.is_file() or sha256(physical.read_bytes()) != entry["sha256"]:
                raise ValueError("corrupted previously published partition")
            if entry["rows"] != len(rows):
                raise ValueError("logical index row count mismatch")
        else:
            table = pa.Table.from_pylist(rows)
            with tempfile.NamedTemporaryFile(dir=root / "tmp", prefix=".parquet-", suffix=".parquet", delete=False) as tmp:
                tmp_name = tmp.name
            try:
                pq.write_table(table, tmp_name, compression="zstd", use_dictionary=True)
                data = Path(tmp_name).read_bytes()
                physical_hash = sha256(data)
                relative = "objects/" + physical_hash[:2] + "/" + physical_hash + ".parquet"
                physical = root / relative
                physical.parent.mkdir(parents=True, exist_ok=True)
                if physical.exists():
                    if sha256(physical.read_bytes()) != physical_hash:
                        raise ValueError("object hash collision/corruption")
                else:
                    os.replace(tmp_name, physical)
                entry = {"path": relative, "sha256": physical_hash, "bytes": len(data), "rows": len(rows), "logical_sha256": logical_hash}
                atomic_bytes(index_path, canonical(entry))
            finally:
                if os.path.exists(tmp_name):
                    os.unlink(tmp_name)
        partitions.append(entry)
        total += len(rows)
        rows.clear()

    (root / "tmp").mkdir(parents=True, exist_ok=True)
    # Caller must provide a frozen export, sorted by immutable observation key.
    with open(source, "r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            row = json.loads(line)
            key = row.get(key_field)
            if not isinstance(key, str) or not key:
                raise ValueError(f"each row needs nonempty {key_field}")
            if snapshot_kind == "candidate-state":
                if row.get("estado_identidade") != "REFERENCIA":
                    raise ValueError("candidate-state accepts only REFERENCIA rows")
                required_candidate_fields = {"candidate_uuid", "nome_completo", "data_nascimento", "nome_mae", "estado_identidade"}
                if set(row) != required_candidate_fields:
                    raise ValueError("candidate-state row must contain exactly the replay candidate contract fields")
            if previous_key is not None and key <= previous_key:
                raise ValueError("input must be strictly sorted by unique observation_key")
            previous_key = key
            rows.append(row)
            if len(rows) == chunk_size:
                flush()
    flush()
    manifest = {
        "schema_version": SCHEMA_VERSION, "run_id": run_id,
        "snapshot_kind": snapshot_kind, "key_field": key_field,
        "captured_at_utc": datetime.now(timezone.utc).isoformat(),
        "versions": versions, "row_count": total, "partitions": partitions,
        "partition_set_sha256": sha256(canonical(partitions))
    }
    # Reject duplicate run IDs rather than silently overwrite audit evidence.
    manifest_path = root / "manifests" / (run_id + ".json")
    if manifest_path.exists():
        raise FileExistsError("manifest for run already exists")
    atomic_bytes(manifest_path, canonical(manifest))
    return manifest_path


def verify(root, manifest_path):
    root = Path(root).resolve()
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    if manifest["schema_version"] not in SUPPORTED_SCHEMA_VERSIONS:
        raise ValueError("unsupported manifest version")
    if manifest["schema_version"] >= 2:
        kind = manifest.get("snapshot_kind")
        if kind not in SNAPSHOT_KINDS or manifest.get("key_field") != SNAPSHOT_KINDS[kind]:
            raise ValueError("invalid snapshot kind/key contract")
    if sha256(canonical(manifest["partitions"])) != manifest["partition_set_sha256"]:
        raise ValueError("partition set hash mismatch")
    total = 0
    for entry in manifest["partitions"]:
        path = (root / entry["path"]).resolve()
        if not path.is_relative_to(root / "objects"):
            raise ValueError("partition path escapes object store")
        payload = path.read_bytes()
        if len(payload) != entry["bytes"] or sha256(payload) != entry["sha256"]:
            raise ValueError("partition integrity failure")
        total += entry["rows"]
    if total != manifest["row_count"]:
        raise ValueError("row count mismatch")
    return {"run_id": manifest["run_id"], "rows": total, "partitions": len(manifest["partitions"])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", help="snapshot root; defaults to BronzeStorage__RootPath/linkage-snapshots/v1")
    commands = parser.add_subparsers(dest="command", required=True)
    capture_cmd = commands.add_parser("capture")
    capture_cmd.add_argument("--input", required=True, help="frozen NDJSON export sorted by observation_key")
    capture_cmd.add_argument("--run-id", required=True)
    capture_cmd.add_argument("--versions-json", required=True, help="JSON with exact scorer/ruleset/model/input versions")
    capture_cmd.add_argument("--chunk-size", type=int, default=10000)
    capture_cmd.add_argument("--snapshot-kind", choices=sorted(SNAPSHOT_KINDS), default="input-universe")
    verify_cmd = commands.add_parser("verify")
    verify_cmd.add_argument("--manifest", required=True)
    args = parser.parse_args()
    if args.command == "capture":
        versions = json.loads(args.versions_json)
        required = {"scorer_version", "ruleset_version", "model_version", "input_snapshot_id"}
        if not isinstance(versions, dict) or not required.issubset(versions) or any(not versions[k] for k in required):
            parser.error("versions-json requires scorer_version, ruleset_version, model_version, input_snapshot_id")
        result = capture(args.root, args.input, args.run_id, versions, args.chunk_size, args.snapshot_kind)
        print(json.dumps({"manifest": str(result)}))
    else:
        print(json.dumps(verify(args.root, args.manifest)))


if __name__ == "__main__":
    main()

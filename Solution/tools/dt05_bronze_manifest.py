#!/usr/bin/env python3
"""DT-05 referential manifest: reuse immutable Bronze ZIPs; never copy payloads.

The caller must register SQL pins with sp_fixar_bronze_para_linkage before
publishing this manifest. This offline utility verifies bytes, not SQL pins.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

SHA = re.compile(r"^[0-9a-f]{64}$")
KEY = re.compile(r"^sha256/([0-9a-f]{2})/([0-9a-f]{2})/([0-9a-f]{64})\.zip$")


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def check_object(bronze_root, entry):
    key = entry["objeto_chave"]
    sha = entry["payload_sha256"].lower()
    match = KEY.fullmatch(key)
    if not SHA.fullmatch(sha) or not match or match.group(1) != sha[:2] or match.group(2) != sha[2:4] or match.group(3) != sha:
        raise ValueError("invalid content-addressed Bronze key")
    path = (bronze_root / key).resolve()
    if not path.is_relative_to(bronze_root.resolve()):
        raise ValueError("Bronze path escapes root")
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as handle:
        while chunk := handle.read(1024 * 1024):
            digest.update(chunk)
            size += len(chunk)
    if digest.hexdigest() != sha:
        raise ValueError("Bronze object hash mismatch")
    if "bytes" in entry and entry["bytes"] != size:
        raise ValueError("Bronze object length mismatch")
    return {"objeto_chave": key, "payload_sha256": sha, "bytes": size}


def verify(root, manifest, visited=None):
    root = Path(root).resolve()
    manifest = Path(manifest).resolve()
    if not manifest.is_relative_to(root):
        raise ValueError("manifest outside Bronze root")
    visited = set() if visited is None else visited
    if manifest in visited:
        raise ValueError("cyclic manifest ancestry")
    visited.add(manifest)
    data = json.loads(manifest.read_text(encoding="utf-8"))
    parent = data.get("parent")
    inherited_count = 0
    if parent:
        ancestor = (root / parent["path"]).resolve()
        if not ancestor.is_relative_to(root):
            raise ValueError("parent escapes Bronze root")
        if hashlib.sha256(ancestor.read_bytes()).hexdigest() != parent["manifest_sha256"]:
            raise ValueError("parent manifest hash mismatch")
        inherited_count = verify(root, ancestor, visited)
    refs = data["bronze_objects"]
    if data["bronze_set_sha256"] != hashlib.sha256(canonical(refs)).hexdigest():
        raise ValueError("manifest reference hash mismatch")
    for item in refs:
        check_object(root, item)
    return len(refs) + inherited_count


def create(root, refs_file, manifest_path, run_id, versions, parent_manifest=None):
    root = Path(root).resolve()
    refs = json.loads(Path(refs_file).read_text(encoding="utf-8"))
    if not isinstance(refs, list) or (not refs and not parent_manifest):
        raise ValueError("nonempty Bronze references or parent manifest required")
    unique = {}
    for ref in refs:
        item = check_object(root, ref)
        previous = unique.setdefault(item["objeto_chave"], item)
        if previous != item:
            raise ValueError("conflicting Bronze reference")
    parent = None
    if parent_manifest:
        parent_path = Path(parent_manifest).resolve()
        verify(root, parent_path)
        parent_data = json.loads(parent_path.read_text(encoding="utf-8"))
        old = {}
        ancestor_data = parent_data
        while True:
            for item in ancestor_data["bronze_objects"]:
                existing = old.setdefault(item["objeto_chave"], item)
                if existing != item:
                    raise ValueError("conflicting inherited Bronze reference")
            ancestor_parent = ancestor_data.get("parent")
            if not ancestor_parent:
                break
            ancestor_path = (root / ancestor_parent["path"]).resolve()
            ancestor_data = json.loads(ancestor_path.read_text(encoding="utf-8"))
        for key, item in unique.items():
            if key in old and old[key] != item:
                raise ValueError("append-only Bronze object changed")
        unique = {key: item for key, item in unique.items() if key not in old}
        if not parent_path.is_relative_to(root):
            raise ValueError("parent manifest outside Bronze root")
        parent = {"path": parent_path.relative_to(root).as_posix(),
                  "run_id": parent_data["run_id"],
                  "manifest_sha256": hashlib.sha256(parent_path.read_bytes()).hexdigest()}
    ordered = sorted(unique.values(), key=lambda item: item["objeto_chave"])
    required = {"scorer_version", "ruleset_version", "model_version", "input_snapshot_id"}
    if not isinstance(versions, dict) or not required.issubset(versions) or any(not versions[k] for k in required):
        raise ValueError("missing exact historical versions")
    data = {
        "schema_version": 1, "run_id": run_id, "versions": versions,
        "bronze_objects": ordered,
        "bronze_set_sha256": hashlib.sha256(canonical(ordered)).hexdigest(),
        "pin_contract": "identidade.sp_fixar_bronze_para_linkage/v1", "parent": parent
    }
    dest = Path(manifest_path).resolve()
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.exists():
        raise FileExistsError("immutable manifest already exists")
    with tempfile.NamedTemporaryFile(dir=dest.parent, prefix=".dt05-", delete=False) as out:
        tmp = Path(out.name)
        out.write(canonical(data))
        out.flush()
        os.fsync(out.fileno())
    try:
        # Link is create-only: os.replace would silently overwrite an immutable manifest.
        os.link(tmp, dest)
    finally:
        tmp.unlink(missing_ok=True)
    return verify(root, dest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bronze-root", required=True)
    commands = parser.add_subparsers(dest="command", required=True)
    build = commands.add_parser("create")
    build.add_argument("--refs-json", required=True)
    build.add_argument("--manifest", required=True)
    build.add_argument("--run-id", required=True)
    build.add_argument("--parent-manifest")
    build.add_argument("--versions-json", required=True)
    check = commands.add_parser("verify")
    check.add_argument("--manifest", required=True)
    args = parser.parse_args()
    if args.command == "create":
        count = create(args.bronze_root, args.refs_json, args.manifest, args.run_id, json.loads(args.versions_json), args.parent_manifest)
    else:
        count = verify(args.bronze_root, args.manifest)
    print(json.dumps({"verified_bronze_objects": count}))


if __name__ == "__main__":
    main()

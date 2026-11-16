#!/usr/bin/env python3
"""Atomically promote an already validated REF corpus without deleting history.

Requires an existing shared POSIX store and a cooperative lock.
Promotion is automatic only for prevalidated, approved corpus entries.
This utility does NOT attest durability, delete old versions, or start TRAIN.
"""
import argparse
import fcntl
import json
import os
from pathlib import Path
import tempfile


def promote(index_path: Path, fingerprint: str, configuration: str, *, store: Path = None, generation: dict = None, environment: str = "experimental"):
    if environment != "experimental":
        raise ValueError("REF corpus auto-promotion forbidden outside experimental environment; production requires manual PO approval")
    if store is None or not isinstance(generation, dict) or not generation:
        raise ValueError("Physical snapshot store and generation configuration required")
    if any(p.is_symlink() for p in (store, *store.parents)) or not store.is_dir():
        raise ValueError("Unsafe or missing corpus store")
    if len(fingerprint) != 64 or any(c not in "0123456789abcdef" for c in fingerprint):
        raise ValueError("Invalid fingerprint")
    if not configuration:
        raise ValueError("Missing configuration")
    if index_path.is_symlink() or any(p.is_symlink() for p in index_path.parents):
        raise ValueError("Unsafe index path")
    if not index_path.is_file():
        raise ValueError("Existing REF index required")
    lock_path = index_path.parent / ".ref-retention.lock"
    fd = os.open(lock_path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, "r+b") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            if index_path.is_symlink():
                raise ValueError("Unsafe index path")
            data = json.loads(index_path.read_text(encoding="utf-8"))
            if data.get("schema") != "v9-ref-retention-index-v1" or not isinstance(data.get("entries"), list):
                raise ValueError("Invalid retention index")
            matches = [e for e in data["entries"] if e.get("fingerprint") == fingerprint
                       and e.get("configuration") == configuration]
            if len(matches) != 1 or matches[0].get("validated") is not True or matches[0].get("approved") is not True:
                raise ValueError("Target must already be validated and approved")
            from importlib.util import spec_from_file_location, module_from_spec
            spec = spec_from_file_location("retention_plan", Path(__file__).with_name("v9-corpus-retention-plan.py"))
            module = module_from_spec(spec)
            spec.loader.exec_module(module)
            module.plan(data, active_fingerprints=set(), protected_fingerprints=set())
            snapshot_spec = spec_from_file_location("snapshot_verify", Path(__file__).with_name("v9-corpus-snapshot.py"))
            snapshot_module = module_from_spec(snapshot_spec)
            snapshot_spec.loader.exec_module(snapshot_module)
            if snapshot_module.identity(generation) != fingerprint:
                raise ValueError("Generation configuration does not match target fingerprint")
            snapshot_module.verify(store / fingerprint, generation)
            if matches[0]["state"] == "CURRENT":
                return {"status": "ALREADY_CURRENT", "fingerprint": fingerprint}
            for entry in data["entries"]:
                if entry.get("configuration") == configuration and entry["state"] == "CURRENT":
                    entry["state"] = "PREVIOUS"
            matches[0]["state"] = "CURRENT"
            module.plan(data, active_fingerprints=set(), protected_fingerprints=set())
            temp = None
            try:
                with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=index_path.parent,
                                                 prefix=".ref-index-", delete=False) as stream:
                    temp = Path(stream.name)
                    json.dump(data, stream, sort_keys=True, indent=2)
                    stream.write("\n")
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(temp, index_path)
                dirfd = os.open(index_path.parent, os.O_RDONLY | os.O_DIRECTORY)
                try:
                    os.fsync(dirfd)
                finally:
                    os.close(dirfd)
            finally:
                if temp is not None:
                    temp.unlink(missing_ok=True)
            return {"status": "PROMOTED_NO_DELETION", "fingerprint": fingerprint}
    except BaseException:
        # fdopen owns fd only after it succeeds; normal failures close via context.
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--fingerprint", required=True)
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--environment", choices=("experimental", "production"), required=True)
    parser.add_argument("--store", type=Path, required=True)
    parser.add_argument("--generation-config", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(promote(args.index, args.fingerprint, args.configuration,
                             store=args.store,
                             generation=json.loads(args.generation_config.read_text(encoding="utf-8")),
                             environment=args.environment), sort_keys=True))


if __name__ == "__main__":
    main()

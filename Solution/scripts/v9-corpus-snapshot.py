#!/usr/bin/env python3
"""Versioned, fail-closed local corpus store. Storage must be durable across Actions jobs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
import fcntl

VERSION = "v9-corpus-snapshot-v1"
REQUIRED = ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json", "generation-manifest.json")

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def identity(config):
    return hashlib.sha256(json.dumps(
        {"schema": VERSION, "generation": config},
        sort_keys=True, ensure_ascii=False, separators=(",", ":")
    ).encode()).hexdigest()

def snapshot(config, source):
    files = {}
    for name in sorted(REQUIRED):
        p = source / name
        if not p.is_file() or p.is_symlink():
            raise ValueError("Missing or unsafe corpus file: " + name)
        files[name] = {"sha256": digest(p), "size": p.stat().st_size}
    return {"schema": VERSION, "fingerprint": identity(config), "configuration": config, "files": files}

def verify(target, config):
    p = target / "snapshot.json"
    if target.is_symlink() or not p.is_file() or p.is_symlink():
        raise ValueError("Missing or unsafe snapshot")
    recorded = json.loads(p.read_text(encoding="utf-8"))
    if recorded.get("schema") != VERSION or recorded.get("fingerprint") != identity(config):
        raise ValueError("Corpus configuration mismatch")
    if recorded.get("configuration") != config:
        raise ValueError("Corpus configuration payload mismatch")
    if set(recorded.get("files", {})) != set(REQUIRED):
        raise ValueError("Corpus file allowlist mismatch")
    actual = snapshot(config, target)
    if recorded != actual:
        raise ValueError("Corpus content hash or manifest mismatch")
    return recorded

def publish(store, config, source):
    key = identity(config)
    destination = store / key
    if store.is_symlink():
        raise ValueError("Symlink store is forbidden")
    store.mkdir(parents=True, exist_ok=True)
    if store.is_symlink():
        raise ValueError("Symlink store is forbidden")
    if destination.is_symlink():
        raise ValueError("Symlink corpus is forbidden")
    if destination.exists():
        return verify(destination, config)
    # No partial directory is ever exposed at its final address.
    tmp = Path(tempfile.mkdtemp(prefix=".staging-", dir=store))
    try:
        for name in REQUIRED:
            original = source / name
            if original.is_symlink() or not original.is_file():
                raise ValueError("Unsafe source file: " + name)
            shutil.copyfile(original, tmp / name)
        manifest = snapshot(config, tmp)
        (tmp / "snapshot.json").write_text(
            json.dumps(manifest, sort_keys=True, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        # flock serializes competing publishers on the same durable POSIX volume.
        # Do not use this primitive on object stores without equivalent leases.
        lock_path = store / ".publish.lock"
        if lock_path.is_symlink():
            raise ValueError("Unsafe publish lock")
        with open(lock_path, "a+b") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            if destination.exists():
                return verify(destination, config)
            os.rename(tmp, destination)
        return verify(destination, config)
    finally:
        if tmp.exists():
            shutil.rmtree(tmp)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("lookup", "publish"))
    parser.add_argument("--store", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--source", type=Path)
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    if not isinstance(config, dict) or not config:
        parser.error("Generation configuration must be a nonempty object")
    target = args.store / identity(config)
    if args.command == "publish":
        if args.source is None:
            parser.error("--source is required for publish")
        result = publish(args.store, config, args.source)
    else:
        result = verify(target, config)
    print(json.dumps({"corpus": str(target), "fingerprint": result["fingerprint"],
                      "status": "VALIDATED"}, sort_keys=True))

if __name__ == "__main__":
    main()

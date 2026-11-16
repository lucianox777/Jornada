#!/usr/bin/env python3
"""Fail-closed PREPARE orchestration against a durable POSIX corpus volume.

This tool deliberately does not select a GitHub storage provider. The workflow
must not advertise cross-run persistence until that provider is configured.
"""
import argparse
import fcntl
import importlib.util
import json
from pathlib import Path
import subprocess
import shutil
import tempfile
import os
import sys
import stat

spec = importlib.util.spec_from_file_location("v9_corpus_snapshot", Path(__file__).with_name("v9-corpus-snapshot.py"))
snapshot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(snapshot)

def open_preparation_lock(path, *, create=False):
    """Open the actual lock inode without following a final-component symlink."""
    flags = os.O_RDWR | os.O_CREAT if create else os.O_RDONLY
    flags |= os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK
    try:
        fd = os.open(path, flags, 0o600)
    except OSError as exc:
        raise ValueError("Missing or unsafe preparation lock") from exc
    if not stat.S_ISREG(os.fstat(fd).st_mode):
        os.close(fd)
        raise ValueError("Missing or unsafe preparation lock")
    return os.fdopen(fd, "a+b" if create else "rb")


def prepare(store, config, generator, output):
    if not isinstance(config, dict) or not config:
        raise ValueError("Invalid generation configuration")
    if store.is_symlink():
        raise ValueError("Symlink store forbidden")
    store.mkdir(parents=True, exist_ok=True)
    if store.is_symlink():
        raise ValueError("Symlink store forbidden")
    target = store / snapshot.identity(config)
    # A single durable-volume lock protects the entire lookup/generation/publish
    # sequence. The snapshot publisher uses a separate lock for atomic exposure.
    lock_path = store / ".prepare.lock"
    if lock_path.is_symlink():
        raise ValueError("Unsafe preparation lock")
    with open_preparation_lock(lock_path, create=True) as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if target.exists() or target.is_symlink():
            receipt = snapshot.verify(target, config)
            status = "REUSED"
        else:
            if not generator:
                raise ValueError("No generator command configured for cache miss")
            if output.exists() or output.is_symlink():
                raise ValueError("Generation output must be fresh and absent")
            output.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(generator, check=True)
            receipt = snapshot.publish(store, config, output)
            status = "GENERATED"
    return {"status": status, "corpus": str(target),
            "fingerprint": receipt["fingerprint"], "files": receipt["files"]}

def handoff(store, config, destination):
    """Materialize validated corpus on another runner; never trust a bare cache hit."""
    if store.is_symlink() or not store.is_dir():
        raise ValueError("Missing or unsafe corpus store")
    source = store / snapshot.identity(config)
    # Validate while holding the same lock as PREPARE: do not copy a corpus
    # concurrently with a writer on the shared POSIX volume.
    if destination.exists() or destination.is_symlink():
        raise ValueError("Handoff destination must not exist")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.parent.is_symlink():
        raise ValueError("Unsafe handoff parent")
    temporary = Path(tempfile.mkdtemp(prefix=".handoff-", dir=destination.parent))
    try:
        lock_path = store / ".prepare.lock"
        if lock_path.is_symlink() or not lock_path.is_file():
            raise ValueError("Missing or unsafe preparation lock")
        with open_preparation_lock(lock_path) as lock:
            fcntl.flock(lock, fcntl.LOCK_SH)
            snapshot.verify(source, config)
            for name in snapshot.REQUIRED:
                shutil.copyfile(source / name, temporary / name)
            shutil.copyfile(source / "snapshot.json", temporary / "snapshot.json")
        snapshot.verify(temporary, config)
        os.rename(temporary, destination)
        return {"status": "HANDOFF_VERIFIED", "corpus": str(destination),
                "fingerprint": snapshot.identity(config)}
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--store", type=Path, required=True)
    parser.add_argument("--handoff-to", type=Path)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--generation-output", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("generator", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.generator
    if command and command[0] == "--":
        command = command[1:]
    config = json.loads(args.config.read_text(encoding="utf-8"))
    result = prepare(args.store, config, command, args.generation_output)
    if args.handoff_to is not None:
        result["handoff"] = handoff(args.store, config, args.handoff_to)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(result, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("V9_CORPUS_PREPARE_" + result["status"] + " " + result["fingerprint"])

if __name__ == "__main__":
    main()

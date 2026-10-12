#!/usr/bin/env python3
"""Attest and reuse Release binaries ONLY within one exact GitHub Actions run.

This does not reuse cached arbitrary branches, change restore policy, suppress
tests, or replace the independent deterministic / CodeQL builds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import subprocess
import tarfile
from pathlib import Path
from typing import Any

MAX_FILES = 20000
MAX_BYTES = 1_000_000_000
# Runtime dependencies and fixtures copied into the test project's output folder
# are sufficient for dotnet test --no-build. Do not package every project's
# duplicate transitive outputs (previously ~379 MB compressed).
INTEGRATION_OUTPUT = "tests/Jornada.Integration.Tests/bin/Release/net10.0"
BINARY_PATH = re.compile(r"^" + re.escape(INTEGRATION_OUTPUT) + r"/[^/].+")
REQUIRED_OUTPUTS = tuple(INTEGRATION_OUTPUT + "/" + name for name in (
    "Jornada.Integration.Tests.dll",
    "Jornada.Integration.Tests.deps.json",
    "Jornada.Integration.Tests.runtimeconfig.json",
))
SCHEMA = 2


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as source:
        for piece in iter(lambda: source.read(1024 * 1024), b""):
            h.update(piece)
    return h.hexdigest()


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(root.parent), *args], text=True, timeout=15
    ).strip()


def valid_path(relative: str) -> bool:
    parts = relative.split("/")
    return bool(BINARY_PATH.fullmatch(relative)) and all(
        p not in (".", "..", "") for p in parts
    ) and "\\" not in relative and "\x00" not in relative


def lock_digest(root: Path) -> str:
    entries: list[str] = []
    for base in (root / "src", root / "tests"):
        for path in sorted(base.rglob("packages.lock.json")):
            if {"bin", "obj"} & set(path.relative_to(root).parts):
                continue
            if path.is_symlink():
                raise ValueError("Symlinked lockfile")
            entries.append(path.relative_to(root).as_posix() + " " + digest(path))
    if len(entries) < 2:
        raise ValueError("Expected committed/restored NuGet lock graph")
    return hashlib.sha256(("\n".join(entries) + "\n").encode()).hexdigest()


def identity(root: Path) -> dict[str, Any]:
    sha = os.environ.get("GITHUB_SHA", "")
    head = git(root, "rev-parse", "HEAD")
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or sha != head:
        raise ValueError("Action HEAD must match Git checkout SHA exactly")
    run_id = os.environ.get("GITHUB_RUN_ID", "")
    if not run_id.isdecimal():
        raise ValueError("Missing GitHub Actions run ID")
    sdk = subprocess.check_output(["dotnet", "--version"], text=True, timeout=20).strip()
    if sdk != "10.0.112":
        raise ValueError("SDK different from the CI pinned toolchain")
    return {
        "schema": SCHEMA,
        "repository": os.environ.get("GITHUB_REPOSITORY", ""),
        "gitSha": sha,
        "gitTree": git(root, "rev-parse", "HEAD^{tree}"),
        "runId": run_id,
        "sdk": sdk,
        "os": platform.system(),
        "machine": platform.machine(),
        "locksSha256": lock_digest(root),
    }


def release_files(root: Path) -> list[Path]:
    files: list[Path] = []
    base = root / INTEGRATION_OUTPUT
    if base.is_symlink():
        raise ValueError("Refusing symlinked integration output directory")
    for path in base.rglob("*"):
        if path.is_symlink():
            raise ValueError("Refusing symlinked integration test output")
        if path.is_file():
            relative = path.relative_to(root).as_posix()
            if not valid_path(relative):
                raise ValueError(f"Invalid integration test output location: {relative}")
            files.append(path)
    files.sort(key=lambda p: p.relative_to(root).as_posix())
    if len(files) < len(REQUIRED_OUTPUTS) or len(files) > MAX_FILES:
        raise ValueError("Unexpected integration test output file count")
    names = {p.relative_to(root).as_posix() for p in files}
    if any(required not in names for required in REQUIRED_OUTPUTS):
        raise ValueError("Integration test DLL, deps.json and runtimeconfig.json required")
    if sum(p.stat().st_size for p in files) > MAX_BYTES:
        raise ValueError("Compiled release artifacts exceed safety limit")
    return files


def produce(root: Path, manifest: Path, archive: Path, context: dict[str, Any]) -> dict:
    files = release_files(root)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    archive.parent.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, Any]] = []
    with tarfile.open(archive, "w:gz", format=tarfile.PAX_FORMAT) as tar:
        for file in files:
            relative = file.relative_to(root).as_posix()
            info = tar.gettarinfo(str(file), arcname=relative)
            if not info.isfile():
                raise ValueError("Compiled artifact not a regular file")
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            with file.open("rb") as handle:
                tar.addfile(info, handle)
            rows.append({
                "path": relative,
                "size": file.stat().st_size,
                "sha256": digest(file),
            })
    payload = {
        **context,
        "archiveSha256": digest(archive),
        "artifactCount": len(rows),
        "files": rows,
    }
    manifest.write_text(json.dumps(payload, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return payload


def check_payload(root: Path, manifest: Path, archive: Path, context: dict[str, Any]) -> list[tuple[tarfile.TarInfo, Path]]:
    payload = json.loads(manifest.read_text(encoding="utf-8"))
    for key, value in context.items():
        if payload.get(key) != value:
            raise ValueError(f"Build provenance mismatch: {key}")
    if payload.get("archiveSha256") != digest(archive):
        raise ValueError("Compiled archive SHA-256 mismatch")
    rows = payload.get("files")
    if not isinstance(rows, list) or not len(REQUIRED_OUTPUTS) <= len(rows) <= MAX_FILES or payload.get("artifactCount") != len(rows):
        raise ValueError("Compiled artifact manifest size invalid")
    entries = {}
    total_size = 0
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Invalid manifest record")
        name, size, sha = row.get("path"), row.get("size"), row.get("sha256")
        if not isinstance(name, str) or not valid_path(name):
            raise ValueError("Unsafe compiled artifact path")
        if name in entries or type(size) is not int or size < 0 or not re.fullmatch(r"[0-9a-f]{64}", str(sha)):
            raise ValueError("Duplicate or invalid compiled artifact record")
        total_size += size
        entries[name] = (size, sha)
    if total_size > MAX_BYTES:
        raise ValueError("Compiled artifacts exceed safety limit")
    if any(name not in entries for name in REQUIRED_OUTPUTS):
        raise ValueError("Integration test binary and runtime manifests missing")
    selected = []
    with tarfile.open(archive, "r:gz") as tar:
        members = tar.getmembers()
        if len(members) != len(entries):
            raise ValueError("Compiled archive and manifest entries differ")
        for member in members:
            if not member.isfile() or member.name not in entries or not valid_path(member.name):
                raise ValueError("Unsafe or unexpected tar archive member")
            size, sha = entries.pop(member.name)
            if member.size != size:
                raise ValueError("Binary file size differs from manifest")
            handle = tar.extractfile(member)
            if handle is None:
                raise ValueError("Binary file not readable")
            file_hash = hashlib.sha256()
            read_bytes = 0
            while block := handle.read(1024 * 1024):
                read_bytes += len(block)
                if read_bytes > size:
                    raise ValueError("Archive member exceeds declared size")
                file_hash.update(block)
            if read_bytes != size or file_hash.hexdigest() != sha:
                raise ValueError("Binary SHA differs from manifest")
            selected.append((member, root / member.name))
    if entries:
        raise ValueError("Missing compiled files")
    return selected


def consume(root: Path, manifest: Path, archive: Path, context: dict[str, Any]) -> int:
    # Validate all bytes and paths BEFORE creating or replacing any binary.
    validated = check_payload(root, manifest, archive, context)
    validated_hashes = {
        row["path"]: row["sha256"] for row in json.loads(manifest.read_text(encoding="utf-8"))["files"]
    }
    with tarfile.open(archive, "r:gz") as tar:
        for info, destination in validated:
            current = root
            for component in Path(info.name).parts[:-1]:
                current = current / component
                if current.is_symlink():
                    raise ValueError("Symlinked destination directory")
                current.mkdir(exist_ok=True)
            if destination.is_symlink() or (destination.exists() and not destination.is_file()):
                raise ValueError("Invalid extraction target")
            data = tar.extractfile(info)
            if data is None:
                raise ValueError("Archive member disappeared")
            # Archive verified and path controlled. Preserve executable bit for apphost binaries.
            flags = os.O_WRONLY | os.O_CREAT | os.O_TRUNC
            if hasattr(os, "O_NOFOLLOW"):
                flags |= os.O_NOFOLLOW
            fd = os.open(destination, flags, 0o600)
            with os.fdopen(fd, "wb") as target:
                while chunk := data.read(1024 * 1024):
                    target.write(chunk)
            destination.chmod(info.mode & 0o777)
            if digest(destination) != validated_hashes[info.name]:
                raise ValueError("Post-extraction binary hash mismatch")
    return len(validated)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("produce", "consume"))
    parser.add_argument("--manifest", type=Path, default=Path(".local/compiled-release/manifest.json"))
    parser.add_argument("--archive", type=Path, default=Path(".local/compiled-release/release-binaries.tar.gz"))
    options = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if options.operation == "produce":
        result = produce(root, options.manifest, options.archive, identity(root))
        print(f"CI BINARIES: produced {result['artifactCount']} SHA-bound files")
    else:
        count = consume(root, options.manifest, options.archive, identity(root))
        print(f"CI BINARIES: verified and reused {count} exact-run Release files")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Fail-closed synthetic TRAIN artifact transfer for the V9 standard workflow."""
from __future__ import annotations
import argparse
import os
from pathlib import Path, PurePosixPath
import tarfile

COHORT_DIRS = ("people-100k", "validation-people", "corpus")
# Preserve public TRAIN constants consumed by existing regression contracts.
ALLOWED = ("people-100k/",)
REQUIRED = ("people-100k/observacoes.csv",)
MAX_UNCOMPRESSED = 4 * 1024**3
MAX_FILES = 1000


def safe_name(name: str, cohort: str = "people-100k") -> bool:
    p = PurePosixPath(name)
    return (not p.is_absolute() and not any(x in ("..", ".", "") for x in p.parts)
            and (name.rstrip("/") == cohort
                 or name.startswith(cohort + "/")))


def validate_members(members: list[tarfile.TarInfo], cohort: str = "people-100k") -> None:
    if cohort not in COHORT_DIRS:
        raise ValueError("V9_PREPARE_UNSUPPORTED_COHORT")
    if len(members) > MAX_FILES:
        raise ValueError("V9_PREPARE_TOO_MANY_FILES")
    names = set()
    size = 0
    for m in members:
        if not safe_name(m.name, cohort) or not (m.isfile() or m.isdir()):
            raise ValueError("V9_PREPARE_UNSAFE_MEMBER")
        if m.name in names:
            raise ValueError("V9_PREPARE_DUPLICATE_MEMBER")
        names.add(m.name)
        size += m.size
        if size > MAX_UNCOMPRESSED:
            raise ValueError("V9_PREPARE_UNCOMPRESSED_SIZE_LIMIT")
    if cohort + "/observacoes.csv" not in names:
        raise ValueError("V9_PREPARE_MISSING_REQUIRED_FILE")


def pack(root: Path, archive: Path, cohort: str = "people-100k") -> None:
    if cohort not in COHORT_DIRS:
        raise ValueError("V9_PREPARE_UNSUPPORTED_COHORT")
    root = root.resolve(strict=True)
    files = []
    for dirname in (cohort,):
        folder = root / dirname
        if not folder.is_dir() or folder.is_symlink():
            raise ValueError("V9_PREPARE_MISSING_DIRECTORY")
        for item in sorted(folder.rglob("*")):
            if item.is_symlink() or not (item.is_file() or item.is_dir()):
                raise ValueError("V9_PREPARE_UNSAFE_SOURCE")
            files.append(item)
    if not (root / cohort / "observacoes.csv").is_file():
        raise ValueError("V9_PREPARE_MISSING_REQUIRED_FILE")
    members = []
    for item in files:
        info = tarfile.TarInfo(item.relative_to(root).as_posix())
        info.size = item.stat().st_size if item.is_file() else 0
        info.type = tarfile.REGTYPE if item.is_file() else tarfile.DIRTYPE
        members.append(info)
    validate_members(members, cohort)
    archive.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, "w:gz") as tar:
        for item in files:
            tar.add(item, arcname=item.relative_to(root).as_posix(), recursive=False)


def unpack(root: Path, archive: Path, cohort: str = "people-100k") -> None:
    if cohort not in COHORT_DIRS:
        raise ValueError("V9_PREPARE_UNSUPPORTED_COHORT")
    root = root.resolve(strict=True)
    with tarfile.open(archive, "r:gz") as tar:
        members = tar.getmembers()
        validate_members(members, cohort)
        for member in members:
            target = root / member.name
            if target.is_symlink() or any(parent.is_symlink()
                                          for parent in target.parents if parent != root):
                raise ValueError("V9_PREPARE_SYMLINK_DESTINATION")
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                if target.exists():
                    raise ValueError("V9_PREPARE_DESTINATION_EXISTS")
                source = tar.extractfile(member)
                if source is None:
                    raise ValueError("V9_PREPARE_MISSING_FILE_CONTENT")
                with source, target.open("xb") as dest:
                    while block := source.read(1024 * 1024):
                        dest.write(block)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("pack", "unpack"))
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--cohort-dir", choices=COHORT_DIRS, default="people-100k")
    args = parser.parse_args()
    if args.action == "pack":
        if args.out is None:
            parser.error("pack requires --out")
        pack(args.root, args.out, args.cohort_dir)
    else:
        if args.archive is None:
            parser.error("unpack requires --archive")
        unpack(args.root, args.archive, args.cohort_dir)


if __name__ == "__main__":
    main()

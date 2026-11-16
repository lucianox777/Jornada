#!/usr/bin/env python3
"""Immutable REF ZIP packaging and fail-closed physical verification (not scientific TRAIN)."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import shutil
import tempfile
import zipfile

REQUIRED = ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json", "generation-manifest.json")
MAX_UNCOMPRESSED = 2_000_000_000
MAX_ZIP_BYTES = 2_000_000_000

def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def inspect(archive, manifest):
    if archive.is_symlink() or not archive.is_file() or manifest.is_symlink() or not manifest.is_file():
        raise ValueError("Missing or unsafe REF archive/manifest")
    record = json.loads(manifest.read_text(encoding="utf-8"))
    if record.get("schema") != "v9-ref-zip-v1" or set(record.get("files", {})) != set(REQUIRED):
        raise ValueError("Invalid REF ZIP manifest")
    if record.get("zip_sha256") != sha(archive) or record.get("zip_size") != archive.stat().st_size:
        raise ValueError("REF ZIP digest/size mismatch")
    if archive.stat().st_size > MAX_ZIP_BYTES:
        raise ValueError("REF ZIP exceeds safety limit")
    with zipfile.ZipFile(archive) as z:
        infos = z.infolist()
        if len(infos) != len(REQUIRED) or {i.filename for i in infos} != set(REQUIRED):
            raise ValueError("Unexpected, missing or duplicate ZIP entries")
        if sum(i.file_size for i in infos) > MAX_UNCOMPRESSED:
            raise ValueError("Uncompressed REF exceeds safety limit")
        for info in infos:
            if info.flag_bits & 1 or info.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
                raise ValueError("Encrypted or unsupported REF entry")
            mode = info.external_attr >> 16
            if mode and not stat.S_ISREG(mode):
                raise ValueError("Nonregular REF ZIP entry")
            expected = record["files"][info.filename]
            if set(expected) != {"sha256", "size"} or type(expected["size"]) is not int or expected["size"] != info.file_size:
                raise ValueError("REF entry size mismatch")
            digest = hashlib.sha256()
            with z.open(info) as f:
                for block in iter(lambda: f.read(1024 * 1024), b""):
                    digest.update(block)
            if digest.hexdigest() != expected["sha256"]:
                raise ValueError("REF entry digest mismatch")
    return record

def pack(source, archive, manifest):
    if any(p.is_symlink() for p in (source, *source.parents)):
        raise ValueError("Unsafe source directory")
    if not source.is_dir() or {p.name for p in source.iterdir()} != set(REQUIRED):
        raise ValueError("REF source allowlist mismatch")
    if archive.exists() or manifest.exists() or archive.is_symlink() or manifest.is_symlink():
        raise ValueError("REF output already exists: immutable create-only")
    files = {}
    for name in REQUIRED:
        p = source / name
        if p.is_symlink() or not p.is_file():
            raise ValueError("Unsafe REF source entry")
        files[name] = {"sha256": sha(p), "size": p.stat().st_size}
    if any(p.is_symlink() for p in (archive.parent, *archive.parent.parents, manifest.parent, *manifest.parent.parents)):
        raise ValueError("Unsafe REF output parent")
    archive.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
        for name in sorted(REQUIRED):
            z.write(source / name, arcname=name)
    record = {"schema": "v9-ref-zip-v1", "zip_sha256": sha(archive),
              "zip_size": archive.stat().st_size, "files": files}
    manifest.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return inspect(archive, manifest)

def unpack(archive, manifest, destination):
    """Verify before extraction; publish only complete verified directory."""
    record = inspect(archive, manifest)
    if any(p.is_symlink() for p in (destination, *destination.parents)):
        raise ValueError("Unsafe REF extraction path")
    if destination.exists():
        raise ValueError("REF extraction destination already exists")
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".ref-zip-staging-", dir=destination.parent))
    try:
        with zipfile.ZipFile(archive) as z:
            for name in sorted(REQUIRED):
                with z.open(name) as src, (staging / name).open("xb") as out:
                    shutil.copyfileobj(src, out, length=1024 * 1024)
                if sha(staging / name) != record["files"][name]["sha256"]:
                    raise ValueError("REF extraction integrity mismatch")
        staging.rename(destination)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return record

def main():
    p = argparse.ArgumentParser()
    p.add_argument("command", choices=("pack", "verify", "unpack"))
    p.add_argument("--source", type=Path)
    p.add_argument("--destination", type=Path)
    p.add_argument("--zip", type=Path, required=True)
    p.add_argument("--manifest", type=Path, required=True)
    args = p.parse_args()
    if args.command == "pack":
        if args.source is None:
            p.error("pack requires --source")
        result = pack(args.source, args.zip, args.manifest)
    elif args.command == "unpack":
        if args.destination is None:
            p.error("unpack requires --destination")
        result = unpack(args.zip, args.manifest, args.destination)
    else:
        result = inspect(args.zip, args.manifest)
    print(json.dumps({"status": "VALIDATED", "zip_size": result["zip_size"],
                      "zip_sha256": result["zip_sha256"]}, sort_keys=True))

if __name__ == "__main__":
    main()

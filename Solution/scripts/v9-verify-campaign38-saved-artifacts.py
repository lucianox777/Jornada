#!/usr/bin/env python3
"""Check existing campaign #38 archives IN PLACE; no network or copy.

This checker deliberately does not attest media durability, backup, access
controls, retention or recovery, nor does it implement institutional NAS.
Run it on the operator's private destination and retain its local receipt.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import sys
import zipfile

PRESERVER_PATH = Path(__file__).with_name("v9-preserve-campaign38-artifacts.py")
_spec = importlib.util.spec_from_file_location("campaign38_preserver_contract", PRESERVER_PATH)
_pins = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_pins)

MAX_RECEIPT_PATH = 4096
CHUNK = 1024 * 1024


def require(value, message):
    if not value:
        raise ValueError(message)


def inspect_directory(directory):
    """Read 14 canonical ZIPs; never create/modify files in the archive."""
    directory = Path(directory).expanduser()
    require(not directory.is_symlink() and directory.is_dir(),
            "archive directory missing or symlinked")
    require(directory.stat().st_mode & 0o077 == 0,
            "archive directory permissions must exclude group and others (0700)")
    found = []
    expected_files = {
        f"{artifact_id}-{role}.zip"
        for artifact_id, (role, _) in _pins.ARTIFACTS.items()
    }
    actual_zips = {
        item.name for item in directory.iterdir() if item.name.endswith(".zip")
    }
    require(actual_zips == expected_files,
            "incomplete, unexpected, or differently named campaign ZIPs")
    for artifact_id, (role, expected_sha) in _pins.ARTIFACTS.items():
        path = directory / f"{artifact_id}-{role}.zip"
        require(path.is_file() and not path.is_symlink(),
                f"missing or linked ZIP artifact {artifact_id}")
        require(path.stat().st_mode & 0o077 == 0,
                f"ZIP permissions must be private: {artifact_id}")
        h = hashlib.sha256()
        with path.open("rb") as source:
            while block := source.read(CHUNK):
                h.update(block)
        actual_sha = h.hexdigest()
        require(actual_sha == expected_sha,
                f"SHA-256 mismatch for archived ZIP {artifact_id}")
        with zipfile.ZipFile(path) as archive:
            members = archive.infolist()
            require(bool(members) and archive.testzip() is None,
                    f"CRC or ZIP members invalid: {artifact_id}")
            files = [m for m in members if not m.is_dir()]
            require(bool(files), f"empty archive {artifact_id}")
            require(all(
                not (m.external_attr >> 16 & 0o170000) == stat.S_IFLNK
                and not m.filename.startswith("/")
                and not any(part in ("", ".", "..") for part in m.filename.split("/"))
                for m in files
            ), f"unsafe ZIP member metadata: {artifact_id}")
            if artifact_id == 14742452561:
                expected_name = _pins.WINNER_SHA + ".operational.json"
                winner = [m for m in files if m.filename.endswith(expected_name)]
                require(len(winner) == 1,
                        "winner archive lacks unique pinned operational JSON")
                inner = hashlib.sha256()
                with archive.open(winner[0]) as source:
                    while block := source.read(CHUNK):
                        inner.update(block)
                require(inner.hexdigest() == _pins.WINNER_SHA,
                        "internal winner bytes mismatch pinned SHA-256")
        found.append({
            "artifactId": artifact_id,
            "role": role,
            "zipSha256": actual_sha,
            "zipBytes": path.stat().st_size,
            "zipCrcAndMembersVerified": True,
        })
    require(len(found) == 14, "cannot attest incomplete campaign")
    return {
        "schema": "JORNADA_CAMPAIGN38_DESTINATION_BYTE_VERIFICATION_V1",
        "status": "14_OF_14_DESTINATION_BYTES_VERIFIED_NOT_STORAGE_CERTIFIED",
        "repository": _pins.REPO,
        "runId": _pins.RUN_ID,
        "runAttempt": _pins.RUN_ATTEMPT,
        "codeSha": _pins.CODE_SHA,
        "winnerOperationalSha256": _pins.WINNER_SHA,
        "expectedArtifacts": 14,
        "verifiedArtifacts": len(found),
        "zipSha256AndCrcVerified": True,
        "storageDurabilityIndependentlyVerified": False,
        "storageRecoveryTested": False,
        "nasCertified": False,
        "scientificValidation": False,
        "activated": False,
        "E2E_NUCLEO_PASS": False,
        "E2E_INTEGRAL_PASS": False,
        "artifacts": found,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True,
                        help="Private existing archive directory with 14 canonical ZIPs")
    parser.add_argument("--receipt", type=Path,
                        help="Optional new local JSON receipt, never an Actions artifact")
    args = parser.parse_args(argv)
    if args.receipt is not None:
        require(len(str(args.receipt)) <= MAX_RECEIPT_PATH
                and not args.receipt.exists() and not args.receipt.is_symlink()
                and args.receipt.resolve() != args.directory.resolve(),
                "receipt target exists, symlinked, or aliases directory")
    evidence = inspect_directory(args.directory)
    if args.receipt is not None:
        require(args.receipt.parent.is_dir()
                and not args.receipt.parent.is_symlink()
                and args.receipt.parent.stat().st_mode & 0o077 == 0,
                "receipt parent must be an existing private directory")
        with args.receipt.open("x", encoding="utf-8") as out:
            out.write(json.dumps(evidence, sort_keys=True, indent=2) + "\n")
    else:
        print(json.dumps(evidence, sort_keys=True, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        print(f"FAIL_CLOSED_CAMPAIGN38_DESTINATION_VERIFICATION: {exc}", file=sys.stderr)
        raise SystemExit(2)

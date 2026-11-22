#!/usr/bin/env python3
"""Preflight for external synthetic E2E ground truth; NOT an independent linkage oracle."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def validate(zip_path: Path, truth_path: Path) -> dict:
    if zip_path.resolve() == truth_path.resolve():
        raise ValueError("truth and ZIP must be distinct files")
    def reject_duplicate_members(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("duplicate ground truth JSON member")
            result[key] = value
        return result

    truth = json.loads(truth_path.read_text(encoding="utf-8"),
                       object_pairs_hook=reject_duplicate_members,
                       parse_constant=lambda value: (_ for _ in ()).throw(
                           ValueError("nonfinite ground truth JSON value")))
    if not isinstance(truth, dict) or set(truth) != {"zipSha256", "expectedIdentities"}:
        raise ValueError("ground truth must contain only zipSha256 and expectedIdentities")
    hasher = hashlib.sha256()
    with zip_path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    digest = hasher.hexdigest()
    if truth["zipSha256"] != digest:
        raise ValueError("ZIP digest differs from external ground truth binding")
    identities = truth["expectedIdentities"]
    if not isinstance(identities, list) or not identities or not all(
        isinstance(item, str) and item.strip() for item in identities
    ) or len(identities) != len(set(identities)):
        raise ValueError("expectedIdentities must be a nonempty list of unique strings")
    with zipfile.ZipFile(zip_path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("ZIP has duplicate entry names")
        if any(
            name.startswith("/") or name.endswith("//") or "\\" in name or
            any(part in ("", ".", "..") for part in name.rstrip("/").split("/"))
            for name in names
        ):
            raise ValueError("ZIP contains unsafe entry path")
        # Reject encrypted entries and Unix symbolic links: neither is a regular
        # payload file, even when its ZIP path appears harmless.
        for info in archive.infolist():
            if info.flag_bits & 0x1:
                raise ValueError("ZIP contains encrypted entry")
            unix_mode = (info.external_attr >> 16) & 0xFFFF if info.create_system == 3 else 0
            if (unix_mode & 0o170000) == 0o120000:
                raise ValueError("ZIP contains symbolic link")
        # Ground truth must remain outside the HTTP payload, not just outside
        # its expected filename. Compare exact bytes, including renamed entries.
        truth_bytes = truth_path.read_bytes()
        if any(archive.read(info) == truth_bytes for info in archive.infolist() if not info.is_dir()):
            raise ValueError("external ground truth embedded in ZIP")
    return {"status": "PASS", "zipSha256": digest, "expectedIdentityCount": len(identities),
            "groundTruthLocation": "EXTERNAL", "scope": "BOUNDARY_ONLY_NOT_LINKAGE_ORACLE"}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", type=Path, required=True)
    parser.add_argument("--truth", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    receipt = validate(args.zip, args.truth)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

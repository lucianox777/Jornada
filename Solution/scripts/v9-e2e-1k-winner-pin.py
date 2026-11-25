#!/usr/bin/env python3
"""Verify the preserved #38 winner for DEV E2E 1k; never fit or activate."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import stat
import zipfile

RUN_ID = 44970012048
ARTIFACT_ID = 14742452561
ZIP_SHA = "24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7"
WINNER_SHA = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"
ZIP_BYTES = 2009583
WINNER_BYTES = 8769194


def verify(archive: Path, output: Path) -> dict:
    if archive.is_symlink() or not archive.is_file() or archive.stat().st_size != ZIP_BYTES:
        raise ValueError("V9_E2E_WINNER_ZIP_SIZE_OR_TYPE_INVALID")
    with archive.open("rb") as stream:
        raw = stream.read(ZIP_BYTES + 1)
    if len(raw) != ZIP_BYTES or hashlib.sha256(raw).hexdigest() != ZIP_SHA:
        raise ValueError("V9_E2E_WINNER_ZIP_SHA_MISMATCH")
    filename = WINNER_SHA + ".operational.json"
    with zipfile.ZipFile(io.BytesIO(raw)) as package:
        members = package.infolist()
        if len(members) != 1:
            raise ValueError("V9_E2E_WINNER_ZIP_MEMBER_COUNT_INVALID")
        member = members[0]
        mode = stat.S_IFMT(member.external_attr >> 16)
        if (member.filename != filename or member.is_dir()
                or mode not in (0, stat.S_IFREG) or member.flag_bits & 1
                or member.file_size != WINNER_BYTES):
            raise ValueError("V9_E2E_WINNER_ZIP_MEMBER_INVALID")
        with package.open(member) as stream:
            model = stream.read(WINNER_BYTES + 1)
    if len(model) != WINNER_BYTES or hashlib.sha256(model).hexdigest() != WINNER_SHA:
        raise ValueError("V9_E2E_WINNER_OPERATIONAL_SHA_MISMATCH")
    # Never extract archive paths or overwrite a previously selected package.
    output.mkdir(mode=0o700, exist_ok=False)
    with (output / filename).open("xb") as stream:
        stream.write(model)
    with (output / "selection.json").open("x", encoding="utf-8") as stream:
        json.dump({"ArtifactPath": filename, "WinnerSha256": WINNER_SHA}, stream)
    receipt = {"schema": "V9_E2E_1K_WINNER_PIN_V1", "runId": RUN_ID,
               "artifactId": ARTIFACT_ID, "zipSha256": ZIP_SHA,
               "winnerSha256": WINNER_SHA, "zipBytes": ZIP_BYTES,
               "winnerBytes": WINNER_BYTES, "status": "PASS",
               "refitted": False, "activated": False, "scientificTest": False}
    with (output / "winner-pin-receipt.json").open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, sort_keys=True)
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        receipt = verify(args.archive, args.output)
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError):
        raise SystemExit("V9_E2E_WINNER_PIN_REJECTED") from None
    print(json.dumps(receipt, sort_keys=True))


if __name__ == "__main__":
    main()

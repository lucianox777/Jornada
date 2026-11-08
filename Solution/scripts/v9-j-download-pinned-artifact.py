#!/usr/bin/env python3
"""Download frozen GitHub Actions artifacts without leaking API bearer on CDN redirects.

The original artifact bytes MUST match the expected predeclared SHA-256.
"""
import argparse
import hashlib
import os
from pathlib import Path
import re
import urllib.parse
import urllib.request


class DropCrossOriginAuthorization(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(request, fp, code, msg, headers, newurl)
        if redirected is not None and (
            urllib.parse.urlsplit(request.full_url).netloc.lower()
            != urllib.parse.urlsplit(newurl).netloc.lower()
        ):
            for target in (redirected.headers, redirected.unredirected_hdrs):
                for name in list(target):
                    if name.lower() in ("authorization", "proxy-authorization"):
                        del target[name]
        return redirected


def download(artifact_id: str, expected_sha256: str, destination: Path) -> None:
    if not re.fullmatch(r"[1-9][0-9]*", artifact_id):
        raise ValueError("Invalid pinned artifact ID")
    if not re.fullmatch(r"[0-9a-f]{64}", expected_sha256):
        raise ValueError("Invalid pinned artifact SHA-256")
    token = os.environ.get("GH_TOKEN")
    if not token:
        raise ValueError("GH_TOKEN is required")

    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + ".unverified")
    request = urllib.request.Request(
        "https://api.github.com/repos/lucianox777/Jornada/actions/artifacts/"
        + artifact_id + "/zip",
        headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    digest = hashlib.sha256()
    try:
        with urllib.request.build_opener(DropCrossOriginAuthorization()).open(
            request, timeout=180
        ) as response, temporary.open("wb") as output:
            for chunk in iter(lambda: response.read(1024 * 1024), b""):
                digest.update(chunk)
                output.write(chunk)
        if digest.hexdigest() != expected_sha256:
            raise ValueError("Pinned original artifact SHA-256 mismatch")
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-id", required=True)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    download(args.artifact_id, args.expected_sha256, args.output)


if __name__ == "__main__":
    main()

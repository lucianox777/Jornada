#!/usr/bin/env python3
"""Pure fail-closed classifier for the exact API-image APT DNS failure.

No network, Docker, credentials, environment inspection, mutations, or fallback
itself. Exit 0 means the exclusive ephemeral E2E may retry ONE build with host
network; exit 2 means all other build failures remain fatal.
"""
from pathlib import Path
import sys

SIGNATURES = (
    "Could not resolve 'archive.ubuntu.com'",
    "Could not resolve 'security.ubuntu.com'",
    "E: Unable to locate package jq",
    "E: Unable to locate package curl",
    "exit code: 100",
)
MAX_LOG_BYTES = 4_000_000


def is_exact_apt_dns_failure(log: str) -> bool:
    return all(token in log for token in SIGNATURES)


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print("C3.2d: invalid bounded DNS gate invocation", file=sys.stderr)
        return 2
    path = Path(argv[0])
    if not path.is_file() or path.stat().st_size > MAX_LOG_BYTES:
        print("C3.2d: API build log missing or too large; no retry", file=sys.stderr)
        return 2
    try:
        log = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        print("C3.2d: API build log unreadable; no retry", file=sys.stderr)
        return 2
    if not is_exact_apt_dns_failure(log):
        print("C3.2d: API build failure is not the known APT DNS signature; no retry",
              file=sys.stderr)
        return 2
    print("C3.2d: known Ubuntu DNS failure verified; permit one isolated build retry")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

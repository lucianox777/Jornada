#!/usr/bin/env python3
"""Fast, narrowly scoped CI guard against C# declarations swallowed by literal \n.

A source comment such as // note\npublic const string Name = ... is one
physical line to C# and comments out the declaration. This gate detects that
specific regression without treating valid escape sequences in string literals
as errors. It complements, but never replaces, a real dotnet build.
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMMENT_START = re.compile(r"^\s*//|[;{}]\s*//")
DECLARATION = re.compile(
    r"^(?:(?:public|protected|private|internal)\s+"
    r"|(?:namespace|using)\s+[A-Za-z_]"
    r"|(?:class|record|struct|interface|enum)\s+[A-Za-z_]"
    r"|(?:return|throw)\s+[A-Za-z_])"
)


def suspicious_lines(source: str) -> list[int]:
    hits = []
    for lineno, line in enumerate(source.splitlines(), start=1):
        comment_start = COMMENT_START.search(line)
        if not comment_start:
            continue
        # Do not scan C# string literals or regex escapes. Only text that is
        # lexically intended as a one-line comment is a candidate here.
        comment = line[comment_start.end():]
        parts = comment.split(r"\n")
        if len(parts) > 1 and any(DECLARATION.match(part.lstrip()) for part in parts[1:]):
            hits.append(lineno)
    return hits


def main() -> int:
    paths = subprocess.check_output(
        ["git", "-C", str(ROOT), "ls-files", "-z"],
    ).split(b"\0")
    examined = 0
    errors = []
    for raw in paths:
        if not raw:
            continue
        rel = raw.decode("utf-8", errors="surrogateescape")
        if not rel.endswith(".cs"):
            continue
        path = ROOT / rel
        if not path.is_file():
            continue
        examined += 1
        source = path.read_text(encoding="utf-8-sig")
        for line in suspicious_lines(source):
            errors.append(f"{rel}:{line}")
    if errors:
        for error in errors[:40]:
            print(f"C# ESCAPED NEWLINE IN COMMENT: {error}")
        print("Replace literal backslash-n between a // comment and a declaration with a real line break.")
        return 1
    print(f"C# comment escaped-newline gate: OK ({examined} tracked C# files)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

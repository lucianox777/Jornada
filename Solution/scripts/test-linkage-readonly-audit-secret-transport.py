#!/usr/bin/env python3
"""Regression guard for SQL secret transport in read-only linkage audits (#405).

Checks the real entrypoint sources, including restoration on success and failure.
No Docker, SQL instance or credentials are required.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent
ENTRYPOINTS = (
    "local-linkage-evidence-readiness.ps1",
    "local-linkage-triplet-collision-audit.ps1",
)


def check_transport(source: str) -> None:
    # The Docker environment option must receive only the variable's NAME.
    assert re.search(r"\$dockerArgs\s*=\s*@\([^\n]*'-e','SQLCMDPASSWORD'", source), (
        "Docker invocation must request SQLCMDPASSWORD by name"
    )
    assert not re.search(r"'-e',\s*\"?SQLCMDPASSWORD\s*=\s*\$password", source), (
        "SQL secret must not appear in Docker argv"
    )
    assert not re.search(r"sqlcmd[^\n]*\s-P\s+\$password", source, re.I), (
        "sqlcmd -P exposes the secret in process argv"
    )
    required = (
        "$previousPassword=$env:SQLCMDPASSWORD",
        "$env:SQLCMDPASSWORD=$password",
        "$lines=@(& docker @dockerArgs)",
        "if ($null -eq $previousPassword)",
        "Remove-Item Env:SQLCMDPASSWORD",
        "$env:SQLCMDPASSWORD=$previousPassword",
    )
    for marker in required:
        assert marker in source, f"missing scoped environment handling: {marker}"
    assert re.search(
        r"try\s*\{\s*\$env:SQLCMD" r"PASSWORD=\$password\s*"
        r"\$lines=@\(& docker @dockerArgs\)\s*\}\s*finally\s*\{",
        source,
    ), "SQL password must be scoped to Docker invocation with finally"


class ReadOnlyAuditSecretTransportTests(unittest.TestCase):
    def test_real_entrypoints(self):
        for filename in ENTRYPOINTS:
            with self.subTest(filename=filename):
                check_transport((ROOT / filename).read_text(encoding="utf-8-sig"))

    def test_rejects_password_in_argv(self):
        clean = (ROOT / ENTRYPOINTS[0]).read_text(encoding="utf-8-sig")
        compromised = clean.replace(
            "'-e','SQLCMDPASSWORD'",
            "'-e',\"SQLCMDPASSWORD=$password\"",
            1,
        )
        self.assertNotEqual(clean, compromised)
        with self.assertRaises(AssertionError):
            check_transport(compromised)

    def test_rejects_missing_finally(self):
        clean = (ROOT / ENTRYPOINTS[1]).read_text(encoding="utf-8-sig")
        compromised = clean.replace("        finally {", "        if ($false) {", 1)
        self.assertNotEqual(clean, compromised)
        with self.assertRaises(AssertionError):
            check_transport(compromised)


if __name__ == "__main__":
    unittest.main()

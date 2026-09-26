#!/usr/bin/env python3
"""Fail-closed regression guard for the actual cluster entrypoints (#405).

A source-level gate; PowerShell syntax and actual execution are checked separately.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent


def verify_powershell(source: str) -> None:
    assert source.count("exec -T -e SQLCMDPASSWORD sqlserver") == 2
    assert not re.search(r"-P\s+\$password", source)
    assert source.count("$previousPassword = $env:SQLCMDPASSWORD") == 2
    assert source.count("$env:SQLCMDPASSWORD = $password") == 2
    assert source.count("Remove-Item Env:SQLCMDPASSWORD") == 2
    assert source.count("$env:SQLCMDPASSWORD = $previousPassword") == 2
    assert len(re.findall(
        r"try\s*\{\s*\$env:SQLCMDPASSWORD\s*=\s*\$password"
        r"[\s\S]*?exec -T -e SQLCMDPASSWORD sqlserver"
        r"[\s\S]*?\}\s*finally\s*\{",
        source,
    )) == 2, "both SQL calls must scope the environment through finally"


def verify_bash(source: str) -> None:
    assert source.count('SQLCMDPASSWORD="$password" compose exec -T -e SQLCMDPASSWORD sqlserver') == 2
    assert not re.search(r"-P\s+[\"']?\$password", source)
    assert source.count('[[ -n "$password" ]]') >= 2


class ClusterPasswordTransport(unittest.TestCase):
    def test_real_powershell(self) -> None:
        verify_powershell((ROOT / "local-cluster.ps1").read_text(encoding="utf-8-sig"))

    def test_real_bash(self) -> None:
        verify_bash((ROOT / "local-cluster.sh").read_text(encoding="utf-8"))

    def test_rejects_powershell_argv_regression(self) -> None:
        actual = (ROOT / "local-cluster.ps1").read_text(encoding="utf-8-sig")
        broken = actual.replace("-U sa -C", "-U sa -P $password -C", 1)
        self.assertNotEqual(actual, broken)
        with self.assertRaises(AssertionError):
            verify_powershell(broken)

    def test_rejects_missing_environment_restoration(self) -> None:
        actual = (ROOT / "local-cluster.ps1").read_text(encoding="utf-8-sig")
        broken = actual.replace("$env:SQLCMDPASSWORD = $previousPassword", "Write-Host 'not restored'", 1)
        self.assertNotEqual(actual, broken)
        with self.assertRaises(AssertionError):
            verify_powershell(broken)

    def test_rejects_bash_argv_regression(self) -> None:
        actual = (ROOT / "local-cluster.sh").read_text(encoding="utf-8")
        broken = actual.replace("-U sa -C", '-U sa -P "$password" -C', 1)
        self.assertNotEqual(actual, broken)
        with self.assertRaises(AssertionError):
            verify_bash(broken)


if __name__ == "__main__":
    unittest.main()

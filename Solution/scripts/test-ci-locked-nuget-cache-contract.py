#!/usr/bin/env python3
"""DT-10/CI incremental speedup contract: cache packages, never skip gates.

Static check only. It cannot count a cache hit as proof of compilation,
SQL integration, security review or transactional E2E.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
CI = ROOT.parent / ".github/workflows/ci.yml"


def main() -> None:
    data = CI.read_text(encoding="utf-8")
    # A pinned setup-dotnet invocation is the only place cache config belongs.
    matches = list(re.finditer(
        r"(?m)^      - uses: actions/setup-dotnet@[a-f0-9]{40}[^\n]*\n"
        r"        with:\n"
        r"          dotnet-version: '10\.0\.112'\n"
        r"          #[^\n]*\n"
        r"          #[^\n]*\n"
        r"          cache: true\n"
        r"          cache-dependency-path: 'Solution/\*\*/packages\.lock\.json'",
        data,
    ))
    setups = re.findall(r"(?m)^      - uses: actions/setup-dotnet@", data)
    assert len(setups) == 10, f"expected all ten SDK setups, found {len(setups)}"
    assert len(matches) == len(setups), (
        "each .NET setup must cache only NuGet using the complete lockfile set"
    )
    assert "cache: true" in data
    assert "cache-dependency-path: 'Solution/**/packages.lock.json'" in data
    # Never let an apparent package-cache hit become a bypass of the
    # per-job verified lock artifact, restore, build, unit or E2E gates.
    assert data.count("dotnet restore Jornada.sln --locked-mode") >= 6
    assert data.count("name: nuget-lockfiles") >= 6
    assert data.count("dotnet build Jornada.sln --configuration Release --no-restore") >= 3
    assert "TestCategory=DT10Evidence" not in data or "dt10-evidence:" in data
    for gate in ("dependency-lock:", "ddl-upgrade:", "unit:",
                 "deterministic-build:", "security-analysis:",
                 "integration-sql:", "harness-smoke:", "e2e:",
                 "dt10-evidence:"):
        assert "\n  " + gate in data, f"removed mandatory gate {gate}"
    assert "uses: ./.github/workflows/dt10-evidence.yml" in data
    dt10 = (ROOT.parent / ".github/workflows/dt10-evidence.yml").read_text(encoding="utf-8")
    assert "          cache: true\n          cache-dependency-path: 'Solution/**/packages.lock.json'" in dt10
    assert "dotnet restore Jornada.sln --locked-mode" in dt10
    assert "TestCategory=DT10Evidence" in dt10
    assert "--forbid-skipped --minimum-tests 4" in dt10
    assert "name: Upload DT-10 evidence" in dt10
    print("DT10 cache: PASS 11 SDK setups use hash-pinned NuGet package cache; "
          "locked restore, build, evidence and all gates remain")


if __name__ == "__main__":
    main()

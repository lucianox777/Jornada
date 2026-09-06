#!/usr/bin/env python3
"""Static companion to the .NET analyzer build for the v3.80 warning baseline.

It cannot replace Roslyn, but prevents the concrete warning patterns observed in the first
real build from regressing in source packages assembled without a local .NET SDK.
"""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = [*ROOT.joinpath("src").rglob("*.cs"), *ROOT.joinpath("tests/Jornada.Tests").rglob("*.cs"), *ROOT.joinpath("tests/Jornada.Integration.Tests").rglob("*.cs")]
PROPS = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
EDITOR = (ROOT / ".editorconfig").read_text(encoding="utf-8")


def fail(message: str) -> None:
    raise SystemExit("ANALYZER CLEANLINESS GATE: FAIL: " + message)


def main() -> None:
    if "<CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>" not in PROPS:
        fail("CodeAnalysisTreatWarningsAsErrors deve permanecer true")

    accepted = {"CA1014", "CA1707", "CA1848", "CA1859", "CA1861", "CA1822"}
    for code in accepted:
        if f"dotnet_diagnostic.{code}.severity = none" not in EDITOR:
            fail(f"decisão explícita ausente para {code}")

    combined = "\n".join(p.read_text(encoding="utf-8") for p in SRC)

    # CA1305 concrete patterns observed: object conversions and fixed ISO parsing must be invariant.
    for p in SRC:
        text = p.read_text(encoding="utf-8")
        lines=text.splitlines()
        for index, line in enumerate(lines):
            lineno=index+1
            window=" ".join(lines[index:index+5])
            if re.search(r"Convert\.To(?:Int32|Int64|Boolean|String|DateTime)\(", line) and "InvariantCulture" not in window:
                fail(f"{p.relative_to(ROOT)}:{lineno}: Convert sem InvariantCulture")
            if "DateTimeOffset.Parse(" in line and "InvariantCulture" not in window:
                fail(f"{p.relative_to(ROOT)}:{lineno}: DateTimeOffset.Parse sem InvariantCulture")
            if re.search(r"\.ToString\(\"yyyy-MM-dd\"\)", line):
                fail(f"{p.relative_to(ROOT)}:{lineno}: DateOnly.ToString sem provider")
            # Contains(char)/IndexOf(char) are ordinal by definition and CA1847 prefers char overloads.
            # String overloads still need an explicit comparison mode.
            if re.search(r'\.(?:Contains|IndexOf)\("[^"]*"\)', line) and "StringComparison." not in line:
                fail(f"{p.relative_to(ROOT)}:{lineno}: comparação string sem StringComparison")

    if "await base.DisposeAsync();" not in (ROOT / "src/Jornada.Ingestion/IngestionPackageInspector.cs").read_text(encoding="utf-8"):
        fail("DecompressedLimitStream.DisposeAsync não chama base.DisposeAsync")

    defaults = re.compile(r"public\s+(?:bool|int|long|decimal)\s+\w+\s*\{\s*get;\s*init;\s*\}\s*=\s*(?:false|0|0m)\s*;")
    for p in ROOT.joinpath("src").rglob("*.cs"):
        if defaults.search(p.read_text(encoding="utf-8")):
            fail(f"inicializador default redundante em {p.relative_to(ROOT)}")

    # New serializer options are allowed only where cached behind a static field/factory.
    direct_call = re.compile(r"JsonSerializer\.(?:Serialize|Deserialize)[^;\n]*new\s+JsonSerializerOptions")
    for p in SRC:
        if direct_call.search(p.read_text(encoding="utf-8")):
            fail(f"JsonSerializerOptions alocado diretamente em operação em {p.relative_to(ROOT)}")

    print(f"ANALYZER CLEANLINESS GATE: OK ({len(SRC)} C# files; accepted={','.join(sorted(accepted))})")


if __name__ == "__main__":
    main()

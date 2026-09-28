#!/usr/bin/env python3
"""DT-16: exclusive membership and full coverage of five specialized solutions."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAMES = ("Core", "Runtime", "Linkage", "Dev", "Tests")

def members(path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    return [
        line.split('"')[5].replace(chr(92), "/")
        for line in lines
        if line.startswith("Project(") and ".csproj" in line
    ]

def main():
    baseline = members(ROOT / "Jornada.sln")
    assert len(baseline) == 21 and len(set(baseline)) == 21, "unexpected monolithic baseline"
    seen = {}
    for name in NAMES:
        path = ROOT / f"Jornada.{name}.sln"
        items = members(path)
        assert items, f"{path.name} is empty"
        for item in items:
            assert (ROOT / item).is_file(), f"missing csproj: {item}"
            assert item not in seen, f"duplicate project: {item} ({seen.get(item)}, {name})"
            seen[item] = name
    assert set(seen) == set(baseline), f"missing={set(baseline)-set(seen)} extra={set(seen)-set(baseline)}"
    print(f"DT-16: PASS; {len(seen)} projects, {len(NAMES)} exclusive solutions")

if __name__ == "__main__":
    main()

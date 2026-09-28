#!/usr/bin/env python3
"""DT-16: exclusive membership and full coverage of the five specialized solutions."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
NAMES = ("Core", "Runtime", "Linkage", "Dev", "Tests")
PROJECT = re.compile(r'^Project\\("\\{FAE04EC0[^\\n]+? = "[^"]+", "([^"]+\\.csproj)"', re.M)

def members(path):
    return [p.replace('\\\\', '/') for p in PROJECT.findall(path.read_text(encoding='utf-8-sig'))]

def main():
    baseline = members(ROOT / 'Jornada.sln')
    assert len(baseline) == 21 and len(set(baseline)) == 21, 'unexpected monolithic baseline'
    seen = {}
    for name in NAMES:
        path = ROOT / f'Jornada.{name}.sln'
        items = members(path)
        assert items, f'{path.name} is empty'
        for item in items:
            assert (ROOT / item).is_file(), f'missing csproj: {item}'
            assert item not in seen, f'duplicate project: {item} ({seen.get(item)}, {name})'
            seen[item] = name
    assert set(seen) == set(baseline), f'missing={set(baseline)-set(seen)} extra={set(seen)-set(baseline)}'
    print(f'DT-16: PASS; {len(seen)} projects, {len(NAMES)} exclusive solutions')

if __name__ == '__main__':
    main()

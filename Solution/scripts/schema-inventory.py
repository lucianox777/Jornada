#!/usr/bin/env python3
from __future__ import annotations
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DB = ROOT / 'database'
TABLE_RE = re.compile(r'CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+([\[\]A-Za-z0-9_\.]+)', re.IGNORECASE)


def names(path: Path) -> set[str]:
    text = path.read_text(encoding='utf-8-sig')
    out = set()
    for m in TABLE_RE.finditer(text):
        name = m.group(1).replace('[', '').replace(']', '').lower()
        out.add(name)
    return out


def main() -> None:
    baseline = names(DB / 'Jornada_Fase1.sql')
    progressive = names(DB / 'Jornada_Identidade_Progressiva.sql')
    migrations: dict[str, list[str]] = {}
    migration_tables: set[str] = set()
    for path in sorted((DB / 'migrations').glob('*.sql')):
        if 'Smoke' in path.name or 'Schema_Consolidation' in path.name:
            continue
        found = names(path)
        if found:
            migrations[path.name] = sorted(found)
            migration_tables |= found

    complete = baseline | progressive | migration_tables
    extra = (progressive | migration_tables) - baseline
    result = {
        'baselineTableCount': len(baseline),
        'progressiveTableCount': len(progressive),
        'migrationDistinctTableCount': len(migration_tables),
        'completeTableCount': len(complete),
        'tablesOutsideLegacyBaseline': sorted(extra),
        'completeTables': sorted(complete),
        'migrationTablesByFile': migrations,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()

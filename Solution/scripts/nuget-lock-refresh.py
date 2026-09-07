#!/usr/bin/env python3
"""Refresh a project-reference-only NuGet graph with SDK-generated evidence."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SDK = '8.0.424'
EXPECTED_COUNT = 18
MANIFEST = ROOT / 'config/release/nuget-lock-provenance.json'
PROJECT = 'src/Jornada.Operational.Sql/packages.lock.json'
CONTRACTS = 'jornada.contracts'
OPERATIONAL = 'jornada.operational.sql'


def run(*args: str) -> str:
    result = subprocess.run(args, cwd=ROOT, check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout, end='')
    return result.stdout


def fail(message: str) -> None:
    raise RuntimeError(message)


def locks() -> dict[str, bytes]:
    files = sorted(p for p in ROOT.rglob('packages.lock.json')
                   if not {'bin', 'obj', '.local'} & set(p.parts))
    if len(files) != EXPECTED_COUNT:
        fail(f'Expected {EXPECTED_COUNT} locks, found {len(files)}.')
    return {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in files}


def graph(data: bytes) -> dict:
    return json.loads(data)['dependencies']


def packages(data: bytes) -> dict:
    return {tfm: {name.lower(): entry for name, entry in entries.items()
                  if entry.get('type', '').lower() != 'project'}
            for tfm, entries in graph(data).items()}


def fingerprint(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> int:
    if run('dotnet', '--version').strip() != SDK:
        fail('Active SDK must be exactly 8.0.424.')
    if run('git', 'status', '--porcelain').strip():
        fail('Refresh requires a clean checkout.')
    before = locks()
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    if sorted(r['path'] for r in manifest['locks']) != sorted(before):
        fail('Baseline provenance inventory is incomplete.')
    for row in manifest['locks']:
        if row['sha256'] != fingerprint(before[row['path']]):
            fail(f'Baseline provenance mismatch: {row["path"]}')
    if PROJECT not in before:
        fail('Operational SQL lock missing.')
    run('dotnet', 'restore', 'Jornada.sln', '--use-lock-file', '--force-evaluate')
    after = locks()
    if before.keys() != after.keys():
        fail('The project inventory changed unexpectedly.')
    changes = []
    for path in before:
        if packages(before[path]) != packages(after[path]):
            fail(f'Package version, content hash, or metadata drift: {path}')
        if before[path] != after[path]:
            changes.append(path)
        for tfm, entries in graph(after[path]).items():
            if OPERATIONAL in entries:
                dependencies = entries[OPERATIONAL].get('dependencies', {})
                if 'Jornada.Contracts' in dependencies and dependencies['Jornada.Contracts'] != '[1.0.0, )':
                    fail(f'Unexpected Contracts project range: {path} ({tfm})')
    for tfm, entries in graph(after[PROJECT]).items():
        if CONTRACTS not in entries or entries[CONTRACTS].get('type') != 'Project':
            fail(f'Operational SQL direct Contracts reference missing: {tfm}')
    if not changes:
        fail('No lock change detected; refusing to fabricate a refresh.')
    print('Project-reference-only lock changes:', *changes, sep='\n  ')
    run('dotnet', 'restore', 'Jornada.sln', '--use-lock-file', '--force-evaluate')
    if locks() != after:
        fail('A second force-evaluate changed the generated lock graph.')
    run('dotnet', 'restore', 'Jornada.sln', '--locked-mode')
    out = ROOT / '.local/nuget-lock-refresh'
    out.mkdir(parents=True, exist_ok=True)
    run('python3', 'scripts/nuget-lock-gate.py', '--root', '.', '--summary', str(out / 'lock-summary.json'))
    summary = json.loads((out / 'lock-summary.json').read_text(encoding='utf-8'))
    if summary['projectCount'] != EXPECTED_COUNT:
        fail('NuGet structural gate did not cover all projects.')
    evidence = {'sourceCommit': run('git', 'rev-parse', 'HEAD').strip(),
                'githubRunId': os.environ.get('GITHUB_RUN_ID') or None,
                'githubRunAttempt': os.environ.get('GITHUB_RUN_ATTEMPT') or None,
                'sdk': SDK, 'scope': 'PROJECT_REFERENCE_ONLY',
                'changedLocks': changes, 'packageGraphUnchanged': True,
                'forceEvaluateReproducible': True, 'lockedRestore': 'PASS',
                'combinedSha256': summary['combinedSha256']}
    manifest['lockGraphGeneration']['diagnosticRunId'] = os.environ.get('GITHUB_RUN_ID') or 'LOCAL_REFRESH'
    manifest['lockGraphGeneration']['combinedSha256'] = summary['combinedSha256']
    manifest['lockGraphGeneration']['lockCount'] = EXPECTED_COUNT
    manifest['lockGraphGeneration']['nugetLockGate'] = 'PASS'
    manifest['lockGraphRefresh'] = evidence
    for row in manifest['locks']:
        row['sha256'] = row['sourceSha256'] = fingerprint(after[row['path']])
        if row['path'] in changes:
            row['note'] = 'Projeto/lock regenerado e reproduzido pelo SDK 8.0.424; somente grafo de ProjectReference alterado. Evidência: lockGraphRefresh.'
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    run('python3', 'scripts/nuget-lock-provenance-gate.py', '--root', '.', '--summary', str(out / 'provenance-summary.json'))
    # Git normalmente devolve caminhos relativos à raiz do repositório,
    # mesmo quando o processo está em Solution. --relative fixa o escopo.
    changed = set(run('git', 'diff', '--name-only', '--relative').splitlines())
    allowed = {'config/release/nuget-lock-provenance.json', *changes}
    if not changed or changed - allowed:
        fail(f'Unexpected worktree changes: {sorted(changed - allowed)}')
    (out / 'refresh-summary.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
    print('NuGet refresh verified; ready for a separate, scoped commit.')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError) as exc:
        print(f'NUGET LOCK REFRESH: FAIL: {exc}', file=sys.stderr)
        raise SystemExit(1)

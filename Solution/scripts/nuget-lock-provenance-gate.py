#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path


def fail(msg: str) -> None:
    raise SystemExit(f'NUGET LOCK PROVENANCE GATE: FAIL: {msg}')


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    ap=argparse.ArgumentParser(description='Valida a declaração de origem dos packages.lock.json sem fingir que uma inspeção estática prova dotnet restore.')
    ap.add_argument('--root',default='.')
    ap.add_argument('--manifest',default='config/release/nuget-lock-provenance.json')
    ap.add_argument('--summary')
    a=ap.parse_args()
    root=Path(a.root).resolve(); manifest=(root/a.manifest).resolve()
    data=json.loads(manifest.read_text(encoding='utf-8'))
    if data.get('schemaVersion') != 1: fail('schemaVersion inesperado')
    if data.get('release') != 'v3.90' or data.get('baseNormativa') != 'v3.62' or data.get('solutionSchema') != 'v3.68':
        fail('versões declaradas inesperadas')
    env=data.get('packagingEnvironment') or {}
    if env.get('nugetRestoreExecuted') is not False or data.get('assurance') != 'NO_RESTORE_CLAIMED':
        fail('empacotamento deve declarar explicitamente que não executou restore NuGet')
    rows=data.get('locks') or []
    listed={r.get('path'):r for r in rows if r.get('path')}
    actual=sorted(p.relative_to(root).as_posix() for p in root.rglob('packages.lock.json') if '.local' not in p.parts and 'obj' not in p.parts and 'bin' not in p.parts)
    if sorted(listed) != actual:
        fail(f'inventário de locks diverge; manifesto={sorted(listed)} atual={actual}')
    pending=[]
    for rel in actual:
        row=listed[rel]; p=root/rel
        if sha256(p) != str(row.get('sha256','')).lower(): fail(f'SHA atual diverge: {rel}')
        origin=row.get('origin')
        if origin not in {'INHERITED_UNCHANGED_FROM_V3.84','INHERITED_PENDING_FROM_V3.84'}:
            fail(f'origem desconhecida para {rel}: {origin}')
        if row.get('sourceSha256') != row.get('sha256'):
            fail(f'lock declarado como herdado não é byte-a-byte idêntico à origem: {rel}')
        if origin == 'INHERITED_PENDING_FROM_V3.84':
            if row.get('verificationStatus') != 'PENDING_TRUSTED_DOTNET_RESTORE':
                fail(f'lock pendente herdado sem status pendente: {rel}')
            pending.append(rel)
    expected_pending={'tests/Jornada.Tests/packages.lock.json','tests/Jornada.Integration.Tests/packages.lock.json'}
    if set(pending) != expected_pending: fail(f'locks pendentes herdados inesperados: {pending}')
    req='\n'.join(data.get('promotionRequirements') or [])
    if 'dotnet restore Jornada.sln --locked-mode' not in req: fail('promoção não exige locked restore')
    summary={'status':'PASS','lockCount':len(actual),'pendingInheritedLocks':pending,'assurance':data.get('assurance')}
    if a.summary:
        out=Path(a.summary); out.parent.mkdir(parents=True,exist_ok=True); out.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'NUGET LOCK PROVENANCE GATE: OK ({len(actual)} locks; {len(pending)} pendentes herdados da v3.84)')
    return 0

if __name__=='__main__':
    raise SystemExit(main())

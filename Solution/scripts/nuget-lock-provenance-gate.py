#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, xml.etree.ElementTree as ET
from pathlib import Path

RELEASE='v4.05'
SDK='8.0.424'
ORIGIN='REGENERATED_OR_VERIFIED_V405_SDK_8_0_424'
STATUS='CI_FORCE_EVALUATE_AND_LOCK_GATE_PASS_V405'
ASSURANCE='CI_REGENERATED_AND_REPRODUCIBLE_LOCK_GRAPH'
GRAPH='SDK_8_0_424_FORCE_EVALUATED_NO_DIFF_THEN_LOCKED_MODE'

def fail(msg: str) -> None:
    raise SystemExit(f'NUGET LOCK PROVENANCE GATE: FAIL: {msg}')

def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main() -> int:
    ap=argparse.ArgumentParser(description='Valida o grafo NuGet reproduzível da Jornada v4.05.')
    ap.add_argument('--root', default='.')
    ap.add_argument('--manifest', default='config/release/nuget-lock-provenance.json')
    ap.add_argument('--summary')
    a=ap.parse_args()
    root=Path(a.root).resolve()
    data=json.loads((root/a.manifest).read_text(encoding='utf-8'))
    if data.get('schemaVersion') != 1 or data.get('release') != RELEASE:
        fail('schema/release inesperado')
    if data.get('baseNormativa') != 'v3.64' or data.get('solutionSchema') != 'v3.69':
        fail('versões normativas inesperadas')
    env=data.get('packagingEnvironment') or {}
    if env.get('nugetRestoreExecuted') is not True or env.get('dotnetAvailable') is not True:
        fail('proveniência v4.05 deve registrar regeneração real via dotnet')
    if data.get('assurance') != ASSURANCE or data.get('currentGraphVerification') != GRAPH:
        fail('assurance/grafo v4.05 inesperado')
    if data.get('pendingLockCount') != 0:
        fail('existem locks pendentes')
    gen=data.get('lockGraphGeneration') or {}
    if gen.get('sdk') != SDK or gen.get('command') != 'dotnet restore Jornada.sln --use-lock-file --force-evaluate':
        fail('geração de locks não fixa SDK/comando canônicos')
    if gen.get('nugetLockGate') != 'PASS' or gen.get('lockCount') != 15:
        fail('evidência de geração dos locks incompleta')

    actual=sorted(
        p.relative_to(root).as_posix() for p in root.rglob('packages.lock.json')
        if '.local' not in p.parts and 'obj' not in p.parts and 'bin' not in p.parts
    )
    listed={r.get('path'):r for r in (data.get('locks') or []) if r.get('path')}
    if sorted(listed) != actual:
        fail(f'inventário diverge; manifesto={sorted(listed)} atual={actual}')
    for rel in actual:
        row=listed[rel]
        current=sha256(root/rel)
        if row.get('sha256') != current or row.get('sourceSha256') != current:
            fail(f'SHA divergente: {rel}')
        if row.get('origin') != ORIGIN or row.get('verificationStatus') != STATUS:
            fail(f'proveniência/status inesperado: {rel}')

    projects={p.stem.lower():p for p in root.rglob('*.csproj')}
    def deps(project_path: Path) -> dict[str,str]:
        xml=ET.parse(project_path).getroot(); out={}
        for node in xml.findall('.//ProjectReference'):
            include=node.attrib.get('Include')
            if include: out[Path(include.replace('\\','/')).stem]='[1.0.0, )'
        for node in xml.findall('.//PackageReference'):
            name=node.attrib.get('Include'); version=node.attrib.get('Version') or node.findtext('Version')
            if name and version: out[name]=f'[{version}, )'
        return dict(sorted(out.items(), key=lambda item:item[0].lower()))
    for rel in actual:
        lock=json.loads((root/rel).read_text(encoding='utf-8'))
        for graph in (lock.get('dependencies') or {}).values():
            for project_key, entry in graph.items():
                if not isinstance(entry,dict) or entry.get('type') != 'Project': continue
                project=projects.get(project_key.lower())
                if project is not None and entry.get('dependencies',{}) != deps(project):
                    fail(f'metadata Project diverge do csproj: {rel} -> {project_key}')

    req='\n'.join(data.get('promotionRequirements') or [])
    for token in ('8.0.424','--force-evaluate','nuget-lock-provenance-gate.py','--locked-mode','Unit + Integration da v4.05'):
        if token not in req: fail(f'promoção não exige: {token}')
    summary={'status':'PASS','release':RELEASE,'lockCount':len(actual),'sdk':SDK,'assurance':ASSURANCE}
    if a.summary:
        out=Path(a.summary); out.parent.mkdir(parents=True,exist_ok=True)
        out.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'NUGET LOCK PROVENANCE GATE: OK ({len(actual)} locks; SDK {SDK}; grafo reproduzível)')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())

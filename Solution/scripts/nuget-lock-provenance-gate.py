#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, re, xml.etree.ElementTree as ET
from pathlib import Path

RELEASE='v4.05'; SDK='8.0.424'; SOLUTION_SCHEMA='v3.70'
ORIGIN='REGENERATED_OR_VERIFIED_V405_SDK_8_0_424'; STATUS='CI_FORCE_EVALUATE_AND_LOCK_GATE_PASS_V405'
ASSURANCE='CI_REGENERATED_AND_REPRODUCIBLE_LOCK_GRAPH'; GRAPH='SDK_8_0_424_FORCE_EVALUATED_NO_DIFF_THEN_LOCKED_MODE'
ATTESTED_LOCK_COUNT=18
POST_RELEASE_ALIASES={'src/Jornada.Ensaio/packages.lock.json':'a46daca29140d77a81a0d8d76c1ba605c15e95dd603edbf594dfb26e8a685fa2'}
SHA256_RE=re.compile(r'^[0-9a-f]{64}$')

def fail(msg): raise SystemExit(f'NUGET LOCK PROVENANCE GATE: FAIL: {msg}')
def sha256(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',default='.');ap.add_argument('--manifest',default='config/release/nuget-lock-provenance.json');ap.add_argument('--summary');a=ap.parse_args();root=Path(a.root).resolve();data=json.loads((root/a.manifest).read_text(encoding='utf-8'))
    if data.get('schemaVersion')!=1 or data.get('release')!=RELEASE: fail('schema/release inesperado')
    if data.get('baseNormativa')!='v3.64' or data.get('solutionSchema')!=SOLUTION_SCHEMA: fail('versões normativas inesperadas')
    env=data.get('packagingEnvironment') or {}
    if env.get('nugetRestoreExecuted') is not True or env.get('dotnetAvailable') is not True: fail('proveniência v4.05 deve registrar regeneração real via dotnet')
    if data.get('assurance')!=ASSURANCE or data.get('currentGraphVerification')!=GRAPH or data.get('pendingLockCount')!=0: fail('assurance/grafo/pending inesperado')
    gen=data.get('lockGraphGeneration') or {}
    if gen.get('sdk')!=SDK or gen.get('command')!='dotnet restore Jornada.sln --use-lock-file --force-evaluate': fail('geração de locks não fixa SDK/comando canônicos')
    if gen.get('nugetLockGate')!='PASS' or gen.get('lockCount')!=ATTESTED_LOCK_COUNT: fail('evidência histórica v4.05 incompleta')

    actual=sorted(p.relative_to(root).as_posix() for p in root.rglob('packages.lock.json') if '.local' not in p.parts and 'obj' not in p.parts and 'bin' not in p.parts)
    listed={r.get('path'):r for r in (data.get('locks') or []) if r.get('path')}
    expected=sorted(set(listed)|set(POST_RELEASE_ALIASES))
    if expected!=actual: fail(f'inventário diverge; atestado+aliases={expected} atual={actual}')

    candidate=data.get('candidateGraph') or {}
    overrides={}
    if candidate:
        if candidate.get('candidate')!='v5.00' or candidate.get('status')!='CI_FORCE_EVALUATED_LOCK_GRAPH':
            fail('candidateGraph inesperado')
        if candidate.get('sdk')!=SDK or candidate.get('lockCount')!=len(actual):
            fail('candidateGraph não fixa SDK/quantidade corrente')
        if not SHA256_RE.fullmatch(str(candidate.get('combinedSha256') or '')):
            fail('candidateGraph sem combinedSha256 válido')
        raw_overrides=candidate.get('overrides') or []
        for row in raw_overrides:
            path=row.get('path'); digest=row.get('sha256')
            if not path or path in overrides or path not in actual or not SHA256_RE.fullmatch(str(digest or '')):
                fail(f'override candidato inválido: {row}')
            overrides[path]=digest
        if candidate.get('changedLockCount')!=len(overrides):
            fail('candidateGraph changedLockCount divergente')

    current_hashes={}
    for rel in actual:
        current=sha256(root/rel); current_hashes[rel]=current
        if rel in overrides:
            if current!=overrides[rel]: fail(f'SHA candidato divergente: {rel}')
            continue
        if rel in POST_RELEASE_ALIASES:
            if current!=POST_RELEASE_ALIASES[rel]: fail(f'SHA do alias pós-release diverge: {rel}')
            continue
        row=listed[rel]
        if row.get('sha256')!=current or row.get('sourceSha256')!=current: fail(f'SHA divergente: {rel}')
        if row.get('origin')!=ORIGIN or row.get('verificationStatus')!=STATUS: fail(f'proveniência/status inesperado: {rel}')

    if candidate:
        combined='\n'.join(f"{current_hashes[rel]}  {rel}" for rel in actual).encode()
        current_combined=hashlib.sha256(combined).hexdigest()
        if current_combined!=candidate.get('combinedSha256'):
            fail(f'combinedSha256 candidato divergente: manifesto={candidate.get("combinedSha256")} atual={current_combined}')

    projects={p.stem.lower():p for p in root.rglob('*.csproj')}
    def deps(project_path):
        xml=ET.parse(project_path).getroot();out={}
        for node in xml.findall('.//ProjectReference'):
            include=node.attrib.get('Include')
            if include: out[Path(include.replace('\\','/')).stem]='[1.0.0, )'
        for node in xml.findall('.//PackageReference'):
            name=node.attrib.get('Include');version=node.attrib.get('Version') or node.findtext('Version')
            if name and version: out[name]=f'[{version}, )'
        return dict(sorted(out.items(),key=lambda item:item[0].lower()))
    for rel in actual:
        lock=json.loads((root/rel).read_text(encoding='utf-8'))
        for graph in (lock.get('dependencies') or {}).values():
            for project_key,entry in graph.items():
                if not isinstance(entry,dict) or entry.get('type')!='Project': continue
                project=projects.get(project_key.lower())
                if project is None: fail(f'entrada Project sem csproj correspondente: {rel} -> {project_key}')
                declared=deps(project);observed=entry.get('dependencies',{}) or {};unexpected={k:v for k,v in observed.items() if declared.get(k)!=v}
                if unexpected: fail(f'metadata Project divergente: {rel} -> {project_key}: {unexpected}')

    req='\n'.join(data.get('promotionRequirements') or [])
    for token in ('8.0.424','--force-evaluate','nuget-lock-provenance-gate.py','--locked-mode','Unit + Integration da v4.05'):
        if token not in req: fail(f'promoção não exige: {token}')

    summary={'status':'PASS','release':RELEASE,'solutionSchema':SOLUTION_SCHEMA,'lockCount':len(actual),
             'attestedLockCount':ATTESTED_LOCK_COUNT,'postReleaseAliases':POST_RELEASE_ALIASES,
             'candidateOverrideCount':len(overrides),'candidate':candidate.get('candidate') if candidate else None,
             'sdk':SDK,'assurance':ASSURANCE}
    if a.summary:
        out=Path(a.summary);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'NUGET LOCK PROVENANCE GATE: OK ({len(actual)} locks; {len(overrides)} overrides candidatos; histórico v4.05 preservado)')
    return 0
if __name__=='__main__': raise SystemExit(main())

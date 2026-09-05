#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, re, sys
from pathlib import Path
import xml.etree.ElementTree as ET

EXACT = re.compile(r"^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$")


def package_refs(project: Path) -> dict[str,str]:
    root = ET.parse(project).getroot()
    result = {}
    for node in root.iter():
        if node.tag.split('}')[-1] != 'PackageReference':
            continue
        name = node.attrib.get('Include') or node.attrib.get('Update')
        version = node.attrib.get('Version')
        if version is None:
            for c in node:
                if c.tag.split('}')[-1] == 'Version' and c.text:
                    version = c.text.strip()
                    break
        if not name:
            continue
        if not version or not EXACT.fullmatch(version):
            raise ValueError(f"{project}: PackageReference {name!r} sem versão exata: {version!r}")
        result[name] = version
    return result


def main() -> int:
    ap = argparse.ArgumentParser(description='Valida PackageReference exato e consistência estrutural de packages.lock.json; a proveniência do restore é validada separadamente.')
    ap.add_argument('--root', default='.', help='raiz da Solution')
    ap.add_argument('--summary', help='JSON de evidência a gravar')
    args = ap.parse_args()
    root = Path(args.root).resolve()
    projects = sorted(p for p in root.rglob('*.csproj') if not {'obj','bin'} & set(p.parts))
    if not projects:
        raise SystemExit('ERRO: nenhum .csproj encontrado')
    rows=[]
    for project in projects:
        refs=package_refs(project)
        lock=project.with_name('packages.lock.json')
        if not lock.is_file():
            raise SystemExit(f'ERRO: lock file ausente: {lock.relative_to(root)}')
        data=json.loads(lock.read_text(encoding='utf-8'))
        if data.get('version') != 1:
            raise SystemExit(f'ERRO: versão inesperada do lock: {lock.relative_to(root)}')
        deps=data.get('dependencies') or {}
        # aceita net8.0 e chaves qualificadas como net8.0/win-x64, mas exige ao menos uma família net8.0
        groups=[(k,v) for k,v in deps.items() if k == 'net8.0' or k.startswith('net8.0/')]
        if not groups:
            raise SystemExit(f'ERRO: {lock.relative_to(root)} sem grupo net8.0')
        direct_found={}
        for _, group in groups:
            for name, entry in (group or {}).items():
                if isinstance(entry, dict) and str(entry.get('type','')).lower() == 'direct':
                    direct_found.setdefault(name, entry.get('resolved'))
                if isinstance(entry, dict) and str(entry.get('type','')).lower() != 'project':
                    if not entry.get('resolved'):
                        raise SystemExit(f'ERRO: {lock.relative_to(root)} entrada {name} sem resolved')
                    if not entry.get('contentHash'):
                        raise SystemExit(f'ERRO: {lock.relative_to(root)} entrada {name} sem contentHash')
        expected_direct=set(refs)
        actual_direct=set(direct_found)
        if actual_direct != expected_direct:
            missing=sorted(expected_direct-actual_direct)
            extra=sorted(actual_direct-expected_direct)
            raise SystemExit(
                f'ERRO: {lock.relative_to(root)} conjunto Direct diverge do .csproj; ausentes={missing} extras={extra}'
            )
        for name, expected in refs.items():
            actual=direct_found.get(name)
            if actual != expected:
                raise SystemExit(f'ERRO: {lock.relative_to(root)} {name}: esperado {expected}, lock resolveu {actual!r}')
        raw=lock.read_bytes()
        rows.append({
            'project': str(project.relative_to(root)).replace('\\','/'),
            'lockFile': str(lock.relative_to(root)).replace('\\','/'),
            'sha256': hashlib.sha256(raw).hexdigest(),
            'directPackages': refs,
        })
    combined='\n'.join(f"{r['sha256']}  {r['lockFile']}" for r in rows).encode()
    summary={
        'schemaVersion':1,
        'targetFramework':'net8.0',
        'projectCount':len(rows),
        'combinedSha256':hashlib.sha256(combined).hexdigest(),
        'projects':rows,
        'assurance':'STRUCTURE_ONLY_NOT_RESTORE_PROOF',
    }
    if args.summary:
        out=Path(args.summary); out.parent.mkdir(parents=True,exist_ok=True)
        out.write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(f"NuGet lock gate OK: {len(rows)} projetos; combinedSha256={summary['combinedSha256']}")
    return 0

if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, json.JSONDecodeError, ET.ParseError) as e:
        print(f'ERRO: {e}', file=sys.stderr)
        raise SystemExit(2)

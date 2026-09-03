#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, uuid
from pathlib import Path


def iter_packages(data):
    for project in data.get('projects', []):
        project_name = project.get('path') or project.get('name') or 'unknown-project'
        for fw in project.get('frameworks', []):
            framework = fw.get('framework') or fw.get('name') or 'unknown-framework'
            for kind, key in [('direct','topLevelPackages'),('transitive','transitivePackages')]:
                for p in fw.get(key, []) or []:
                    name=p.get('id') or p.get('name')
                    version=p.get('resolvedVersion') or p.get('resolved') or p.get('version')
                    if name and version:
                        yield project_name, framework, kind, name, str(version)


def read_release_info(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding='utf-8').splitlines():
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = line.split('=', 1)
        values[key.strip()] = value.strip().removeprefix('v')
    return values


def main():
    ap=argparse.ArgumentParser(description='Converte dotnet list package --format json em CycloneDX 1.5 JSON.')
    ap.add_argument('--input', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--version', help='Override explícito da Solution; por padrão lê RELEASE_INFO.txt.')
    ap.add_argument('--release-info', default=str(Path(__file__).resolve().parents[2] / 'RELEASE_INFO.txt'))
    args=ap.parse_args()
    release=read_release_info(Path(args.release_info))
    solution_version=(args.version or release.get('solution_engenharia') or '').removeprefix('v')
    base_version=(release.get('base_normativa') or '').removeprefix('v')
    if not solution_version or not base_version:
        raise SystemExit('RELEASE_INFO sem base_normativa/solution_engenharia válidos.')
    data=json.loads(Path(args.input).read_text(encoding='utf-8'))
    occurrences={}
    for project, framework, kind, name, version in iter_packages(data):
        key=(name.lower(),version)
        rec=occurrences.setdefault(key, {'name':name,'version':version,'projects':set(),'kinds':set(),'frameworks':set()})
        rec['projects'].add(project); rec['kinds'].add(kind); rec['frameworks'].add(framework)
    comps=[]
    for (_,version), rec in sorted(occurrences.items(), key=lambda x:(x[1]['name'].lower(),x[1]['version'])):
        purl=f"pkg:nuget/{rec['name']}@{rec['version']}"
        comps.append({
            'type':'library',
            'bom-ref':purl,
            'name':rec['name'],
            'version':rec['version'],
            'purl':purl,
            'properties':[
                {'name':'jornada:dependency-kinds','value':','.join(sorted(rec['kinds']))},
                {'name':'jornada:frameworks','value':','.join(sorted(rec['frameworks']))},
                {'name':'jornada:projects','value':'|'.join(sorted(rec['projects']))},
            ],
        })
    identity='\n'.join(c['bom-ref'] for c in comps)
    serial=uuid.uuid5(uuid.NAMESPACE_URL, f'Jornada-Fase1-v{solution_version}\n{identity}')
    bom={
        'bomFormat':'CycloneDX',
        'specVersion':'1.5',
        'serialNumber':f'urn:uuid:{serial}',
        'version':1,
        'metadata':{
            'component':{
                'type':'application',
                'bom-ref':f'urn:jornada:fase1:solution:{solution_version}',
                'name':'Jornada.Fase1.Solution',
                'version':solution_version,
            },
            'properties':[
                {'name':'jornada:base-normativa','value':base_version},
                {'name':'jornada:generator','value':'scripts/generate-sbom.py'},
            ],
        },
        'components':comps,
    }
    out=Path(args.output); out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(bom,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    sha=hashlib.sha256(out.read_bytes()).hexdigest()
    print(f'SBOM CycloneDX 1.5: {len(comps)} componentes; sha256={sha}')

if __name__=='__main__':
    main()

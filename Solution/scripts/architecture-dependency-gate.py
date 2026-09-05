#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, tempfile, xml.etree.ElementTree as ET
from pathlib import Path


def fail(msg): raise SystemExit(f'ARCHITECTURE DEPENDENCY GATE: FAIL: {msg}')

def project_name(p:Path):
    return p.stem

def actual_refs(p:Path):
    root=ET.parse(p).getroot(); out=[]
    for node in root.findall('.//ProjectReference'):
        inc=node.attrib.get('Include')
        if inc: out.append(Path(inc.replace('\\','/')).stem)
    return sorted(set(out))

def check(solution:Path, policy:Path):
    cfg=json.loads(policy.read_text(encoding='utf-8'))
    allowed=cfg.get('projects') or {}
    csprojs=sorted(solution.rglob('*.csproj'))
    actual={project_name(p):actual_refs(p) for p in csprojs}
    errs=[]
    missing=sorted(set(actual)-set(allowed)); stale=sorted(set(allowed)-set(actual))
    if missing: errs.append(f'projetos sem política: {missing}')
    if stale: errs.append(f'projetos da política não encontrados: {stale}')
    for name,refs in actual.items():
        want=set(allowed.get(name,[])); got=set(refs)
        extra=sorted(got-want); lost=sorted(want-got)
        if extra: errs.append(f'{name}: dependências não permitidas {extra}')
        if lost: errs.append(f'{name}: política espera referências ausentes {lost}')
    # invariantes centrais independentes da lista detalhada.
    for leaf in ('Jornada.Contracts','Jornada.Bronze.Storage'):
        if actual.get(leaf): errs.append(f'{leaf}: núcleo deve permanecer sem ProjectReference')
    if 'Jornada.Api' in actual.get('Jornada.Contracts',[]): errs.append('Contracts não pode depender da API')
    if 'Jornada.Api' in actual.get('Jornada.Pipeline.Coordination',[]): errs.append('Pipeline.Coordination não pode depender da API')
    return actual,errs

def selftest():
    with tempfile.TemporaryDirectory() as td:
        r=Path(td); (r/'A').mkdir(); (r/'B').mkdir()
        (r/'A/A.csproj').write_text('<Project><ItemGroup><ProjectReference Include="../B/B.csproj" /></ItemGroup></Project>',encoding='utf-8')
        (r/'B/B.csproj').write_text('<Project />',encoding='utf-8')
        pol=r/'p.json'; pol.write_text(json.dumps({'projects':{'A':[],'B':[]}}),encoding='utf-8')
        _,errs=check(r,pol)
        if not errs: fail('self-test não detectou dependência proibida')
    print('ARCHITECTURE DEPENDENCY GATE SELF-TEST: OK')

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',default=str(Path(__file__).resolve().parent.parent)); ap.add_argument('--policy'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); a=ap.parse_args()
    if a.self_test: return selftest()
    root=Path(a.root).resolve(); policy=Path(a.policy).resolve() if a.policy else root/'config/release/architecture-dependencies.json'
    actual,errs=check(root,policy)
    summary={'status':'PASS' if not errs else 'FAIL','projects':actual,'errors':errs}
    if a.summary:
        p=Path(a.summary); p.parent.mkdir(parents=True,exist_ok=True); p.write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    if errs: fail('; '.join(errs[:12]))
    print(f'ARCHITECTURE DEPENDENCY GATE: OK ({len(actual)} projetos)')
if __name__=='__main__': main()

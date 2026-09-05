#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, re, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PROGRAM=ROOT/'src/Jornada.Api/Program.cs'
MATRIX=ROOT/'config/security/authorization-matrix.json'

def norm_path(p:str)->str:
    return re.sub(r'{([^}:]+):[^}]+}', r'{\1}', p)

def fail(msg:str)->None:
    raise SystemExit('AUTHORIZATION MATRIX GATE: FAIL: '+msg)

def main()->int:
    ap=argparse.ArgumentParser(); ap.add_argument('--matrix',default=str(MATRIX)); a=ap.parse_args()
    data=json.loads(Path(a.matrix).read_text(encoding='utf-8'))
    if data.get('schemaVersion')!=1 or data.get('status')!='VIGENTE': fail('matrix schema/status inválido')
    routes=data.get('routes');
    if not isinstance(routes,list) or not routes: fail('routes ausente')
    expected={(x['method'].upper(),x['path']):x for x in routes}
    if len(expected)!=len(routes): fail('rota duplicada na matriz')
    src=PROGRAM.read_text(encoding='utf-8')
    matches=list(re.finditer(r'app\.Map(Get|Post|Put|Delete)\("([^"]+)"',src))
    actual={}
    for i,m in enumerate(matches):
        method=m.group(1).upper(); path=norm_path(m.group(2));
        if not path.startswith('/api/'): continue
        end=matches[i+1].start() if i+1<len(matches) else src.find('app.Run()',m.start())
        block=src[m.start():end]
        perms=re.findall(r'IsAllowedAsync\(context,\s*"([^"]+)"',block)
        auth=re.search(r'AuthenticateAsync\([^;]+?allowTypeCredentials:\s*(true|false)',block,re.S)
        if not auth: fail(f'{method} {path}: AuthenticateAsync/allowTypeCredentials ausente')
        if not perms: fail(f'{method} {path}: IsAllowedAsync ausente')
        actual[(method,path)]={'permission':perms[0],'allowTypeCredentials':auth.group(1)=='true'}
    if set(actual)!=set(expected):
        fail('rotas divergentes: faltando='+str(sorted(set(expected)-set(actual)))+' extras='+str(sorted(set(actual)-set(expected))))
    for key, exp in expected.items():
        got=actual[key]
        if got['permission']!=exp['permission']: fail(f'{key}: permission={got["permission"]}, esperado={exp["permission"]}')
        allowed=set(exp.get('allowedCredentialTypes') or [])
        should_type=bool({'BENEFICIO','SERVICO'} & allowed)
        if got['allowTypeCredentials']!=should_type: fail(f'{key}: allowTypeCredentials divergente da matriz')
        if 'GESTOR' not in allowed: fail(f'{key}: toda rota API atual deve admitir GESTOR')
    print(f'AUTHORIZATION MATRIX GATE: OK ({len(actual)} rotas protegidas)')
    return 0
if __name__=='__main__': raise SystemExit(main())

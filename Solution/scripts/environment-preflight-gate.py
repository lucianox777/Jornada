#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,os,platform,re,shutil,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def parse_ver(s):
 m=re.search(r'(\d+)\.(\d+)(?:\.(\d+))?',s or '')
 return tuple(map(int,m.groups(default='0'))) if m else None

def tool_version(name):
 exe=shutil.which(name)
 if not exe: return None,None
 cmd=[exe,'--version']
 if name=='pwsh': cmd=[exe,'-NoLogo','-NoProfile','-Command','$PSVersionTable.PSVersion.ToString()']
 try: out=subprocess.run(cmd,capture_output=True,text=True,timeout=10).stdout.strip() or subprocess.run(cmd,capture_output=True,text=True,timeout=10).stderr.strip()
 except Exception: out=''
 return exe,out.splitlines()[0] if out else ''

def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',default=str(ROOT)); ap.add_argument('--profile',required=True); ap.add_argument('--strict',action='store_true'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); args=ap.parse_args(); root=Path(args.root).resolve()
 cfg=json.loads((root/'config/release/environment-requirements.json').read_text(encoding='utf-8'))
 if cfg.get('schemaVersion')!=1: raise SystemExit('ENVIRONMENT PREFLIGHT GATE: FAIL: schemaVersion inválido')
 if args.profile not in cfg.get('profiles',{}): raise SystemExit(f'ENVIRONMENT PREFLIGHT GATE: FAIL: profile desconhecido {args.profile}')
 if args.self_test:
  # estrutura de todos os perfis deve ser válida; não depende das ferramentas locais.
  for name,p in cfg['profiles'].items():
   if p.get('platform') not in {'linux','windows','any'}: raise SystemExit(f'ENVIRONMENT PREFLIGHT GATE: FAIL: platform inválida {name}')
   if not isinstance(p.get('tools'),list) or not isinstance(p.get('requiredEnvironment'),list): raise SystemExit(f'ENVIRONMENT PREFLIGHT GATE: FAIL: profile inválido {name}')
  print('ENVIRONMENT PREFLIGHT GATE SELF-TEST: OK'); return 0
 p=cfg['profiles'][args.profile]; issues=[]; tools=[]
 actual_platform='windows' if os.name=='nt' else 'linux'
 if p.get('platform') not in {'any',actual_platform}: issues.append(f'platform esperado={p.get("platform")} atual={actual_platform}')
 for spec in p.get('tools',[]):
  name=spec['name']; exe,ver=tool_version(name); row={'name':name,'found':bool(exe),'path':exe,'version':ver}; tools.append(row)
  if not exe: issues.append(f'ferramenta ausente: {name}'); continue
  if spec.get('exactVersion') and parse_ver(ver)!=parse_ver(spec['exactVersion']): issues.append(f'{name} versão {ver!r} != {spec["exactVersion"]}')
  if spec.get('minimumVersion') and (parse_ver(ver) is None or parse_ver(ver)<parse_ver(spec['minimumVersion'])): issues.append(f'{name} versão {ver!r} < {spec["minimumVersion"]}')
 missing_env=[k for k in p.get('requiredEnvironment',[]) if not os.environ.get(k)]
 issues += [f'variável ausente: {k}' for k in missing_env]
 status='PASS' if not issues else 'NOT_READY'
 result={'schemaVersion':1,'profile':args.profile,'status':status,'platform':platform.platform(),'tools':tools,'missingEnvironment':missing_env,'issues':issues}
 if args.summary:
  o=Path(args.summary); o.parent.mkdir(parents=True,exist_ok=True); o.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n')
 if args.strict and issues: raise SystemExit('ENVIRONMENT PREFLIGHT GATE: FAIL: '+'; '.join(issues))
 print(f'ENVIRONMENT PREFLIGHT GATE: {status} ({args.profile}; issues={len(issues)})')
 return 0
if __name__=='__main__': raise SystemExit(main())

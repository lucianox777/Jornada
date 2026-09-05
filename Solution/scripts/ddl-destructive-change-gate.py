#!/usr/bin/env python3
from __future__ import annotations
import argparse, difflib, hashlib, json, re, subprocess
from pathlib import Path
RISK_ADD=[r'\bDROP\s+(TABLE|COLUMN|DATABASE|SCHEMA|INDEX|CONSTRAINT)\b',r'\bTRUNCATE\s+TABLE\b',r'\bALTER\s+(TABLE\s+\S+\s+)?COLUMN\b',r'\bDELETE\s+FROM\b']
RISK_DEL=[r'\bCREATE\s+TABLE\b',r'\bCONSTRAINT\b',r'\bPRIMARY\s+KEY\b',r'\bFOREIGN\s+KEY\b',r'\bUNIQUE\b']
def fail(m): raise SystemExit('DDL DESTRUCTIVE CHANGE GATE: FAIL: '+m)
def info(p):
 d={}
 for l in p.read_text(encoding='utf-8').splitlines():
  if '=' in l and not l.lstrip().startswith('#'): k,v=l.split('=',1); d[k.strip()]=v.strip()
 return d
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--repo',default='.'); ap.add_argument('--release-info',default='RELEASE_INFO.txt'); ap.add_argument('--policy',default='Solution/config/release/ddl-change-policy.json'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); a=ap.parse_args()
 if a.self_test:
  line='+DROP TABLE gold.pessoa';
  if not any(re.search(p,line,re.I) for p in RISK_ADD): fail('self-test não detectou DROP TABLE')
  print('DDL DESTRUCTIVE CHANGE GATE SELF-TEST: OK'); return
 repo=Path(a.repo).resolve(); pred=info(repo/a.release_info).get('source_git_predecessor_tag');
 if not pred: fail('predecessor tag ausente')
 try: old=subprocess.check_output(['git','-C',str(repo),'show',f'{pred}:Solution/database/Jornada_Fase1.sql'],text=True)
 except subprocess.CalledProcessError: fail('DDL predecessor indisponível')
 new=(repo/'Solution/database/Jornada_Fase1.sql').read_text(encoding='utf-8')
 diff=list(difflib.unified_diff(old.splitlines(),new.splitlines(),lineterm=''))
 issues=[]
 for raw in diff:
  if raw.startswith(('+++','---','@@')): continue
  kind='ADD' if raw.startswith('+') else 'DEL' if raw.startswith('-') else None
  if not kind: continue
  line=raw[1:].strip()
  if not line or line.startswith('--'): continue
  pats=RISK_ADD if kind=='ADD' else RISK_DEL
  if any(re.search(p,line,re.I) for p in pats): issues.append({'kind':kind,'line':line,'sha256':hashlib.sha256((kind+'|'+line).encode()).hexdigest()})
 policy=json.loads((repo/a.policy).read_text(encoding='utf-8')); allowed={x.get('sha256') for x in policy.get('allowlist',[]) if x.get('justification') and x.get('migrationEvidence')}
 unapproved=[x for x in issues if x['sha256'] not in allowed]
 summary={'status':'PASS' if not unapproved else 'FAIL','predecessorTag':pred,'riskyChanges':issues,'unapproved':unapproved}
 if a.summary:
  Path(a.summary).parent.mkdir(parents=True,exist_ok=True); Path(a.summary).write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
 if unapproved: fail('; '.join(f"{x['kind']} {x['line']}" for x in unapproved[:10]))
 print(f'DDL DESTRUCTIVE CHANGE GATE: OK ({len(issues)} risky diff lines; {len(allowed)} approved exceptions; predecessor={pred})')
if __name__=='__main__': main()

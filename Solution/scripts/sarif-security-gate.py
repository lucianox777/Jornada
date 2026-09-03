#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,tempfile
from pathlib import Path
def fail(m): raise SystemExit('SARIF SECURITY GATE: FAIL: '+m)
def inspect(paths):
 findings=[]
 for p in paths:
  d=json.loads(p.read_text(encoding='utf-8'))
  for run in d.get('runs',[]):
   rules={r.get('id'):r for r in run.get('tool',{}).get('driver',{}).get('rules',[]) if r.get('id')}
   for x in run.get('results',[]):
    rid=x.get('ruleId'); rule=rules.get(rid,{})
    props=rule.get('properties',{}); sec=props.get('security-severity') or x.get('properties',{}).get('security-severity')
    try: secf=float(sec) if sec is not None else None
    except: secf=None
    level=(x.get('level') or 'warning').lower()
    if level=='error' or (secf is not None and secf>=8.0): findings.append({'ruleId':rid,'level':level,'securitySeverity':secf})
 return findings
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('path',nargs='?',type=Path); ap.add_argument('--summary',type=Path); ap.add_argument('--self-test',action='store_true'); a=ap.parse_args()
 if a.self_test:
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'x.sarif'; p.write_text(json.dumps({'runs':[{'tool':{'driver':{'rules':[{'id':'X','properties':{'security-severity':'9.0'}}]}},'results':[{'ruleId':'X','level':'warning'}]}]}))
   if not inspect([p]): fail('self-test não bloqueou severity 9')
  print('SARIF SECURITY GATE SELF-TEST: OK'); return
 if not a.path: fail('path SARIF ausente')
 paths=[a.path] if a.path.is_file() else sorted(a.path.rglob('*.sarif'))
 if not paths: fail('nenhum SARIF encontrado')
 findings=inspect(paths); sm={'status':'PASS' if not findings else 'FAIL','sarifFiles':len(paths),'blockingFindings':findings}
 if a.summary: a.summary.parent.mkdir(parents=True,exist_ok=True); a.summary.write_text(json.dumps(sm,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
 if findings: fail(f'{len(findings)} findings bloqueantes (level=error ou security-severity>=8)')
 print(f'SARIF SECURITY GATE: OK ({len(paths)} SARIF files; blocking findings=0)')
if __name__=='__main__':main()

#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,re
from pathlib import Path
from datetime import datetime
ROOT=Path(__file__).resolve().parents[1]
REQ={'PROCESSOR_WORKER','OPERATIONS_MAINTENANCE','BRONZE_MAINTENANCE','LINKAGE_GENERATE_DRAFT','LINKAGE_VALIDATE','LINKAGE_ACTIVATE','LINKAGE_INCREMENTAL','LINKAGE_REPLAY','LINKAGE_FULL','LINKAGE_MODEL_VALIDATION'}
SHA=re.compile(r'^[0-9a-f]{64}$')
def fail(x): raise SystemExit('SCHEDULER CONTRACT GATE: FAIL: '+x)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',default=str(ROOT)); ap.add_argument('--require-scheduled',action='store_true'); ap.add_argument('--summary'); args=ap.parse_args(); root=Path(args.root).resolve()
 p=root/'config/operations/scheduler-jobs.json'; d=json.loads(p.read_text(encoding='utf-8'))
 if d.get('schemaVersion')!=1: fail('schemaVersion deve ser 1')
 rows=d.get('jobs') if isinstance(d.get('jobs'),list) else []; by={}
 for r in rows:
  jid=r.get('id');
  if not jid or jid in by: fail(f'id ausente/duplicado: {jid}')
  by[jid]=r
  project=root/str(r.get('project',''))
  if not project.is_file(): fail(f'projeto inexistente para {jid}: {r.get("project")}')
  if r.get('kind') not in {'CONTINUOUS_WORKER','RUN_ONCE','EXCEPTIONAL'}: fail(f'{jid}: kind inválido')
  for dep in r.get('dependsOn',[]):
   if dep==jid: fail(f'{jid}: dependência reflexiva')
  if any(x in json.dumps(r).lower() for x in ['password=','access-key','secret=','token=']): fail(f'{jid}: contrato não pode conter segredo')
 if set(by)!=REQ: fail(f'jobs esperados divergentes missing={sorted(REQ-set(by))} extra={sorted(set(by)-REQ)}')
 for jid,r in by.items():
  for dep in r.get('dependsOn',[]):
   if dep not in by: fail(f'{jid}: dependsOn inexistente {dep}')
 approved=d.get('status')=='APROVADO'
 if approved:
  for jid,r in by.items():
   if r.get('status')!='APROVADO': fail(f'{jid}: deve estar APROVADO')
   if r.get('kind')!='EXCEPTIONAL' and (not isinstance(r.get('cadence'),str) or not r['cadence'].strip()): fail(f'{jid}: cadence ausente')
   if not isinstance(r.get('owner'),str) or not r['owner'].strip(): fail(f'{jid}: owner ausente')
   if not isinstance(r.get('retryPolicy'),dict): fail(f'{jid}: retryPolicy ausente')
  a=d.get('approval')
  if not isinstance(a,dict) or not isinstance(a.get('approvedBy'),str) or not a.get('approvedBy'): fail('approval.approvedBy ausente')
  if not isinstance(a.get('approvedAtUtc'),str) or not a['approvedAtUtc'].endswith('Z'): fail('approval.approvedAtUtc inválido')
  ev=a.get('evidence') if isinstance(a,dict) else None
  if not isinstance(ev,dict) or not SHA.fullmatch(str(ev.get('sha256','')).lower()): fail('approval.evidence.sha256 inválido')
 if args.require_scheduled and not approved: fail('scheduler corporativo ainda não está APROVADO/configurado')
 result={'schemaVersion':1,'status':'PASS','jobCount':len(rows),'approved':approved}
 if args.summary:
  o=Path(args.summary); o.parent.mkdir(parents=True,exist_ok=True); o.write_text(json.dumps(result,indent=2)+'\n')
 print(f'SCHEDULER CONTRACT GATE: OK ({len(rows)} jobs; approved={approved})')
 return 0
if __name__=='__main__': raise SystemExit(main())

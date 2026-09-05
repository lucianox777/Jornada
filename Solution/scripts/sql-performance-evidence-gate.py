#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,re
from pathlib import Path
SHA=re.compile(r'^[0-9a-f]{64}$')
def fail(x): raise SystemExit('SQL PERFORMANCE EVIDENCE GATE: FAIL: '+x)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('report',nargs='?'); ap.add_argument('--policy'); ap.add_argument('--require-policy-approved',action='store_true'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); args=ap.parse_args()
 if args.self_test:
  good={'schemaVersion':1,'collectionStatus':'PASS','databaseName':'JornadaHml','capturedAt':'2026-09-01T00:00:00+00:00','queryStoreState':'READ_WRITE','blockedRequests':0,'cumulativeLockWaitTasks':1,'cumulativeLockWaitMilliseconds':2,'cumulativeDeadlocksRaw':0,'windowSeconds':60,'deltaLockWaitTasks':0,'deltaLockWaitMilliseconds':0,'deltaDeadlocks':0}
  try: validate(good,{'schemaVersion':1,'status':'PENDENTE','requireQueryStoreReadWrite':True},False)
  except SystemExit as e: fail('self-test positivo falhou: '+str(e))
  bad=dict(good); bad['blockedRequests']=-1
  try: validate(bad,{'schemaVersion':1,'status':'PENDENTE','requireQueryStoreReadWrite':True},False); fail('self-test negativo não falhou')
  except SystemExit as e:
   if str(e).startswith('SQL PERFORMANCE EVIDENCE GATE: FAIL: self-test'): raise
  print('SQL PERFORMANCE EVIDENCE GATE SELF-TEST: OK'); return 0
 if not args.report or not args.policy: fail('report e --policy são obrigatórios')
 d=json.loads(Path(args.report).read_text(encoding='utf-8')); p=json.loads(Path(args.policy).read_text(encoding='utf-8'))
 result=validate(d,p,args.require_policy_approved)
 if args.summary:
  o=Path(args.summary); o.parent.mkdir(parents=True,exist_ok=True); o.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n')
 print(f"SQL PERFORMANCE EVIDENCE GATE: OK (queryStore={d.get('queryStoreState')}; blocked={d.get('blockedRequests')})")
 return 0

def validate(d,p,require):
 if d.get('schemaVersion')!=1 or d.get('collectionStatus')!='PASS': fail('evidência inválida')
 for k in ('blockedRequests','cumulativeLockWaitTasks','cumulativeLockWaitMilliseconds','cumulativeDeadlocksRaw','windowSeconds','deltaLockWaitTasks','deltaLockWaitMilliseconds','deltaDeadlocks'):
  v=d.get(k)
  if isinstance(v,bool) or not isinstance(v,int) or v<0: fail(f'{k} deve ser inteiro >=0')
 if not isinstance(d.get('databaseName'),str) or not d['databaseName']: fail('databaseName ausente')
 if p.get('schemaVersion')!=1 or p.get('status') not in {'PENDENTE','APROVADO'}: fail('policy inválida')
 if p.get('requireQueryStoreReadWrite') and d.get('queryStoreState')!='READ_WRITE': fail('Query Store deve estar READ_WRITE')
 approved=p.get('status')=='APROVADO'
 if require and not approved: fail('política SQL ainda não APROVADA')
 if approved:
  if not isinstance(p.get('approvedBy'),str) or not p.get('approvedBy'): fail('policy aprovada exige approvedBy')
  if not isinstance(p.get('approvedAtUtc'),str) or not p.get('approvedAtUtc').endswith('Z'): fail('policy aprovada exige approvedAtUtc UTC')
  ev=p.get('evidence'); ctx=p.get('approvalContext')
  if not isinstance(ev,dict) or not isinstance(ev.get('artifact'),str) or not SHA.fullmatch(str(ev.get('sha256','')).lower()): fail('policy aprovada exige evidence artifact+sha256')
  if not isinstance(ctx,dict): fail('policy aprovada exige approvalContext')
  limits=[('maximumBlockedRequests','blockedRequests'),('maximumLockWaitSeconds',None),('maximumDeadlocksPerWindow',None)]
  mb=p.get('maximumBlockedRequests')
  if not isinstance(mb,int) or mb<0: fail('maximumBlockedRequests inválido')
  if d['blockedRequests']>mb: fail('blockedRequests acima do limite')
  ml=p.get('maximumLockWaitSeconds')
  if not isinstance(ml,(int,float)) or isinstance(ml,bool) or ml<0: fail('maximumLockWaitSeconds inválido')
  if d['deltaLockWaitMilliseconds'] > ml*1000: fail('lock wait acima do limite aprovado para a janela de coleta')
  md=p.get('maximumDeadlocksPerWindow')
  if not isinstance(md,int) or md<0: fail('maximumDeadlocksPerWindow inválido')
  if d['deltaDeadlocks']>md: fail('deadlocks acima do limite')
 return {'schemaVersion':1,'status':'PASS','policyApproved':approved,'queryStoreState':d.get('queryStoreState'),'blockedRequests':d['blockedRequests']}
if __name__=='__main__': raise SystemExit(main())

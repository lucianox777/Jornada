#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,re,tempfile
from pathlib import Path
REQ={1,10,100,1000}; SHA=re.compile(r'^[0-9a-f]{64}$')
def fail(x): raise SystemExit('API PROJECTION EVIDENCE GATE: FAIL: '+x)
def validate(d,p,require):
 if d.get('schemaVersion')!=1 or d.get('status')!='PASS': fail('report inválido')
 if d.get('endpoint')!='POST /api/v1/pessoas/consulta': fail('endpoint inesperado')
 if not SHA.fullmatch(str(d.get('inputSha256','')).lower()): fail('inputSha256 inválido')
 rows=d.get('results') if isinstance(d.get('results'),list) else []; by={r.get('batchSize'):r for r in rows if isinstance(r,dict)}
 if set(by)!=REQ: fail('report deve conter lotes 1,10,100,1000')
 for size,r in by.items():
  sts=r.get('statusCodes'); counts=r.get('responseItemCounts'); lats=r.get('latenciesMs')
  if not sts or any(x!=200 for x in sts): fail(f'lote {size}: status HTTP não 200')
  if len(sts)!=r.get('iterations') or len(lats)!=r.get('iterations'): fail(f'lote {size}: iterações incoerentes')
  if any((not isinstance(x,(int,float)) or isinstance(x,bool) or x<=0) for x in lats): fail(f'lote {size}: latência inválida')
  if any(c!=size for c in counts): fail(f'lote {size}: responseItemCounts deve ser {size}')
  if not isinstance(r.get('p95Milliseconds'),(int,float)) or r['p95Milliseconds']<=0: fail(f'lote {size}: p95 inválido')
 pstatus=p.get('status')
 if p.get('schemaVersion')!=1 or pstatus not in {'PENDENTE','APROVADO'}: fail('policy inválida')
 if p.get('batchSizes')!=[1,10,100,1000]: fail('policy deve fixar lotes 1/10/100/1000')
 if require and pstatus!='APROVADO': fail('política de carga API ainda não APROVADA')
 if pstatus=='APROVADO':
  if not isinstance(p.get('approvedBy'),str) or not p.get('approvedBy'): fail('policy aprovada exige approvedBy')
  if not isinstance(p.get('approvedAtUtc'),str) or not p.get('approvedAtUtc').endswith('Z'): fail('policy aprovada exige approvedAtUtc UTC')
  ev=p.get('evidence'); ctx=p.get('approvalContext')
  if not isinstance(ev,dict) or not isinstance(ev.get('artifact'),str) or not SHA.fullmatch(str(ev.get('sha256','')).lower()): fail('policy aprovada exige evidence artifact+sha256')
  if not isinstance(ctx,dict): fail('policy aprovada exige approvalContext')
  limits=p.get('maxP95MillisecondsByBatchSize') or {}
  for size,r in by.items():
   limit=limits.get(str(size))
   if not isinstance(limit,(int,float)) or isinstance(limit,bool) or limit<=0: fail(f'limite p95 inválido para {size}')
   if r['p95Milliseconds']>limit: fail(f'lote {size}: p95 acima do limite')
 return {'schemaVersion':1,'status':'PASS','policyApproved':pstatus=='APROVADO','batchSizes':sorted(by)}
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('report',nargs='?'); ap.add_argument('--policy'); ap.add_argument('--require-policy-approved',action='store_true'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); args=ap.parse_args()
 if args.self_test:
  d={'schemaVersion':1,'status':'PASS','endpoint':'POST /api/v1/pessoas/consulta','inputSha256':'0'*64,'results':[{'batchSize':n,'iterations':2,'statusCodes':[200,200],'responseItemCounts':[n,n],'latenciesMs':[1.0,2.0],'p95Milliseconds':2.0} for n in sorted(REQ)]}
  p={'schemaVersion':1,'status':'PENDENTE','batchSizes':[1,10,100,1000],'maxP95MillisecondsByBatchSize':{str(n):None for n in REQ}}
  validate(d,p,False); bad=json.loads(json.dumps(d)); bad['results'][0]['statusCodes'][0]=500
  try: validate(bad,p,False); fail('self-test negativo não falhou')
  except SystemExit as e:
   if str(e).startswith('API PROJECTION EVIDENCE GATE: FAIL: self-test'): raise
  print('API PROJECTION EVIDENCE GATE SELF-TEST: OK'); return 0
 if not args.report or not args.policy: fail('report e --policy obrigatórios')
 r=validate(json.loads(Path(args.report).read_text()),json.loads(Path(args.policy).read_text()),args.require_policy_approved)
 if args.summary:
  o=Path(args.summary); o.parent.mkdir(parents=True,exist_ok=True); o.write_text(json.dumps(r,indent=2)+'\n')
 print('API PROJECTION EVIDENCE GATE: OK (1/10/100/1000)'); return 0
if __name__=='__main__': raise SystemExit(main())

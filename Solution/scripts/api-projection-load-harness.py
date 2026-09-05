#!/usr/bin/env python3
from __future__ import annotations
import argparse,hashlib,json,os,statistics,time,urllib.request,urllib.error
from pathlib import Path
SIZES=(1,10,100,1000)
def fail(x): raise SystemExit('API PROJECTION LOAD HARNESS: FAIL: '+x)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--base-url',required=True); ap.add_argument('--uuids-file',required=True); ap.add_argument('--gestor',required=True); ap.add_argument('--iterations',type=int,default=5); ap.add_argument('--output',required=True); args=ap.parse_args()
 key=os.environ.get('JORNADA_HML_ACCESS_KEY');
 if not key: fail('defina JORNADA_HML_ACCESS_KEY via secret store/ambiente')
 lines=[x.strip() for x in Path(args.uuids_file).read_text(encoding='utf-8').splitlines() if x.strip()]
 if len(lines)<1000 or len(set(lines))<1000: fail('arquivo deve conter ao menos 1000 UUIDs únicos autorizados')
 if args.iterations<2: fail('iterations deve ser >=2')
 rows=[]
 for size in SIZES:
  durations=[]; statuses=[]; counts=[]
  payload=json.dumps({'pessoaUuids':lines[:size]},separators=(',',':')).encode()
  for _ in range(args.iterations):
   req=urllib.request.Request(args.base_url.rstrip('/')+'/api/v1/pessoas/consulta',data=payload,method='POST',headers={'Content-Type':'application/json','X-Jornada-Gestor':args.gestor,'X-Jornada-Access-Key':key})
   start=time.perf_counter()
   try:
    with urllib.request.urlopen(req,timeout=120) as resp:
     body=resp.read(); code=resp.status
   except urllib.error.HTTPError as e:
    body=e.read(); code=e.code
   durations.append(round((time.perf_counter()-start)*1000,3)); statuses.append(code)
   try:
    parsed=json.loads(body); counts.append(len(parsed) if isinstance(parsed,list) else None)
   except Exception: counts.append(None)
  ordered=sorted(durations); idx=max(0,min(len(ordered)-1,int((len(ordered)-1)*0.95+0.999999)))
  rows.append({'batchSize':size,'iterations':args.iterations,'statusCodes':statuses,'responseItemCounts':counts,'latenciesMs':durations,'p95Milliseconds':ordered[idx]})
 out={'schemaVersion':1,'status':'PASS','endpoint':'POST /api/v1/pessoas/consulta','inputSha256':hashlib.sha256(Path(args.uuids_file).read_bytes()).hexdigest(),'results':rows,'note':'UUIDs, payloads e respostas não são persistidos nesta evidência.'}
 Path(args.output).parent.mkdir(parents=True,exist_ok=True); Path(args.output).write_text(json.dumps(out,indent=2,ensure_ascii=False)+'\n')
 print('API PROJECTION LOAD HARNESS: OK')
if __name__=='__main__': raise SystemExit(main())

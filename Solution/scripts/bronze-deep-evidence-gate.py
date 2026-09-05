#!/usr/bin/env python3
from __future__ import annotations
import argparse,hashlib,json,re,sys
from pathlib import Path
SHA=re.compile(r'^[0-9a-f]{64}$')
def fail(m): print('ERRO:',m,file=sys.stderr); return 2
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('report'); ap.add_argument('plan'); ap.add_argument('--summary'); ap.add_argument('--minimum-gc-candidates',type=int,default=0); a=ap.parse_args()
 try: r=json.loads(Path(a.report).read_text()); p=json.loads(Path(a.plan).read_text())
 except Exception as e: return fail(str(e))
 errors=[]
 if r.get('schemaVersion')!=1 or r.get('status')!='PASS' or not r.get('deep'): errors.append('relatório deep deve estar PASS')
 for k in ('missing','divergent','unavailable','keyHashMismatch','metadataConflicts','physicalDivergent','physicalUnavailable'):
  if r.get(k)!=0: errors.append(f'{k} deve ser zero')
 if p.get('schemaVersion')!=1 or p.get('mode')!='DRY_RUN_ONLY': errors.append('plano deve ser DRY_RUN_ONLY')
 c=p.get('candidates');
 if not isinstance(c,list): errors.append('candidates deve ser array'); c=[]
 if p.get('candidateCount')!=len(c) or r.get('gcEligibleCount')!=len(c): errors.append('contagem do plano divergente')
 if len(c)<a.minimum_gc_candidates: errors.append(f'plano possui {len(c)} candidato(s), mínimo exigido={a.minimum_gc_candidates}')
 actual=hashlib.sha256(json.dumps(c,ensure_ascii=False,indent=2,separators=(',', ': ')).encode()).hexdigest()
 declared=str(p.get('candidatesSha256','')).lower()
 if not SHA.fullmatch(declared): errors.append('candidatesSha256 inválido')
 elif declared != actual: errors.append(f'candidatesSha256 diverge do conteúdo: declarado={declared}, calculado={actual}')
 keys=set()
 for i,x in enumerate(c):
  if x.get('reason')!='UNREFERENCED_AND_OLDER_THAN_GRACE': errors.append(f'candidates[{i}].reason inválido')
  key=x.get('objectKey'); sha=x.get('sha256')
  if key in keys: errors.append('objectKey duplicada no plano')
  keys.add(key)
  if not isinstance(key,str) or not key.endswith('.zip') or not SHA.fullmatch(str(sha).lower()): errors.append(f'candidates[{i}] chave/hash inválido')
 summary={'schemaVersion':1,'status':'FAIL' if errors else 'PASS','candidateCount':len(c),'errors':errors}
 if a.summary: Path(a.summary).write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
 if errors:
  for e in errors: print('ERRO:',e,file=sys.stderr)
  return 2
 print(f'BRONZE DEEP EVIDENCE GATE: OK (gcCandidates={len(c)})'); return 0
if __name__=='__main__': raise SystemExit(main())

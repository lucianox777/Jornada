#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,sys
from pathlib import Path
COUNT_FIELDS=['gestor','bronzeEntrega','bronzeArquivo','silverPessoaObservacao','identidadePessoa','identityMap','vinculoFonte','goldPessoa','goldBeneficio','goldServico']
VIOLATION_FIELDS=['identityMapMissingPerson','resolvedVinculoMissingPerson','selfSuccessor','goldPersonMissingIdentity','activeCpfDuplicate','multipleActiveVinculoPerObservation']
def load(p):
 d=json.loads(Path(p).read_text());
 if not isinstance(d,dict): raise ValueError('JSON raiz deve ser objeto')
 return d
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('before'); ap.add_argument('after'); ap.add_argument('--summary'); a=ap.parse_args(); errors=[]
 try: b=load(a.before); c=load(a.after)
 except Exception as e: print('ERRO:',e,file=sys.stderr); return 2
 bc=b.get('counts') or {}; ac=c.get('counts') or {}; av=c.get('violations') or {}
 for f in COUNT_FIELDS:
  x=bc.get(f); y=ac.get(f)
  if not isinstance(x,int) or not isinstance(y,int): errors.append(f'counts.{f} ausente/não inteiro')
  elif y<x: errors.append(f'counts.{f} regrediu: before={x}, after={y}')
 for f in VIOLATION_FIELDS:
  if av.get(f)!=0: errors.append(f'violations.{f} deve ser 0, observado={av.get(f)!r}')
 summary={'schemaVersion':1,'status':'FAIL' if errors else 'PASS','beforeCounts':bc,'afterCounts':ac,'afterViolations':av,'errors':errors}
 if a.summary: Path(a.summary).write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
 if errors:
  for e in errors: print('ERRO:',e,file=sys.stderr)
  return 2
 print('UPGRADE INVARIANT GATE: OK ('+', '.join(f'{f}:{bc[f]}->{ac[f]}' for f in COUNT_FIELDS)+')'); return 0
if __name__=='__main__': raise SystemExit(main())

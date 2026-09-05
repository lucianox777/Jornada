#!/usr/bin/env python3
from __future__ import annotations
import argparse,hashlib,json,xml.etree.ElementTree as ET
from pathlib import Path
def fail(m): raise SystemExit('COVERAGE EVIDENCE GATE: FAIL: '+m)
def pct(x): return round(float(x)*100.0,4)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('coverage',nargs='?',type=Path); ap.add_argument('--baseline',type=Path,default=Path('config/release/coverage-baseline.json')); ap.add_argument('--summary',type=Path); ap.add_argument('--strict-baseline',action='store_true'); ap.add_argument('--self-test',action='store_true'); a=ap.parse_args()
 if a.self_test:
  import tempfile
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'c.xml'; p.write_text('<coverage line-rate="0.75" branch-rate="0.5" lines-valid="100" lines-covered="75" branches-valid="20" branches-covered="10"/>')
   r=ET.parse(p).getroot()
   if pct(r.attrib['line-rate'])!=75.0: fail('self-test line-rate')
  print('COVERAGE EVIDENCE GATE SELF-TEST: OK'); return
 if not a.coverage or not a.coverage.is_file(): fail(f'Cobertura ausente: {a.coverage}')
 try:r=ET.parse(a.coverage).getroot()
 except Exception as e: fail(f'Cobertura XML inválida: {e}')
 try: line=pct(r.attrib['line-rate']); branch=pct(r.attrib.get('branch-rate','0')); lv=int(r.attrib.get('lines-valid','0')); lc=int(r.attrib.get('lines-covered','0'))
 except Exception as e: fail(f'atributos Cobertura inválidos: {e}')
 if lv<=0 or lc<=0 or line<=0: fail('evidência de cobertura vazia')
 b=json.loads(a.baseline.read_text(encoding='utf-8')); status=b.get('status'); violations=[]
 if status=='APROVADO':
  if b.get('approvedLineRate') is None: violations.append('baseline aprovado sem approvedLineRate')
  else:
   allowed=float(b.get('maximumRegressionPercentagePoints',0)); floor=float(b['approvedLineRate'])-allowed
   if line+1e-9<floor: violations.append(f'line coverage {line}% abaixo do ratchet {floor}%')
  if b.get('approvedBranchRate') is not None:
   floor=float(b['approvedBranchRate'])-float(b.get('maximumRegressionPercentagePoints',0))
   if branch+1e-9<floor: violations.append(f'branch coverage {branch}% abaixo do ratchet {floor}%')
 elif a.strict_baseline: violations.append(f'baseline não aprovado: {status}')
 summary={'status':'PASS' if not violations else 'FAIL','baselineStatus':status,'lineRatePercent':line,'branchRatePercent':branch,'linesValid':lv,'linesCovered':lc,'coverageSha256':hashlib.sha256(a.coverage.read_bytes()).hexdigest(),'violations':violations}
 if a.summary:
  a.summary.parent.mkdir(parents=True,exist_ok=True); a.summary.write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
 if violations: fail('; '.join(violations))
 print(f'COVERAGE EVIDENCE GATE: OK (line={line}%; branch={branch}%; baseline={status})')
if __name__=='__main__':main()

#!/usr/bin/env python3
from pathlib import Path
import re,sys
ROOT=Path(__file__).resolve().parents[2]
pat=re.compile(r'^\s*uses:\s*([^\s#]+)',re.M); bad=[]; total=0
for f in sorted((ROOT/'.github/workflows').glob('*.y*ml')):
 text=f.read_text(encoding='utf-8')
 for m in pat.finditer(text):
  total+=1; ref=m.group(1)
  if ref.startswith('./'): continue
  if '@' not in ref: bad.append((f.name,ref)); continue
  _,v=ref.rsplit('@',1)
  if not re.fullmatch(r'[0-9a-f]{40}',v): bad.append((f.name,ref))
if bad: raise SystemExit('WORKFLOW ACTION PIN GATE: FAIL: ações não fixadas em SHA completo: '+repr(bad))
print(f'WORKFLOW ACTION PIN GATE: OK ({total} action references pinned by full commit SHA)')

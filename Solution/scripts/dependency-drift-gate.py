#!/usr/bin/env python3
from __future__ import annotations
import argparse,json,re,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
def fail(m): raise SystemExit('DEPENDENCY DRIFT GATE: FAIL: '+m)
def info(p):
 d={}
 for l in p.read_text(encoding='utf-8').splitlines():
  if '=' in l and not l.lstrip().startswith('#'): k,v=l.split('=',1); d[k.strip()]=v.strip()
 return d
def major(v):
 m=re.match(r'^(\d+)',v or ''); return int(m.group(1)) if m else None
def parse_xml(text):
 try:r=ET.fromstring(text)
 except ET.ParseError:return {}
 out={}
 for x in r.findall('.//PackageReference'):
  name=x.attrib.get('Include') or x.attrib.get('Update'); ver=x.attrib.get('Version') or (x.findtext('Version') or '')
  if name: out[name.lower()]=(name,ver)
 return out
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--repo',default='.'); ap.add_argument('--release-info',default='RELEASE_INFO.txt'); ap.add_argument('--policy',default='Solution/config/release/dependency-drift-policy.json'); ap.add_argument('--summary'); a=ap.parse_args()
 repo=Path(a.repo).resolve(); pred=info(repo/a.release_info).get('source_git_predecessor_tag'); pol=json.loads((repo/a.policy).read_text(encoding='utf-8'))
 files=subprocess.check_output(['git','-C',str(repo),'ls-files','Solution/**/*.csproj'],text=True).splitlines(); changes=[]
 for rel in files:
  cur=parse_xml((repo/rel).read_text(encoding='utf-8'))
  try: old=parse_xml(subprocess.check_output(['git','-C',str(repo),'show',f'{pred}:{rel}'],text=True))
  except subprocess.CalledProcessError: old={}
  for k in sorted(set(old)|set(cur)):
   o=old.get(k); n=cur.get(k)
   if o is None: changes.append({'kind':'ADD','project':rel,'package':n[0],'from':None,'to':n[1]})
   elif n is None: changes.append({'kind':'REMOVE','project':rel,'package':o[0],'from':o[1],'to':None})
   elif o[1]!=n[1]: changes.append({'kind':'MAJOR' if major(o[1])!=major(n[1]) else 'UPDATE','project':rel,'package':n[0],'from':o[1],'to':n[1]})
 allowed=pol.get('allowedChanges',[])
 def approved(c):
  if c['kind']=='UPDATE': return True
  return any(all(a.get(k)==c.get(k) for k in ('kind','project','package','from','to')) and a.get('justification') for a in allowed)
 blocked=[c for c in changes if not approved(c)]
 sm={'status':'PASS' if not blocked else 'FAIL','predecessorTag':pred,'changes':changes,'blocked':blocked}
 if a.summary: Path(a.summary).parent.mkdir(parents=True,exist_ok=True); Path(a.summary).write_text(json.dumps(sm,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
 if blocked: fail(str(blocked[:8]))
 print(f'DEPENDENCY DRIFT GATE: OK ({len(changes)} dependency changes; {sum(1 for x in changes if x["kind"]!="UPDATE")} governed)')
if __name__=='__main__':main()

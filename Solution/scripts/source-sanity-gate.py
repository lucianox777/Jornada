#!/usr/bin/env python3
from __future__ import annotations
import fnmatch,json,re,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]; POL=ROOT/'Solution/config/security/source-sanity-policy.json'
def fail(m): raise SystemExit('SOURCE SANITY GATE: FAIL: '+m)
p=json.loads(POL.read_text(encoding='utf-8'))
files=subprocess.check_output(['git','-C',str(ROOT),'ls-files'],text=True).splitlines(); issues=[]
def allowed(rel,globs): return any(fnmatch.fnmatch(rel,g) for g in globs)
for rel in files:
 f=ROOT/rel
 if not f.is_file() or f.suffix.lower() in {'.pdf','.docx','.zip','.bundle','.png','.jpg','.jpeg','.gif'}: continue
 try:text=f.read_text(encoding='utf-8')
 except UnicodeDecodeError: continue
 if re.search(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',text): issues.append(f'{rel}: private key')
 # O próprio arquivo de política precisa declarar literalmente os prefixos que proíbe.
 if rel != 'Solution/config/security/source-sanity-policy.json':
  for pref in p.get('forbiddenTokenPrefixes',[]):
   if pref in text: issues.append(f'{rel}: token prefix {pref}')
 if re.search(r'(?i)Encrypt\s*=\s*false|TrustServerCertificate\s*=\s*true',text) and not allowed(rel,p.get('allowedInsecureSqlGlobs',[])): issues.append(f'{rel}: SQL TLS enfraquecido fora de Development')
 if re.search(r'(?i)Password\s*=\s*(?!<|\$|\{|%|ENV|CHANGE_ME)[^;\s"\']{5,}',text) and not allowed(rel,p.get('allowedPasswordLiteralGlobs',[])): issues.append(f'{rel}: password literal fora de allowlist')
 if rel.endswith('/.env') or rel=='.env': issues.append(f'{rel}: .env não pode ser versionado')
if issues: fail('; '.join(issues[:20]))
print(f'SOURCE SANITY GATE: OK ({len(files)} tracked paths; no private keys/tokens/prod-insecure SQL literals)')

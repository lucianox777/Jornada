#!/usr/bin/env python3
from __future__ import annotations
import fnmatch,json,re,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]; POL=ROOT/'Solution/config/security/source-sanity-policy.json'
def fail(m): raise SystemExit('SOURCE SANITY GATE: FAIL: '+m)
p=json.loads(POL.read_text(encoding='utf-8'))
files=subprocess.check_output(['git','-C',str(ROOT),'ls-files'],text=True).splitlines(); issues=[]
def allowed(rel,globs): return any(fnmatch.fnmatch(rel,g) for g in globs)
# The operational V9 SQL smoke runs on a disposable DEV SQL Server bound only
# to loopback. Its isolated TLS exception is NOT a general workflow exception:
# fail closed if any essential isolation or secret handling guard disappears.
V9_DEV_SQL_SMOKE = '.github/workflows/v9-operational-model-sql-smoke.yml'
def restricted_v9_dev_sql_smoke(source):
 requirements = {
  'DEV runtime': r'(?m)^\s+JORNADA_RUNTIME_MODE:\s*DEV\s*for rel in files:
 f=ROOT/rel
 if not f.is_file() or f.suffix.lower() in {'.pdf','.docx','.zip','.bundle','.png','.jpg','.jpeg','.gif'}: continue
 try:text=f.read_text(encoding='utf-8')
 except UnicodeDecodeError: continue
 if re.search(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',text): issues.append(f'{rel}: private key')
 # O próprio arquivo de política precisa declarar literalmente os prefixos que proíbe.
 if rel != 'Solution/config/security/source-sanity-policy.json':
  for pref in p.get('forbiddenTokenPrefixes',[]):
   if pref in text: issues.append(f'{rel}: token prefix {pref}')
 if rel == V9_DEV_SQL_SMOKE and not restricted_v9_dev_sql_smoke(text):
  issues.append(f'{rel}: smoke DEV SQL sem isolamento loopback, imagem fixada, senha aleatória mascarada ou cleanup')
 if re.search(r'(?i)Encrypt\s*=\s*false|TrustServerCertificate\s*=\s*true',text) and not allowed(rel,p.get('allowedInsecureSqlGlobs',[])): issues.append(f'{rel}: SQL TLS enfraquecido fora de Development')
 if re.search(r'(?i)Password\s*=\s*(?!<|\$|\{|%|ENV|CHANGE_ME)[^;\s"\']{5,}',text) and not allowed(rel,p.get('allowedPasswordLiteralGlobs',[])): issues.append(f'{rel}: password literal fora de allowlist')
 if rel.endswith('/.env') or rel=='.env': issues.append(f'{rel}: .env não pode ser versionado')
if issues: fail('; '.join(issues[:20]))
print(f'SOURCE SANITY GATE: OK ({len(files)} tracked paths; no private keys/tokens/prod-insecure SQL literals)')
,
  'DEV dotnet': r'(?m)^\s+DOTNET_ENVIRONMENT:\s*DEV\s*for rel in files:
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
,
  'DEV aspnet': r'(?m)^\s+ASPNETCORE_ENVIRONMENT:\s*DEV\s*for rel in files:
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
,
  'hosted disposable runner': r'(?m)^\s+runs-on:\s*ubuntu-24\\.04\s*for rel in files:
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
,
  'loopback SQL port': r'--publish\s+127\\.0\\.0\\.1:1433:1433\b',
  'loopback SQL client': r'Server=127\\.0\\.0\\.1,1433;',
  'random per-run SQL password': r'openssl rand -hex 24',
  'mask before Docker startup': r'::add-mask::%s',
  'pinned container image': r'mcr\\.microsoft\\.com/mssql/server:2022-CU26-ubuntu-22\\.04@sha256:[a-f0-9]{64}',
  'disposable container': r'docker run --detach --rm',
  'no production publish': r'(?m)^\s+run:\s+docker rm -f v9-operational-converter-smoke',
 }
 return all(re.search(pattern,source) for pattern in requirements.values())

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

if '--self-test' in __import__('sys').argv:
 smoke = (ROOT / V9_DEV_SQL_SMOKE).read_text(encoding='utf-8')
 assert restricted_v9_dev_sql_smoke(smoke), 'DEV SQL smoke must be isolated'
 for original, unsafe in [
  ('JORNADA_RUNTIME_MODE: DEV', 'JORNADA_RUNTIME_MODE: PROD'),
  ('DOTNET_ENVIRONMENT: DEV', 'DOTNET_ENVIRONMENT: Production'),
  ('ASPNETCORE_ENVIRONMENT: DEV', 'ASPNETCORE_ENVIRONMENT: Production'),
  ('--publish 127.0.0.1:1433:1433', '--publish 1433:1433'),
  ('Server=127.0.0.1,1433;', 'Server=sqlserver.internal,1433;'),
  ('openssl rand -hex 24', 'echo static'),
  ('::add-mask::%s', '::notice::%s'),
  ('docker run --detach --rm', 'docker run --detach'),
  ('docker rm -f v9-operational-converter-smoke', 'echo missing-cleanup'),
 ]:
  assert original in smoke, f'baseline guard missing: {original}'
  assert not restricted_v9_dev_sql_smoke(smoke.replace(original,unsafe)), f'unsafe smoke admitted: {original}'
 print('SOURCE SANITY DEV SQL SMOKE NEGATIVE CONTRACT: PASS')

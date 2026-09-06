#!/usr/bin/env python3
from __future__ import annotations
from pathlib import Path
import hashlib, json

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
GESTORES = ('SEHAB','SMADS','SMDET','SMS')

# 1. Publish immutable Pessoa v2 contracts; v1 bytes remain untouched.
hashes: dict[str,str] = {}
for gestor in GESTORES:
    v1 = ROOT / 'config/contracts/gestores' / gestor / 'pessoa/v1'
    v2 = ROOT / 'config/contracts/gestores' / gestor / 'pessoa/v2'
    v2.mkdir(parents=True, exist_ok=True)

    descriptor = json.loads((v1/'pessoa.json').read_text(encoding='utf-8-sig'))
    descriptor['versao'] = 2
    descriptor['status'] = 'ATIVA'
    descriptor['descricao'] = f'Contrato cadastral versionado de Pessoas do Gestor {gestor}; codigoPessoaOrigem pode ser derivado do CPF quando ausente.'
    descriptor['codigoPessoaOrigemFallback'] = 'CPF_QUANDO_AUSENTE_E_PREENCHIDO'
    (v2/'pessoa.json').write_text(json.dumps(descriptor, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    schema = json.loads((v1/'pessoa.schema.json').read_text(encoding='utf-8-sig'))
    schema['$id'] = schema['$id'].replace('/v1/', '/v2/')
    required = schema.get('required', [])
    if 'codigoPessoaOrigem' not in required:
        raise SystemExit(f'{gestor}: codigoPessoaOrigem não estava required no v1')
    schema['required'] = [x for x in required if x != 'codigoPessoaOrigem']
    schema['properties']['codigoPessoaOrigem']['description'] = 'Opcional quando cpf estiver preenchido. Se ausente, a Jornada usa o CPF de 11 dígitos como codigoPessoaOrigem interno.'
    schema.setdefault('allOf', []).append({
        'if': {'required': ['codigoPessoaOrigem']},
        'then': {},
        'else': {'required': ['cpf'], 'properties': {'cpf': {'type': 'string', 'pattern': '^[0-9]{11}$'}}}
    })
    schema_path = v2/'pessoa.schema.json'
    schema_path.write_text(json.dumps(schema, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    hashes[gestor] = hashlib.sha256(schema_path.read_bytes()).hexdigest()

# 2. Runtime fallback in Processor.
processor = ROOT/'src/Jornada.Processor.Worker/ProcessorModels.cs'
text = processor.read_text(encoding='utf-8-sig')
old = '''            var json = validator.ParseAndValidate(line, "pessoas.jsonl", lineNumber);\n            var sourceCode = RequiredString(json, "codigoPessoaOrigem");\n            if (!sourceCodes.Add(sourceCode))\n                throw new InvalidDataException($"pessoas.jsonl: codigoPessoaOrigem duplicado: {sourceCode}.");\n'''
new = '''            var json = validator.ParseAndValidate(line, "pessoas.jsonl", lineNumber);\n            var cpf = OptionalString(json, "cpf");\n            var sourceCode = OptionalString(json, "codigoPessoaOrigem");\n            if (string.IsNullOrWhiteSpace(sourceCode))\n            {\n                if (string.IsNullOrWhiteSpace(cpf))\n                    throw new InvalidDataException("pessoas.jsonl: codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.");\n                sourceCode = cpf;\n            }\n            if (!sourceCodes.Add(sourceCode))\n                throw new InvalidDataException($"pessoas.jsonl: codigoPessoaOrigem duplicado: {sourceCode}.");\n'''
if old not in text:
    raise SystemExit('ProcessorModels.cs: bloco esperado não encontrado')
text = text.replace(old, new, 1)
old = '''                OptionalString(json, "sourceTransactionId"),\n                OptionalString(json, "cpf"),\n                OptionalString(json, "cpfAusenteMotivo"),'''
new = '''                OptionalString(json, "sourceTransactionId"),\n                cpf,\n                OptionalString(json, "cpfAusenteMotivo"),'''
if old not in text:
    raise SystemExit('ProcessorModels.cs: construção ParsedPerson não encontrada')
processor.write_text(text.replace(old, new, 1), encoding='utf-8')

# 3. Activate v2 in DEV seed while preserving v1 for historical deliveries.
seed = ROOT/'database/Jornada_Seed_Dev.sql'
text = seed.read_text(encoding='utf-8-sig')
start = text.index('-- Contratos cadastrais versionados por Gestor.')
end = text.index('\n\n-- Catálogo de QC por Tipo/Versão.', start)
hcase = ' '.join(f"WHEN '{g}' THEN 0x{hashes[g]}" for g in GESTORES)
block = f'''-- Contratos cadastrais versionados por Gestor.\n-- v1 permanece aceito para Entregas históricas; v2 torna codigoPessoaOrigem opcional quando CPF válido está preenchido.\nINSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)\nSELECT g.gestor_id,1,'2026-01-01',CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v1/pessoa.schema.json'),\n       CASE g.codigo WHEN 'SEHAB' THEN 0x5de6ddecfe95db8575ed321b83fb0eb1801cf3f6767406701f3146d0f474fd44 WHEN 'SMADS' THEN 0xa382a796a9b08b68bad09854b59fad6d64e6f0280812bfc604266bf6845f41f1 WHEN 'SMDET' THEN 0xdae81766352934486e1d1ef319261911d549fe987817e10f38fd5e0aa6bf4743 WHEN 'SMS' THEN 0xa3eee657504e51e0d54960b1e87dd24450b1a3d70f4b6ae9a5647c104b39e9a0 END,\n       'ENCERRADA','2026-08-27'\nFROM ref.gestor g WHERE g.codigo IN('SMS','SEHAB','SMADS','SMDET')\nAND NOT EXISTS(SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=1);\nUPDATE v SET pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0x5de6ddecfe95db8575ed321b83fb0eb1801cf3f6767406701f3146d0f474fd44 WHEN 'SMADS' THEN 0xa382a796a9b08b68bad09854b59fad6d64e6f0280812bfc604266bf6845f41f1 WHEN 'SMDET' THEN 0xdae81766352934486e1d1ef319261911d549fe987817e10f38fd5e0aa6bf4743 WHEN 'SMS' THEN 0xa3eee657504e51e0d54960b1e87dd24450b1a3d70f4b6ae9a5647c104b39e9a0 END,\n             status='ENCERRADA',vigencia_fim=COALESCE(v.vigencia_fim,'2026-09-05')\nFROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id\nWHERE v.versao=1 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');\n\nINSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)\nSELECT g.gestor_id,2,'2026-09-05',CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v2/pessoa.schema.json'),\n       CASE g.codigo {hcase} END,\n       'ATIVA','2026-09-05'\nFROM ref.gestor g WHERE g.codigo IN('SMS','SEHAB','SMADS','SMDET')\nAND NOT EXISTS(SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=2);\nUPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v2/pessoa.schema.json'),\n             pessoa_schema_sha256=CASE g.codigo {hcase} END,\n             status='ATIVA',vigencia_inicio='2026-09-05',vigencia_fim=NULL,ativado_em=COALESCE(v.ativado_em,'2026-09-05')\nFROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id\nWHERE v.versao=2 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');\nDECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=2),\n        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=2),\n        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=2);'''
seed.write_text(text[:start] + block + text[end:], encoding='utf-8')

# 4. AA01 E2E is now the executable proof of the fallback.
for name in ('AA01_v2','AA01_SEM_FATOS_v2'):
    p = ROOT/f'tests/fixtures/ingestao/{name}/manifest.json'
    d = json.loads(p.read_text(encoding='utf-8-sig'))
    d['pessoaSchemaVersao'] = 2
    d['codigoSistemaOrigem'] = 'SEHAB'
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

p = ROOT/'tests/fixtures/ingestao/AA01_v2/pessoas.jsonl'
person = json.loads(p.read_text(encoding='utf-8-sig').strip())
if person.get('cpf') != '70819234532':
    raise SystemExit('fixture AA01: CPF esperado mudou')
person.pop('codigoPessoaOrigem', None)
p.write_text(json.dumps(person, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')

p = ROOT/'tests/fixtures/ingestao/AA01_v2/registros.jsonl'
fact = json.loads(p.read_text(encoding='utf-8-sig').strip())
fact['codigoPessoaOrigem'] = '70819234532'
p.write_text(json.dumps(fact, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')

# E2E hash points at active v2 schema.
p = ROOT/'scripts/local-e2e.sh'
text = p.read_text(encoding='utf-8')
old = 'config/contracts/gestores/SEHAB/pessoa/v1/pessoa.schema.json'
if old not in text:
    raise SystemExit('local-e2e.sh: SEHAB v1 path not found')
p.write_text(text.replace(old, 'config/contracts/gestores/SEHAB/pessoa/v2/pessoa.schema.json', 1), encoding='utf-8')

# 5. Current docs.
p = ROOT/'README.md'
text = p.read_text(encoding='utf-8')
old = 'Todo manifesto declara `codigoSistemaOrigem`. Toda Pessoa declara `codigoPessoaOrigem`; todo fato declara também `codigoRegistroOrigem`. O namespace das chaves é o sistema de origem, não apenas o Gestor.'
new = 'Todo manifesto declara `codigoSistemaOrigem`. A Pessoa declara `codigoPessoaOrigem` ou, quando esse código não existe e o CPF está preenchido, a Jornada deriva `codigoPessoaOrigem` do próprio CPF; todo fato declara também `codigoRegistroOrigem`. Nesse fallback, os fatos devem referenciar a Pessoa usando o CPF como `codigoPessoaOrigem`. O namespace das chaves continua sendo o sistema de origem, não apenas o Gestor.'
if old not in text:
    raise SystemExit('README: identity paragraph not found')
p.write_text(text.replace(old, new, 1), encoding='utf-8')

p = ROOT/'docs/API.md'
text = p.read_text(encoding='utf-8')
old = '- toda Pessoa exige `codigoPessoaOrigem`;'
new = '- toda Pessoa exige `codigoPessoaOrigem` **ou** CPF preenchido; se o código estiver ausente, a Jornada usa o CPF como `codigoPessoaOrigem` interno;'
if old not in text:
    raise SystemExit('API.md: pessoa requirement not found')
text = text.replace(old, new, 1)
text = text.replace('"pessoaSchemaVersao": 1,\n  "codigoSistemaOrigem": "SEHAB"', '"pessoaSchemaVersao": 2,\n  "codigoSistemaOrigem": "SEHAB"')
p.write_text(text, encoding='utf-8')

# 6. Contract-level unit test.
p = ROOT/'tests/Jornada.Tests/Unit/JsonSchemaSubsetValidatorTests.cs'
text = p.read_text(encoding='utf-8')
marker = '    private static string WriteSchema(string text)\n'
test = '''    [Test]\n    public void Person_v2_allows_cpf_as_source_code_fallback_but_rejects_missing_code_without_cpf()\n    {\n        var dir = new DirectoryInfo(AppContext.BaseDirectory);\n        string? schema = null;\n        while (dir is not null)\n        {\n            var candidate = Path.Combine(dir.FullName, "config", "contracts", "gestores", "SEHAB", "pessoa", "v2", "pessoa.schema.json");\n            if (File.Exists(candidate)) { schema = candidate; break; }\n            dir = dir.Parent;\n        }\n        Assert.That(schema, Is.Not.Null, "Contrato Pessoa v2 da SEHAB deve integrar a fixture de testes.");\n        var validator = JsonSchemaSubsetValidator.Load(schema!);\n        const string valid = """\n        {\n          "cpf":"70819234532","cpfAusenteMotivo":null,"nomeCompleto":"Maria da Silva",\n          "dataNascimento":"1982-04-10","nomeMae":"Ana de Souza"\n        }\n        """;\n        const string invalid = """\n        {\n          "cpf":null,"cpfAusenteMotivo":"SEM_CPF","nomeCompleto":"Pessoa sem código",\n          "dataNascimento":"1982-04-10","nomeMae":"Ana de Souza"\n        }\n        """;\n        Assert.DoesNotThrow(() => validator.ParseAndValidate(valid, "pessoas.jsonl", 1));\n        Assert.That(() => validator.ParseAndValidate(invalid, "pessoas.jsonl", 2), Throws.TypeOf<InvalidDataException>());\n    }\n\n'''
if marker not in text:
    raise SystemExit('schema tests marker not found')
if 'Person_v2_allows_cpf_as_source_code_fallback' not in text:
    text = text.replace(marker, test + marker, 1)
p.write_text(text, encoding='utf-8')

# 7. Recalculate complete schema governance inventory.
p = ROOT/'config/governance/schema-approvals.json'
approvals = json.loads(p.read_text(encoding='utf-8-sig'))
old_entries = {e['path']: e for e in approvals['contracts']}
entries = []
for f in sorted((ROOT/'config/contracts').rglob('*.json')):
    rel = f.relative_to(ROOT).as_posix()
    sha = hashlib.sha256(f.read_bytes()).hexdigest()
    if rel in old_entries:
        e = dict(old_entries[rel]); e['sha256'] = sha
    else:
        e = {'path': rel, 'sha256': sha, 'status': 'PENDENTE', 'approval': None}
    entries.append(e)
approvals['contracts'] = entries
p.write_text(json.dumps(approvals, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

# 8. Release metadata for master post-tag development.
p = REPO/'RELEASE_INFO.txt'
text = p.read_text(encoding='utf-8')
text = text.replace(';NO_DOMAIN_CONTRACT_CHANGE', '')
if 'PERSON_SOURCE_CODE_CPF_FALLBACK' not in text:
    text = text.replace('runtime_change=', 'runtime_change=PERSON_SOURCE_CODE_CPF_FALLBACK;PERSON_SCHEMA_V2;', 1)
change_line = next(x for x in text.splitlines() if x.startswith('change='))
if 'codigoPessoaOrigem' not in change_line:
    text = text.replace(change_line, change_line + ' Pessoa v2 permite omitir codigoPessoaOrigem quando CPF estiver preenchido, usando o CPF como código interno de origem.')
if 'PERSON_SCHEMA_V2' not in next(x for x in text.splitlines() if x.startswith('annexes_updated=')):
    text = text.replace('annexes_updated=', 'annexes_updated=PERSON_SCHEMA_V2;CPF_SOURCE_CODE_FALLBACK;', 1)
p.write_text(text, encoding='utf-8')

# 9. Static regression gate.
p = ROOT/'scripts/technical-closure-gate.py'
text = p.read_text(encoding='utf-8')
anchor = '    # Regressões reveladas pelo bootstrap real da v3.86:'
block = '''    # Pessoa v2: codigoPessoaOrigem pode ser derivado do CPF somente quando o código não veio preenchido.\n    processor_models = (ROOT / "src" / "Jornada.Processor.Worker" / "ProcessorModels.cs").read_text(encoding="utf-8")\n    require(processor_models, [\n        'var sourceCode = OptionalString(json, "codigoPessoaOrigem")',\n        'var cpf = OptionalString(json, "cpf")',\n        'codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.',\n    ], "fallback CPF -> codigoPessoaOrigem")\n    for gestor in ("SEHAB", "SMADS", "SMDET", "SMS"):\n        schema_v2 = ROOT / "config" / "contracts" / "gestores" / gestor / "pessoa" / "v2" / "pessoa.schema.json"\n        if not schema_v2.is_file():\n            fail(f"contrato Pessoa v2 ausente para {gestor}")\n        data = json.loads(schema_v2.read_text(encoding="utf-8"))\n        if "codigoPessoaOrigem" in data.get("required", []):\n            fail(f"Pessoa v2 de {gestor} voltou a exigir codigoPessoaOrigem incondicionalmente")\n        if "cpf" not in data.get("required", []):\n            fail(f"Pessoa v2 de {gestor} deve manter cpf explicitamente presente")\n\n'''
if 'fallback CPF -> codigoPessoaOrigem' not in text:
    if anchor not in text:
        raise SystemExit('technical gate anchor not found')
    text = text.replace(anchor, block + anchor, 1)
p.write_text(text, encoding='utf-8')

print('CPF fallback transformation complete')
for gestor in GESTORES:
    print(gestor, hashes[gestor])

from pathlib import Path
import json, hashlib, re
ROOT=Path(__file__).resolve().parents[1]

def read(p): return (ROOT/p).read_text(encoding='utf-8')
def write(p,s):
    q=ROOT/p; q.parent.mkdir(parents=True,exist_ok=True); q.write_text(s,encoding='utf-8',newline='\n')
def one(s,a,b,label):
    n=s.count(a)
    if n!=1: raise SystemExit(f'{label}: esperado 1, encontrado {n}')
    return s.replace(a,b,1)

# Pessoa v4: chave de origem obrigatória e somente ENDERECO_RESIDENCIAL como atributo geográfico corrente.
hashes={}
for gestor in ['SEHAB','SMADS','SMDET','SMS']:
    src=ROOT/f'config/contracts/gestores/{gestor}/pessoa/v3/pessoa.schema.json'
    data=json.loads(src.read_text(encoding='utf-8'))
    data['$id']=data['$id'].replace('/v3/','/v4/')
    req=data['required']
    if 'codigoPessoaOrigem' not in req: req.insert(0,'codigoPessoaOrigem')
    data['properties']['codigoPessoaOrigem']['description']='Obrigatório. Chave local opaca e estável da Pessoa no sistema de origem; não é derivada do CPF.'
    attrs=data['properties']['atributosTransversais']['items']
    props=attrs['properties']
    props['atributoCodigo']['enum']=['ENDERECO_RESIDENCIAL','ENDERECO_CASA_ABRIGO_SIGILOSA','TELEFONE_CONTATO','EMAIL_CONTATO','NOME_SOCIAL']
    props.pop('naturezaReferenciaTerritorial',None)
    props['situacaoGeografia']['description']='Obrigatória somente para ENDERECO_RESIDENCIAL na Fase 1; responsabilidade do Gestor.'
    new_all=[]
    for rule in attrs.get('allOf',[]):
        txt=json.dumps(rule,ensure_ascii=False)
        if 'REFERENCIA_TERRITORIAL' in txt or 'naturezaReferenciaTerritorial' in txt:
            if 'ENDERECO_RESIDENCIAL' in txt:
                txt=txt.replace(', "REFERENCIA_TERRITORIAL"','').replace('"REFERENCIA_TERRITORIAL", ','').replace(' ou REFERENCIA_TERRITORIAL','')
                rule=json.loads(txt)
            else:
                continue
        new_all.append(rule)
    attrs['allOf']=new_all
    # remove fallback top-level codigoPessoaOrigem <- CPF
    data['allOf']=[r for r in data.get('allOf',[]) if 'codigoPessoaOrigem' not in json.dumps(r,ensure_ascii=False)]
    out=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
    path=f'config/contracts/gestores/{gestor}/pessoa/v4/pessoa.schema.json'
    write(path,out)
    hashes[gestor]=hashlib.sha256(out.encode()).hexdigest()

# Governance inventory.
p='config/governance/schema-approvals.json'; d=json.loads(read(p)); entries=d if isinstance(d,list) else d.get('schemas',d.get('entries'))
if entries is None: raise SystemExit('schema approvals shape desconhecido')
for gestor,h in hashes.items():
    path=f'config/contracts/gestores/{gestor}/pessoa/v4/pessoa.schema.json'
    if not any(x.get('path')==path for x in entries): entries.append({'path':path,'sha256':h,'status':'PENDENTE'})
write(p,json.dumps(d,ensure_ascii=False,indent=2)+'\n')

# Seed activates v4 while preserving v1-v3 historical.
p='database/Jornada_Seed_Dev.sql'; s=read(p)
s=s.replace('-- v1/v2 permanecem aceitos para Entregas históricas; v3 torna nomeMae opcional/anulável sem reescrever contratos anteriores.',
'''-- v1-v3 permanecem aceitos para replay histórico. v4 é o contrato corrente: codigoPessoaOrigem é obrigatório,\n-- nomeMae continua opcional e a única geografia cadastral corrente é a de ENDERECO_RESIDENCIAL.''')
old="""UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v3/pessoa.schema.json'),
             pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0xa4ea4f9c337f781e352877e03394f4d0e0db9eab6b9cb348439fe57a067b9752 WHEN 'SMADS' THEN 0x8b0b9709dc8afe420aa8a4693ce461b73d91cde84561702285e4fb48475cb656 WHEN 'SMDET' THEN 0x083937b02cdb31bcdd1265f34dacea80523ad08e1439f9ee6c44705f4433ed75 WHEN 'SMS' THEN 0x2fd8376496f33a422124dab61238498588ddfd99dfba944963787d9c26ea3550 END,
             status='ATIVA',vigencia_inicio='2026-09-12',vigencia_fim=NULL,ativado_em=COALESCE(v.ativado_em,'2026-09-12')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=3 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');
DECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=3),
        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=3),
        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=3);"""
case=' '.join([f"WHEN '{g}' THEN 0x{hashes[g]}" for g in ['SEHAB','SMADS','SMDET','SMS']])
new=f"""UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v3/pessoa.schema.json'),
             pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0xa4ea4f9c337f781e352877e03394f4d0e0db9eab6b9cb348439fe57a067b9752 WHEN 'SMADS' THEN 0x8b0b9709dc8afe420aa8a4693ce461b73d91cde84561702285e4fb48475cb656 WHEN 'SMDET' THEN 0x083937b02cdb31bcdd1265f34dacea80523ad08e1439f9ee6c44705f4433ed75 WHEN 'SMS' THEN 0x2fd8376496f33a422124dab61238498588ddfd99dfba944963787d9c26ea3550 END,
             status='ENCERRADA',vigencia_inicio='2026-09-12',vigencia_fim=COALESCE(v.vigencia_fim,'2026-09-13'),ativado_em=COALESCE(v.ativado_em,'2026-09-12')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=3 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');

INSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)
SELECT g.gestor_id,4,'2026-09-13',CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v4/pessoa.schema.json'),
       CASE g.codigo {case} END,'ATIVA','2026-09-13'
FROM ref.gestor g WHERE g.codigo IN('SMS','SEHAB','SMADS','SMDET')
AND NOT EXISTS(SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=4);
UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v4/pessoa.schema.json'),
             pessoa_schema_sha256=CASE g.codigo {case} END,status='ATIVA',vigencia_inicio='2026-09-13',vigencia_fim=NULL,ativado_em=COALESCE(v.ativado_em,'2026-09-13')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=4 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');
DECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=4),
        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=4),
        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=4);"""
s=one(s,old,new,'seed v4')
write(p,s)

# Parser: fallback only for historical contracts; v4 fails closed and rejects territorial reference.
p='src/Jornada.Processor.Worker/ProcessorModels.cs'; s=read(p)
old="""            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                if (string.IsNullOrWhiteSpace(cpf))
                    throw new InvalidDataException(\"pessoas.jsonl: codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.\");
                sourceCode = cpf;
            }"""
new="""            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                if (batch.PessoaSchemaVersao >= 4)
                    throw new InvalidDataException(\"pessoas.jsonl: codigoPessoaOrigem é obrigatório no contrato Pessoa v4; a Jornada não deriva chave de origem do CPF.\");
                if (string.IsNullOrWhiteSpace(cpf))
                    throw new InvalidDataException(\"pessoas.jsonl histórico: codigoPessoaOrigem ausente exige CPF para replay do contrato legado.\");
                sourceCode = cpf; // compatibilidade de replay v1-v3; não alcançável no contrato corrente v4.
            }"""
s=one(s,old,new,'source code')
needle='''                    var attributeCode = RequiredString(attr, "atributoCodigo");
                    ConfidentialShelterAddressPolicy.ValidateSource(batch, attributeCode);'''
rep=needle+'''\n                    if (batch.PessoaSchemaVersao >= 4 && string.Equals(attributeCode, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))\n                        throw new InvalidDataException("pessoas.jsonl: REFERENCIA_TERRITORIAL é legado e não é aceito no contrato Pessoa v4; use ENDERECO_RESIDENCIAL quando a informação for residência.");'''
s=one(s,needle,rep,'reject ref territorial')
write(p,s)

# SQL Server persistence: every residential address gets its own residential geography; legacy reference is written only for replay <= v3.
p='src/Jornada.Processor.Worker/SqlProcessorRepository.Persistence.cs'; s=read(p)
old='''            if (string.Equals(attribute.AtributoCodigo, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase))
            {
                // ENDERECO_RESIDENCIAL é dado cadastral de residência. Quando usado como fallback, sua geografia
                // é persistida somente no snapshot de Referência Territorial DOMICILIAR; não há tabela geográfica paralela.
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, TerritorialReferenceNature.DOMICILIAR,
                    "ENDERECO_RESIDENCIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
            else if (string.Equals(attribute.AtributoCodigo, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
            {
                if (!attribute.NaturezaReferenciaTerritorial.HasValue)
                    throw new InvalidDataException("REFERENCIA_TERRITORIAL sem natureza declarada pela fonte.");
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, attribute.NaturezaReferenciaTerritorial.Value,
                    "REFERENCIA_TERRITORIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }'''
new='''            if (string.Equals(attribute.AtributoCodigo, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase))
            {
                await InsertResidentialGeographyAsync(connection, tx, attributeObservationId, subprefeituraId, distritoId,
                    attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
                if (batch.PessoaSchemaVersao <= 3)
                    await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, TerritorialReferenceNature.DOMICILIAR,
                        "ENDERECO_RESIDENCIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
            else if (string.Equals(attribute.AtributoCodigo, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.PessoaSchemaVersao >= 4)
                    throw new InvalidDataException("REFERENCIA_TERRITORIAL não é aceito pelo contrato corrente.");
                if (!attribute.NaturezaReferenciaTerritorial.HasValue)
                    throw new InvalidDataException("REFERENCIA_TERRITORIAL histórico sem natureza declarada pela fonte.");
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, attribute.NaturezaReferenciaTerritorial.Value,
                    "REFERENCIA_TERRITORIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }'''
s=one(s,old,new,'sql persistence territorial')
write(p,s)

# Add residential persistence helpers without changing legacy selection API.
p='src/Jornada.Processor.Worker/SqlProcessorRepository.Materialization.cs'; s=read(p)
marker='''    private static async Task<TerritorialReferenceSelection> SelectTerritorialReferenceAsync(
        SqlConnection connection, SqlTransaction tx, long pessoaObservacaoId, CancellationToken ct)'''
helper='''    private static async Task InsertResidentialGeographyAsync(
        SqlConnection connection, SqlTransaction tx, long attributeObservationId, long? subprefeituraId, long? distritoId,
        GeographicResolutionStatus? status, ReferenceGeography? geography, DateTimeOffset sourceAsOf, CancellationToken ct)
    {
        if (!status.HasValue) throw new InvalidDataException("ENDERECO_RESIDENCIAL exige situacaoGeografia.");
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT silver.endereco_residencial_geografia_observacao(
                pessoa_atributo_observacao_id,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em,source_as_of)
            VALUES(@atributo,@subprefeitura,@distrito,@situacao,'ORIGEM',@malha,@resolvido,@source_as_of);
            """;
        command.Parameters.AddWithValue("@atributo", attributeObservationId);
        command.Parameters.Add(new SqlParameter("@subprefeitura", SqlDbType.BigInt) { Value=(object?)subprefeituraId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@distrito", SqlDbType.BigInt) { Value=(object?)distritoId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@situacao", SqlDbType.NVarChar, 30) { Value=status.Value.ToString() });
        AddNullable(command,"@malha",SqlDbType.NVarChar,120,geography?.ReferenciaMalha);
        command.Parameters.Add(new SqlParameter("@resolvido",SqlDbType.DateTimeOffset) { Value=(object?)geography?.ResolvidoEm ?? DBNull.Value });
        command.Parameters.AddWithValue("@source_as_of",sourceAsOf);
        await command.ExecuteNonQueryAsync(ct);
    }

'''+marker
s=one(s,marker,helper,'insert residential helper')
write(p,s)

# Migration: explicit residential geography, legacy reference remains read-only compatibility surface.
mig='''SET XACT_ABORT ON;\nBEGIN TRANSACTION;\n\nIF OBJECT_ID('silver.endereco_residencial_geografia_observacao','U') IS NULL\nBEGIN\n CREATE TABLE silver.endereco_residencial_geografia_observacao(\n  endereco_residencial_geografia_observacao_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_endereco_residencial_geografia_observacao PRIMARY KEY,\n  pessoa_atributo_observacao_id BIGINT NOT NULL CONSTRAINT UQ_endereco_residencial_geografia_atributo UNIQUE,\n  subprefeitura_id BIGINT NULL, distrito_id BIGINT NULL,\n  situacao_geografia NVARCHAR(30) NOT NULL, origem_geografia NVARCHAR(30) NOT NULL, referencia_malha NVARCHAR(120) NULL,\n  resolvido_em DATETIMEOFFSET NULL, source_as_of DATETIMEOFFSET NOT NULL, criado_em DATETIMEOFFSET NOT NULL CONSTRAINT DF_endereco_residencial_geografia_criado DEFAULT SYSDATETIMEOFFSET(),\n  CONSTRAINT FK_endereco_residencial_geografia_atributo FOREIGN KEY(pessoa_atributo_observacao_id) REFERENCES silver.pessoa_atributo_observacao(pessoa_atributo_observacao_id),\n  CONSTRAINT FK_endereco_residencial_geografia_subpref FOREIGN KEY(subprefeitura_id) REFERENCES ref.subprefeitura(subprefeitura_id),\n  CONSTRAINT FK_endereco_residencial_geografia_distrito FOREIGN KEY(distrito_id) REFERENCES ref.distrito(distrito_id),\n  CONSTRAINT CK_endereco_residencial_geografia_situacao CHECK(situacao_geografia IN('RESOLVIDA','FORA_MUNICIPIO','SEM_ENDERECO_APTO','NAO_RESOLVIDA_ORIGEM')),\n  CONSTRAINT CK_endereco_residencial_geografia_origem CHECK(origem_geografia='ORIGEM'),\n  CONSTRAINT CK_endereco_residencial_geografia_resolvida CHECK((situacao_geografia='RESOLVIDA' AND subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL AND referencia_malha IS NOT NULL) OR (situacao_geografia<>'RESOLVIDA' AND subprefeitura_id IS NULL AND distrito_id IS NULL AND referencia_malha IS NULL))\n );\nEND;\n\nINSERT silver.endereco_residencial_geografia_observacao(pessoa_atributo_observacao_id,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em,source_as_of)\nSELECT rt.pessoa_atributo_observacao_id,rt.subprefeitura_id,rt.distrito_id,rt.situacao_geografia,'ORIGEM',rt.referencia_malha,rt.resolvido_em,pa.ingested_at\nFROM silver.referencia_territorial_observacao rt\nJOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id\nWHERE rt.fonte_semantica='ENDERECO_RESIDENCIAL'\nAND NOT EXISTS(SELECT 1 FROM silver.endereco_residencial_geografia_observacao eg WHERE eg.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id);\nGO\nCREATE OR ALTER VIEW silver.v_pessoa_geografia_residencial AS\nSELECT po.pessoa_observacao_id,pa.pessoa_atributo_observacao_id,eg.endereco_residencial_geografia_observacao_id,eg.subprefeitura_id,eg.distrito_id,eg.situacao_geografia,eg.origem_geografia,eg.referencia_malha,eg.resolvido_em\nFROM silver.pessoa_observacao po\nOUTER APPLY(\n SELECT TOP(1) pa0.pessoa_atributo_observacao_id\n FROM silver.pessoa_atributo_observacao pa0\n WHERE pa0.pessoa_observacao_id=po.pessoa_observacao_id AND pa0.atributo_codigo='ENDERECO_RESIDENCIAL'\n ORDER BY COALESCE(pa0.referencia_evidencia,pa0.verificado_em,pa0.atualizado_em_origem,pa0.ingested_at) DESC,pa0.pessoa_atributo_observacao_id DESC\n) x\nLEFT JOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=x.pessoa_atributo_observacao_id\nLEFT JOIN silver.endereco_residencial_geografia_observacao eg ON eg.pessoa_atributo_observacao_id=pa.pessoa_atributo_observacao_id;\nGO\nIF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema') EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71'; ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';\nCOMMIT;\n'''
write('database/migrations/20260913_Endereco_Residencial_Sem_Fallback.sql',mig)

# New canonical installer wraps 3.70 plus 3.71 migration.
write('database/Jornada_Fase1_v3.71.sql',"""-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.71\n:on error exit\n:r database/Jornada_Fase1_v3.70.sql\n:r database/migrations/20260913_Endereco_Residencial_Sem_Fallback.sql\n""")

p='database/migrations/manifest.txt'; s=read(p)
s=s.replace('current SolutionSchema 3.70 baseline.','current SolutionSchema 3.71 baseline.')
if '20260913_Endereco_Residencial_Sem_Fallback.sql' not in s: s=s.rstrip()+'\n20260913_Endereco_Residencial_Sem_Fallback.sql\n'
write(p,s)
p='scripts/apply-migrations.sh'; s=read(p).replace('TARGET_SCHEMA="3.70"','TARGET_SCHEMA="3.71"').replace('FINAL_MIGRATION="20260910_Schema_Consolidation_370.sql"','FINAL_MIGRATION="20260913_Endereco_Residencial_Sem_Fallback.sql"').replace('EXPECTED_MIGRATIONS=13','EXPECTED_MIGRATIONS=14').replace('manifesto 3.70','manifesto 3.71').replace('promover 3.70','promover 3.71')
write(p,s)
for p in ['scripts/local-db.sh','scripts/local-db.ps1','database/Jornada_Runtime_Smoke.sql']:
    s=read(p).replace('Jornada_Fase1_v3.70.sql','Jornada_Fase1_v3.71.sql').replace('schema 3.70','schema 3.71').replace("N'3.70'","N'3.71'")
    write(p,s)

# Clean terminology in active docs/code comments; historical release notes untouched.
for p in ['src/Jornada.Contracts/IdentityLinkageContracts.cs','docs/API.md','docs/Territorializacao_Fase1.md','bi/README.md','Documentos/Especificacao_Tecnica_Jornada_Candidata.md']:
    q=ROOT/p
    if not q.exists(): continue
    s=q.read_text(encoding='utf-8')
    s=s.replace('fallback probabilístico','resolução probabilística').replace('fallback probabilistica','resolução probabilística')
    s=s.replace('modelo_fallback_versao','modelo_linkage_versao')
    s=s.replace('Na ausência de referência explícita, `ENDERECO_RESIDENCIAL` pode originar um fallback `DOMICILIAR`; a partir daí, a leitura territorial usa o snapshot selecionado e não o endereço cadastral diretamente.', 'A geografia de residência pertence diretamente a `ENDERECO_RESIDENCIAL`; a Jornada não cria referência territorial substituta quando essa informação estiver ausente.')
    q.write_text(s,encoding='utf-8',newline='\n')

# Contract tests for the decision.
test=r'''using System.Text.Json;\nusing NUnit.Framework;\n\nnamespace Jornada.Tests.Unit;\n\n[TestFixture]\npublic sealed class NoSemanticFallbackContractTests\n{\n    private static readonly string Root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../"));\n\n    [Test]\n    public void Pessoa_v4_requires_source_code_and_does_not_offer_territorial_reference()\n    {\n        foreach (var gestor in new[] { "SEHAB", "SMADS", "SMDET", "SMS" })\n        {\n            var path = Path.Combine(Root, "config", "contracts", "gestores", gestor, "pessoa", "v4", "pessoa.schema.json");\n            using var doc = JsonDocument.Parse(File.ReadAllText(path));\n            var root = doc.RootElement;\n            var required = root.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();\n            Assert.That(required, Does.Contain("codigoPessoaOrigem"));\n            var attrs = root.GetProperty("properties").GetProperty("atributosTransversais").GetProperty("items").GetProperty("properties");\n            var codes = attrs.GetProperty("atributoCodigo").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();\n            Assert.That(codes, Does.Contain("ENDERECO_RESIDENCIAL"));\n            Assert.That(codes, Does.Not.Contain("REFERENCIA_TERRITORIAL"));\n            Assert.That(attrs.TryGetProperty("naturezaReferenciaTerritorial", out _), Is.False);\n        }\n    }\n\n    [Test]\n    public void Runtime_keeps_legacy_fallback_only_for_replay_before_v4()\n    {\n        var parser = File.ReadAllText(Path.Combine(Root, "src", "Jornada.Processor.Worker", "ProcessorModels.cs"));\n        Assert.That(parser, Does.Contain("batch.PessoaSchemaVersao >= 4"));\n        Assert.That(parser, Does.Contain("a Jornada não deriva chave de origem do CPF"));\n        Assert.That(parser, Does.Contain("REFERENCIA_TERRITORIAL é legado e não é aceito no contrato Pessoa v4"));\n    }\n\n    [Test]\n    public void Schema_371_persists_residential_geography_as_residential_data()\n    {\n        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "20260913_Endereco_Residencial_Sem_Fallback.sql"));\n        Assert.That(migration, Does.Contain("silver.endereco_residencial_geografia_observacao"));\n        Assert.That(migration, Does.Contain("silver.v_pessoa_geografia_residencial"));\n        Assert.That(migration, Does.Contain("fonte_semantica='ENDERECO_RESIDENCIAL'"));\n    }\n}\n'''.replace('\\n','\n')
write('tests/Jornada.Tests/Unit/NoSemanticFallbackContractTests.cs',test)
print('patched semantic fallbacks; v4 hashes:',hashes)

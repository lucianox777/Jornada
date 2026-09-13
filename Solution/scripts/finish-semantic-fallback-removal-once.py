from pathlib import Path
import json, re
ROOT=Path(__file__).resolve().parents[1]

def r(p): return (ROOT/p).read_text(encoding='utf-8')
def w(p,s):
 q=ROOT/p; q.parent.mkdir(parents=True,exist_ok=True); q.write_text(s,encoding='utf-8',newline='\n')
def rep(p,a,b,min_count=1):
 s=r(p); n=s.count(a)
 if n<min_count: raise SystemExit(f'{p}: trecho ausente: {a[:100]}')
 w(p,s.replace(a,b))
def sub(p,pat,repl,min_count=1,flags=0):
 s=r(p); s2,n=re.subn(pat,repl,s,flags=flags)
 if n<min_count: raise SystemExit(f'{p}: regex sem match: {pat[:100]}')
 w(p,s2)

# 1) Schema 3.71 readiness/current installer propagation (historical baselines/releases untouched).
p='src/Jornada.Api/ApiHealth.cs'; s=r(p).replace("AND @solution=N'3.70'","AND @solution=N'3.71'")
s=s.replace("AND OBJECT_ID(N'gold.pessoa',N'U') IS NOT NULL", "AND OBJECT_ID(N'gold.pessoa',N'U') IS NOT NULL\n                    AND OBJECT_ID(N'silver.endereco_residencial_geografia_observacao',N'U') IS NOT NULL\n                    AND OBJECT_ID(N'silver.v_pessoa_geografia_residencial',N'V') IS NOT NULL")
w(p,s)

for p in ['scripts/local-ddl-upgrade.sh','scripts/materialize-sql-installer.py','scripts/compatibility-matrix-gate.py','scripts/technical-closure-gate.py']:
 s=r(p).replace('Jornada_Fase1_v3.70.sql','Jornada_Fase1_v3.71.sql').replace("N'3.70'","N'3.71'").replace('SolutionSchema=3.70','SolutionSchema=3.71').replace('schema 3.70','schema 3.71').replace('SCHEMA_370','SCHEMA_371').replace('schema_370','schema_371')
 w(p,s)

# CI and consolidation workflow current references.
for p in ['../.github/workflows/ci.yml','../.github/workflows/schema-consolidation-370.yml']:
 q=(ROOT/p).resolve(); s=q.read_text(encoding='utf-8')
 s=s.replace('Jornada_Fase1_v3.70.sql','Jornada_Fase1_v3.71.sql').replace("N'3.70'","N'3.71'").replace('SolutionSchema 3.70','SolutionSchema 3.71').replace('schema 3.70','schema 3.71').replace('ledger_rows -ne 13','ledger_rows -ne 14').replace('COUNT(*) FROM jornada.schema_migration;)" == "13"','COUNT(*) FROM jornada.schema_migration;)" == "14"')
 s=s.replace('jornada-schema-consolidation-370','jornada-schema-consolidation-371').replace('Schema consolidation 3.70','Schema consolidation 3.71')
 q.write_text(s,encoding='utf-8',newline='\n')

# Candidate tracks current engineering schema only; sealed release metadata stays untouched.
p='../CANDIDATE_INFO.json'; d=json.loads((ROOT/p).resolve().read_text(encoding='utf-8')); d['candidate']['solution_schema']='v3.71'; d['candidate']['canonical_ddl']='Solution/database/Jornada_Fase1_v3.71.sql'; (ROOT/p).resolve().write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

# Current README/docs pointers.
for p in ['README.md','../Documentos/Resumo_Executivo.md','../Documentos/README.md','../Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.md']:
 q=(ROOT/p).resolve(); s=q.read_text(encoding='utf-8')
 s=s.replace('Jornada_Fase1_v3.70.sql','Jornada_Fase1_v3.71.sql').replace('SolutionSchema=3.70','SolutionSchema=3.71').replace('schema 3.70','schema 3.71')
 # one new operational table in 3.71
 if 'Anexo_Modelo_Fisico' in str(q) or q.name in ('Resumo_Executivo.md','README.md'):
  s=s.replace('69 tabelas distintas','70 tabelas distintas').replace('69 tabelas','70 tabelas')
 q.write_text(s,encoding='utf-8',newline='\n')

# 2) Extend SQL Server migration: residential snapshot is explicit on facts/serving; legacy columns retained only for replay/backward compatibility.
p='database/migrations/20260913_Endereco_Residencial_Sem_Fallback.sql'; s=r(p)
anchor="""GO
CREATE OR ALTER VIEW silver.v_pessoa_geografia_residencial AS"""
pre="""GO
IF COL_LENGTH('gold.beneficio_concedido','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('gold.beneficio_concedido','subprefeitura_residencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.beneficio_concedido','distrito_residencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD distrito_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE gold.servico_prestado ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','subprefeitura_residencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','distrito_residencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD distrito_residencia_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE serving.registro_integrado ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','subprefeitura_residencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','distrito_residencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD distrito_residencia_id BIGINT NULL;
GO
CREATE OR ALTER VIEW silver.v_pessoa_geografia_residencial AS"""
if anchor not in s: raise SystemExit('migration view anchor missing')
s=s.replace(anchor,pre,1)
# Backfill fact residential geography from observation relation, not from invented territorial reference.
insert_before="""GO
IF EXISTS(SELECT 1 FROM sys.extended_properties"""
backfill="""GO
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM gold.beneficio_concedido f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM gold.servico_prestado f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM serving.registro_integrado f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties"""
if insert_before not in s: raise SystemExit('marker anchor missing')
s=s.replace(insert_before,backfill,1)
w(p,s)

# 3) SQL Server runtime carries residential selection independently of legacy territorial selection.
p='src/Jornada.Processor.Worker/SqlProcessorRepository.Materialization.cs'; s=r(p)
# Add selector before legacy selector.
marker='''    private static async Task<TerritorialReferenceSelection> SelectTerritorialReferenceAsync('''
helper='''    private static async Task<ResidentialGeographySelection> SelectResidentialGeographyAsync(
        SqlConnection connection, SqlTransaction tx, long pessoaObservacaoId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT endereco_residencial_geografia_observacao_id,subprefeitura_id,distrito_id FROM silver.v_pessoa_geografia_residencial WHERE pessoa_observacao_id=@pessoa;";
        command.Parameters.AddWithValue("@pessoa", pessoaObservacaoId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0)) return new ResidentialGeographySelection(null,null,null);
        return new ResidentialGeographySelection(reader.GetInt64(0),reader.IsDBNull(1)?null:reader.GetInt64(1),reader.IsDBNull(2)?null:reader.GetInt64(2));
    }

'''+marker
if marker not in s: raise SystemExit('sql selector anchor missing')
s=s.replace(marker,helper,1)
# Fact insert columns/values and parameters.
s=s.replace('motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,valor_concedido', 'motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id,distrito_residencia_id,valor_concedido')
s=s.replace('@motivo_encerramento,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,\n                       @valor_concedido', '@motivo_encerramento,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@residencia_geografia,@subprefeitura_residencia,@distrito_residencia,\n                       @valor_concedido')
s=s.replace('situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,source_as_of,', 'situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id,distrito_residencia_id,source_as_of,')
s=s.replace('@situacao,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@source,', '@situacao,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@residencia_geografia,@subprefeitura_residencia,@distrito_residencia,@source,')
param='''        command.Parameters.Add(new SqlParameter("@distrito", SqlDbType.BigInt) { Value = (object?)person.DistritoId ?? DBNull.Value });'''
param2=param+'''\n        command.Parameters.Add(new SqlParameter("@residencia_geografia", SqlDbType.BigInt) { Value = (object?)person.EnderecoResidencialGeografiaObservacaoId ?? DBNull.Value });\n        command.Parameters.Add(new SqlParameter("@subprefeitura_residencia", SqlDbType.BigInt) { Value = (object?)person.SubprefeituraResidenciaId ?? DBNull.Value });\n        command.Parameters.Add(new SqlParameter("@distrito_residencia", SqlDbType.BigInt) { Value = (object?)person.DistritoResidenciaId ?? DBNull.Value });'''
if param not in s: raise SystemExit('sql param anchor missing')
s=s.replace(param,param2,1)
s=s.replace('private sealed record ProcessedPerson(long ObservationId, long PessoaOrigemId, long SistemaOrigemId, string CodigoPessoaOrigem, string? CpfDeclarado, string? CpfAusenteMotivo, Guid? PessoaUuid, string EstadoAtribuicaoIdentidade, long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);', 'private sealed record ProcessedPerson(long ObservationId, long PessoaOrigemId, long SistemaOrigemId, string CodigoPessoaOrigem, string? CpfDeclarado, string? CpfAusenteMotivo, Guid? PessoaUuid, string EstadoAtribuicaoIdentidade, long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId, long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraResidenciaId, long? DistritoResidenciaId);')
s=s.replace('private sealed record TerritorialReferenceSelection(long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);', 'private sealed record TerritorialReferenceSelection(long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);\n    private sealed record ResidentialGeographySelection(long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraId, long? DistritoId);')
w(p,s)

p='src/Jornada.Processor.Worker/SqlProcessorRepository.Persistence.cs'; s=r(p)
s=s.replace('var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);', 'var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);\n        var residentialGeography = await SelectResidentialGeographyAsync(connection, tx, observationId, ct);')
s=s.replace('selectedGeography.ReferenciaTerritorialObservacaoId, selectedGeography.NaturezaReferenciaTerritorial, selectedGeography.SubprefeituraId, selectedGeography.DistritoId);', 'selectedGeography.ReferenciaTerritorialObservacaoId, selectedGeography.NaturezaReferenciaTerritorial, selectedGeography.SubprefeituraId, selectedGeography.DistritoId, residentialGeography.EnderecoResidencialGeografiaObservacaoId, residentialGeography.SubprefeituraId, residentialGeography.DistritoId);')
# Load existing person: join residential view and append three reader fields.
old="""LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE po.pessoa_observacao_id=@obs;"""
new="""LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE po.pessoa_observacao_id=@obs;""" # load path below selects legacy separately; residential loaded after query
# Replace final LoadProcessedPerson return by selecting residential after territorial selector where present.
needle='''        var selected = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);
        return new ProcessedPerson(observationId,pessoaOrigemId,sistemaOrigemId,codigo,cpf,cpfMotivo,uuid,ToAssignmentState(Enum.Parse<ResolutionStatus>(status)),selected.ReferenciaTerritorialObservacaoId,selected.NaturezaReferenciaTerritorial,selected.SubprefeituraId,selected.DistritoId);'''
if needle in s:
 s=s.replace(needle,'''        var selected = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);
        var residential = await SelectResidentialGeographyAsync(connection, tx, observationId, ct);
        return new ProcessedPerson(observationId,pessoaOrigemId,sistemaOrigemId,codigo,cpf,cpfMotivo,uuid,ToAssignmentState(Enum.Parse<ResolutionStatus>(status)),selected.ReferenciaTerritorialObservacaoId,selected.NaturezaReferenciaTerritorial,selected.SubprefeituraId,selected.DistritoId,residential.EnderecoResidencialGeografiaObservacaoId,residential.SubprefeituraId,residential.DistritoId);''')
w(p,s)

# 4) PostgreSQL core: explicit residential geography table/view and fact columns, while legacy reference remains for replay.
p='database/postgresql/Jornada_Processor_Persistence_Core.sql'; s=r(p)
anchor='''CREATE TABLE IF NOT EXISTS silver.referencia_territorial_observacao('''
res='''CREATE TABLE IF NOT EXISTS silver.endereco_residencial_geografia_observacao(
    endereco_residencial_geografia_observacao_id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    pessoa_atributo_observacao_id BIGINT NOT NULL UNIQUE REFERENCES silver.pessoa_atributo_observacao(pessoa_atributo_observacao_id) ON DELETE CASCADE,
    subprefeitura_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id),
    distrito_id BIGINT NULL REFERENCES ref.distrito(distrito_id),
    situacao_geografia VARCHAR(40) NOT NULL CHECK(situacao_geografia IN ('RESOLVIDA','FORA_MUNICIPIO','SEM_ENDERECO_APTO','NAO_RESOLVIDA_ORIGEM')),
    origem_geografia VARCHAR(30) NOT NULL DEFAULT 'ORIGEM' CHECK(origem_geografia='ORIGEM'),
    referencia_malha VARCHAR(120) NULL,
    resolvido_em TIMESTAMPTZ NULL,
    source_as_of TIMESTAMPTZ NOT NULL,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE OR REPLACE VIEW silver.v_pessoa_geografia_residencial AS
SELECT po.pessoa_observacao_id,x.pessoa_atributo_observacao_id,x.endereco_residencial_geografia_observacao_id,x.subprefeitura_id,x.distrito_id,x.situacao_geografia,x.origem_geografia,x.referencia_malha,x.resolvido_em
FROM silver.pessoa_observacao po
LEFT JOIN LATERAL (
 SELECT pa.pessoa_atributo_observacao_id,eg.endereco_residencial_geografia_observacao_id,eg.subprefeitura_id,eg.distrito_id,eg.situacao_geografia,eg.origem_geografia,eg.referencia_malha,eg.resolvido_em
 FROM silver.pessoa_atributo_observacao pa JOIN silver.endereco_residencial_geografia_observacao eg USING(pessoa_atributo_observacao_id)
 WHERE pa.pessoa_observacao_id=po.pessoa_observacao_id AND pa.atributo_codigo='ENDERECO_RESIDENCIAL'
 ORDER BY COALESCE(pa.referencia_evidencia,pa.verificado_em,pa.atualizado_em_origem,pa.ingested_at) DESC,pa.pessoa_atributo_observacao_id DESC LIMIT 1
) x ON TRUE;

'''+anchor
if anchor not in s: raise SystemExit('pg core anchor missing')
s=s.replace(anchor,res,1)
# Add current fact columns after tables exist using ALTER IF NOT EXISTS, avoids rewriting table definitions.
marker='''CREATE TABLE IF NOT EXISTS identidade.pessoa('''
alters='''ALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;
ALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;
ALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;
ALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;
ALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;
ALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;
ALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;
ALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;
ALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;

'''+marker
# tables may be defined later than marker; only inject if gold table definitions precede this point. Safer append at EOF instead.
# We'll append the ALTERs at EOF where all fact tables are guaranteed to exist.
s=s.rstrip()+'''\n\n-- Linha corrente: geografia factual de residência explícita; colunas territoriais anteriores permanecem somente por compatibilidade.\nALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;\nALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;\nALTER TABLE gold.beneficio_concedido ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;\nALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;\nALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;\nALTER TABLE gold.servico_prestado ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;\nALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS endereco_residencial_geografia_observacao_id BIGINT NULL;\nALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS subprefeitura_residencia_id BIGINT NULL;\nALTER TABLE serving.registro_integrado ADD COLUMN IF NOT EXISTS distrito_residencia_id BIGINT NULL;\n'''
w(p,s)

# PostgreSQL repository write residential directly, retain territorial only on historical v1-v3.
p='src/Jornada.Processor.Worker/PostgreSqlProcessorRepository.cs'; s=r(p)
old='''            if (string.Equals(attribute.AtributoCodigo, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase))
            {
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
if old not in s: raise SystemExit('pg runtime old block missing')
s=s.replace(old,new,1)
# Add residential selector and writer before territorial writer.
marker='''    private static async Task InsertTerritorialReferenceAsync('''
helper='''    private static async Task InsertResidentialGeographyAsync(
        DbConnection connection, DbTransaction tx, long attributeObservationId, long? subprefeituraId, long? distritoId,
        GeographicResolutionStatus? geographyStatus, ReferenceGeography? geography, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        if (!geographyStatus.HasValue) throw new InvalidDataException("ENDERECO_RESIDENCIAL exige situacaoGeografia.");
        await using var command = Command(connection, tx, """
            INSERT INTO silver.endereco_residencial_geografia_observacao(
                pessoa_atributo_observacao_id,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em,source_as_of)
            VALUES(@atributo,@subprefeitura,@distrito,@situacao,'ORIGEM',@malha,@resolvido,@source);
            """);
        Add(command,"@atributo",DbType.Int64,attributeObservationId); Add(command,"@subprefeitura",DbType.Int64,subprefeituraId); Add(command,"@distrito",DbType.Int64,distritoId);
        Add(command,"@situacao",DbType.String,geographyStatus.Value.ToString(),40); Add(command,"@malha",DbType.String,geography?.ReferenciaMalha,120);
        Add(command,"@resolvido",DbType.DateTimeOffset,geography is null?null:geography.ResolvidoEm??dataReferencia); Add(command,"@source",DbType.DateTimeOffset,dataReferencia);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<PgResidentialSelection> SelectResidentialGeographyAsync(DbConnection connection, DbTransaction tx, long observationId, CancellationToken ct)
    {
        await using var command=Command(connection,tx,"SELECT endereco_residencial_geografia_observacao_id,subprefeitura_id,distrito_id FROM silver.v_pessoa_geografia_residencial WHERE pessoa_observacao_id=@pessoa;");
        Add(command,"@pessoa",DbType.Int64,observationId); await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)||reader.IsDBNull(0)) return new PgResidentialSelection(null,null,null);
        return new PgResidentialSelection(reader.GetInt64(0),reader.IsDBNull(1)?null:reader.GetInt64(1),reader.IsDBNull(2)?null:reader.GetInt64(2));
    }

'''+marker
if marker not in s: raise SystemExit('pg writer anchor missing')
s=s.replace(marker,helper,1)
s=s.replace('var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);', 'var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);\n        var residentialGeography = await SelectResidentialGeographyAsync(connection, tx, observationId, ct);')
s=s.replace('selectedGeography.SubprefeituraId, selectedGeography.DistritoId);', 'selectedGeography.SubprefeituraId, selectedGeography.DistritoId, residentialGeography.EnderecoResidencialGeografiaObservacaoId, residentialGeography.SubprefeituraId, residentialGeography.DistritoId);',1)
# LoadProcessedPerson query gets residential via separate selector after reader is disposed is cumbersome; change query to left join residential view and append columns.
s=s.replace('rt.referencia_territorial_observacao_id,rt.natureza_referencia,rt.subprefeitura_id,rt.distrito_id', 'rt.referencia_territorial_observacao_id,rt.natureza_referencia,rt.subprefeitura_id,rt.distrito_id,rg.endereco_residencial_geografia_observacao_id,rg.subprefeitura_id,rg.distrito_id',1)
s=s.replace('LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=po.pessoa_observacao_id', 'LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=po.pessoa_observacao_id\n              LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id',1)
s=s.replace('reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt64(11));', 'reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt64(11),\n            reader.IsDBNull(12) ? null : reader.GetInt64(12), reader.IsDBNull(13) ? null : reader.GetInt64(13), reader.IsDBNull(14) ? null : reader.GetInt64(14));',1)
s=s.replace('long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);', 'long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId, long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraResidenciaId, long? DistritoResidenciaId);')
# add record near PgTerritorialSelection if present
s=s.replace('private sealed record PgTerritorialSelection(long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);', 'private sealed record PgTerritorialSelection(long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);\n    private sealed record PgResidentialSelection(long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraId, long? DistritoId);')
w(p,s)

# PostgreSQL fact materializer: append explicit residential columns/parameters alongside legacy fields.
p='src/Jornada.Processor.Worker/PostgreSqlProcessorRepository.Facts.cs'; s=r(p)
s=s.replace('referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id', 'referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id,distrito_residencia_id')
s=s.replace('@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito', '@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@residencia_geografia,@subprefeitura_residencia,@distrito_residencia')
param='''        Add(command, "@distrito", DbType.Int64, person.DistritoId);'''
if param in s:
 s=s.replace(param,param+'''\n        Add(command, "@residencia_geografia", DbType.Int64, person.EnderecoResidencialGeografiaObservacaoId);\n        Add(command, "@subprefeitura_residencia", DbType.Int64, person.SubprefeituraResidenciaId);\n        Add(command, "@distrito_residencia", DbType.Int64, person.DistritoResidenciaId);''',1)
w(p,s)

# 5) Current BI linkage name no longer calls the probabilistic model a fallback.
p='database/migrations/20260913_Endereco_Residencial_Sem_Fallback.sql'; s=r(p)
s += '''\nGO\nIF OBJECT_ID('serving.v_bi_linkage','V') IS NOT NULL\nBEGIN\n EXEC(N'CREATE OR ALTER VIEW serving.v_bi_linkage AS SELECT po.pessoa_observacao_id,po.pessoa_origem_id,po.gestor_id,po.codigo_pessoa_origem,po.cpf,CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,po.cpf_ausente_motivo,CASE WHEN DATEPART(DAY,po.data_nascimento)=1 AND DATEPART(MONTH,po.data_nascimento)=1 THEN 1 ELSE 0 END nascimento_0101,CASE WHEN rg.subprefeitura_id IS NULL OR rg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_residencia_preenchida,ml.versao modelo_linkage_versao,vc.linkage_run_id,lr.tipo_run,lr.iniciado_em linkage_run_iniciado_em,lr.finalizado_em linkage_run_finalizado_em,ptl.valor t_linkage,lr.status linkage_run_status,COALESCE(sp.nome,''SEM_ENDERECO_RESIDENCIAL_RESOLVIDO'') subprefeitura_residencia,COALESCE(d.nome,''SEM_ENDERECO_RESIDENCIAL_RESOLVIDO'') distrito_residencia FROM silver.pessoa_observacao po LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id=vc.modelo_id LEFT JOIN identidade.linkage_run lr ON lr.linkage_run_id=vc.linkage_run_id LEFT JOIN identidade.parametro_threshold_linkage ptl ON ptl.modelo_id=vc.modelo_id AND ptl.tipo=''T_LINKAGE'' LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=rg.subprefeitura_id LEFT JOIN ref.distrito d ON d.distrito_id=rg.distrito_id;');\nEND;\n'''
w(p,s)
# semantic model rename if column exists
p='bi/Jornada.SemanticModel/definition/tables/Linkage.tmdl'; s=r(p).replace('modelo_fallback_versao','modelo_linkage_versao').replace('subprefeitura','subprefeitura_residencia').replace('distrito','distrito_residencia'); w(p,s)

# 6) Tests ensure current facts/pg/readiness carry residence directly.
p='tests/Jornada.Tests/Unit/NoSemanticFallbackContractTests.cs'; s=r(p)
s=s.replace('Assert.That(migration, Does.Contain("fonte_semantica=\'ENDERECO_RESIDENCIAL\'"));', 'Assert.That(migration, Does.Contain("fonte_semantica=\'ENDERECO_RESIDENCIAL\'"));\n        Assert.That(migration, Does.Contain("subprefeitura_residencia_id"));\n        Assert.That(migration, Does.Contain("distrito_residencia_id"));')
w(p,s)
print('finish patch complete')

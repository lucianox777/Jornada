SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='ref') EXEC('CREATE SCHEMA ref');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='controle') EXEC('CREATE SCHEMA controle');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='ingestao') EXEC('CREATE SCHEMA ingestao');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='bronze') EXEC('CREATE SCHEMA bronze');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='silver') EXEC('CREATE SCHEMA silver');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='qualidade') EXEC('CREATE SCHEMA qualidade');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='identidade') EXEC('CREATE SCHEMA identidade');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='gold') EXEC('CREATE SCHEMA gold');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='serving') EXEC('CREATE SCHEMA serving');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name='auditoria') EXEC('CREATE SCHEMA auditoria');
GO

IF OBJECT_ID('ref.gestor','U') IS NULL CREATE TABLE ref.gestor(
 gestor_id BIGINT IDENTITY PRIMARY KEY, codigo NVARCHAR(30) NOT NULL UNIQUE, nome NVARCHAR(200) NOT NULL, ativo BIT NOT NULL DEFAULT(1));
GO

-- Sistema de origem é o namespace técnico que atribui as chaves internas de Pessoa e de Registro.
-- Um mesmo Gestor pode operar vários sistemas; códigos de origem só são únicos dentro de um sistema.
IF OBJECT_ID('ref.sistema_origem','U') IS NULL CREATE TABLE ref.sistema_origem(
 sistema_origem_id BIGINT IDENTITY PRIMARY KEY,
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 codigo NVARCHAR(80) NOT NULL,
 nome NVARCHAR(200) NOT NULL,
 ativo BIT NOT NULL DEFAULT(1),
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_sistema_origem_codigo UNIQUE(gestor_id,codigo),
 CONSTRAINT uq_sistema_origem_contexto UNIQUE(sistema_origem_id,gestor_id),
 CONSTRAINT ck_sistema_origem_codigo CHECK(LEN(codigo) BETWEEN 1 AND 80 AND codigo NOT LIKE '%[^A-Z0-9_-]%' COLLATE Latin1_General_100_BIN2));
GO
-- Dimensões geográficas observadas. Distrito é unidade territorial; Subprefeitura é o órgão que administra vários Distritos.
-- A relação pode mudar no tempo. Cada combinação observada de código/nome/hierarquia recebe identidade própria;
-- snapshots antigos permanecem apontando para a versão territorial que foi efetivamente selecionada como referência da Pessoa.
-- Não se infere vigência jurídica a partir da ordem de chegada dos dados e não há API geográfica pública da Jornada.
IF OBJECT_ID('ref.subprefeitura','U') IS NULL CREATE TABLE ref.subprefeitura(
 subprefeitura_id BIGINT IDENTITY PRIMARY KEY,
 codigo NVARCHAR(30) NOT NULL, nome NVARCHAR(150) NOT NULL,
 observado_em DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT uq_subprefeitura_versao_observada UNIQUE(codigo,nome));
GO
IF OBJECT_ID('ref.distrito','U') IS NULL CREATE TABLE ref.distrito(
 distrito_id BIGINT IDENTITY PRIMARY KEY,
 subprefeitura_id BIGINT NOT NULL REFERENCES ref.subprefeitura(subprefeitura_id),
 codigo NVARCHAR(30) NOT NULL, nome NVARCHAR(150) NOT NULL,
 observado_em DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT uq_distrito_versao_observada UNIQUE(codigo,nome,subprefeitura_id),
 CONSTRAINT uq_distrito_hierarquia UNIQUE(distrito_id,subprefeitura_id));
GO


-- Catálogo factual único. Natureza é dado, não estrutura: nova Natureza não exige nova tabela de referência.
IF OBJECT_ID('ref.tipo_registro','U') IS NULL CREATE TABLE ref.tipo_registro(
 tipo_registro_id BIGINT IDENTITY PRIMARY KEY,
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 natureza NVARCHAR(30) NOT NULL,
 codigo CHAR(4) NOT NULL UNIQUE,
 nome NVARCHAR(200) NOT NULL,
 ativo BIT NOT NULL DEFAULT(1),
 CONSTRAINT ck_tipo_registro_codigo CHECK(LEN(codigo)=4 AND codigo NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT uq_tipo_registro_gestor UNIQUE(tipo_registro_id,gestor_id),
 CONSTRAINT uq_tipo_registro_contexto UNIQUE(tipo_registro_id,gestor_id,natureza));
GO
IF OBJECT_ID('ref.tipo_registro_versao','U') IS NULL CREATE TABLE ref.tipo_registro_versao(
 tipo_registro_versao_id BIGINT IDENTITY PRIMARY KEY,
 tipo_registro_id BIGINT NOT NULL REFERENCES ref.tipo_registro(tipo_registro_id),
 versao INT NOT NULL,
 vigencia_inicio DATE NOT NULL,
 vigencia_fim DATE NULL,
 regras_texto NVARCHAR(MAX) NOT NULL,
 schema_pessoa_ref NVARCHAR(255) NOT NULL,
 schema_pessoa_sha256 BINARY(32) NULL,
 schema_registro_ref NVARCHAR(255) NOT NULL,
 schema_registro_sha256 BINARY(32) NULL,
 tipo_medida NVARCHAR(30) NOT NULL,
 data_inicio_permitida_concessao DATE NULL,
 data_fim_permitida_concessao DATE NULL,
 regime_vigencia NVARCHAR(30) NULL,
 monitorar_atraso BIT NOT NULL DEFAULT(0),
 prazo_recebimento_dias INT NULL,
 marco_atraso_codigo NVARCHAR(30) NULL,
 qc_status NVARCHAR(20) NOT NULL DEFAULT('NAO_IMPLEMENTADO'),
 status NVARCHAR(20) NOT NULL,
 UNIQUE(tipo_registro_id,versao),
 CONSTRAINT uq_trv_tipo UNIQUE(tipo_registro_versao_id,tipo_registro_id),
 CONSTRAINT ck_trv_vig CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio),
 CONSTRAINT ck_trv_tipo_medida CHECK(tipo_medida IN('MONETARIO','QUANTIDADE','MONETARIO_E_QUANTIDADE','SEM_MEDIDA')),
 CONSTRAINT ck_trv_janela_concessao CHECK(data_fim_permitida_concessao IS NULL OR data_inicio_permitida_concessao IS NULL OR data_fim_permitida_concessao>=data_inicio_permitida_concessao),
 CONSTRAINT ck_trv_regime_vigencia CHECK(regime_vigencia IS NULL OR regime_vigencia IN('PRAZO_DETERMINADO','PRAZO_INDETERMINADO','NAO_APLICAVEL')),
 CONSTRAINT ck_trv_atraso CHECK(
   (monitorar_atraso=0 AND prazo_recebimento_dias IS NULL AND marco_atraso_codigo IS NULL) OR
   (monitorar_atraso=1 AND prazo_recebimento_dias BETWEEN 0 AND 3650 AND marco_atraso_codigo IN('DATA_EVENTO_CONCESSAO','DATA_INICIO_CONCESSAO','DATA_FIM_CONCESSAO','DATA_HORA_SERVICO','SOURCE_AS_OF'))),
 CONSTRAINT ck_trv_qc CHECK(qc_status IN('IMPLEMENTADO','NAO_IMPLEMENTADO')),
 CONSTRAINT ck_trv_status CHECK(status IN('RASCUNHO','ATIVA','ENCERRADA')));
GO
-- Evolução aditiva v3.16: SLA de recebimento versionado por Tipo.
IF COL_LENGTH('ref.tipo_registro_versao','monitorar_atraso') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD monitorar_atraso BIT NOT NULL CONSTRAINT DF_trv_monitorar_atraso DEFAULT(0);
IF COL_LENGTH('ref.tipo_registro_versao','prazo_recebimento_dias') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD prazo_recebimento_dias INT NULL;
IF COL_LENGTH('ref.tipo_registro_versao','marco_atraso_codigo') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD marco_atraso_codigo NVARCHAR(30) NULL;
-- v3.48: semântica do fato BENEFICIO_CONCEDIDO e regras da política pertencem à versão do Tipo.
IF COL_LENGTH('ref.tipo_registro_versao','data_inicio_permitida_concessao') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD data_inicio_permitida_concessao DATE NULL;
IF COL_LENGTH('ref.tipo_registro_versao','data_fim_permitida_concessao') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD data_fim_permitida_concessao DATE NULL;
IF COL_LENGTH('ref.tipo_registro_versao','regime_vigencia') IS NULL
 ALTER TABLE ref.tipo_registro_versao ADD regime_vigencia NVARCHAR(30) NULL;
UPDATE v SET regime_vigencia='NAO_APLICAVEL'
FROM ref.tipo_registro_versao v JOIN ref.tipo_registro tr ON tr.tipo_registro_id=v.tipo_registro_id
WHERE tr.natureza='SERVICO' AND v.regime_vigencia IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ref.tipo_registro_versao') AND name='ck_trv_janela_concessao')
 ALTER TABLE ref.tipo_registro_versao WITH CHECK ADD CONSTRAINT ck_trv_janela_concessao CHECK(data_fim_permitida_concessao IS NULL OR data_inicio_permitida_concessao IS NULL OR data_fim_permitida_concessao>=data_inicio_permitida_concessao);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ref.tipo_registro_versao') AND name='ck_trv_regime_vigencia')
 ALTER TABLE ref.tipo_registro_versao WITH CHECK ADD CONSTRAINT ck_trv_regime_vigencia CHECK(regime_vigencia IS NULL OR regime_vigencia IN('PRAZO_DETERMINADO','PRAZO_INDETERMINADO','NAO_APLICAVEL'));
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ref.tipo_registro_versao') AND name='ck_trv_atraso')
 ALTER TABLE ref.tipo_registro_versao DROP CONSTRAINT ck_trv_atraso;
UPDATE ref.tipo_registro_versao SET marco_atraso_codigo=CASE marco_atraso_codigo
 WHEN 'DATA_EVENTO' THEN 'DATA_EVENTO_CONCESSAO'
 WHEN 'DATA_INICIO' THEN 'DATA_INICIO_CONCESSAO'
 WHEN 'DATA_FIM' THEN 'DATA_FIM_CONCESSAO'
 ELSE marco_atraso_codigo END
WHERE marco_atraso_codigo IN('DATA_EVENTO','DATA_INICIO','DATA_FIM');
ALTER TABLE ref.tipo_registro_versao WITH CHECK ADD CONSTRAINT ck_trv_atraso CHECK(
   (monitorar_atraso=0 AND prazo_recebimento_dias IS NULL AND marco_atraso_codigo IS NULL) OR
   (monitorar_atraso=1 AND prazo_recebimento_dias BETWEEN 0 AND 3650 AND marco_atraso_codigo IN('DATA_EVENTO_CONCESSAO','DATA_INICIO_CONCESSAO','DATA_FIM_CONCESSAO','DATA_HORA_SERVICO','SOURCE_AS_OF')));
GO

-- v3.43: a aprovação do contrato congela também o SHA-256 dos bytes publicados.
-- A referência de caminho sozinha não protege contra substituição silenciosa após restart/deploy.
IF COL_LENGTH('ref.tipo_registro_versao','schema_pessoa_sha256') IS NULL ALTER TABLE ref.tipo_registro_versao ADD schema_pessoa_sha256 BINARY(32) NULL;
IF COL_LENGTH('ref.tipo_registro_versao','schema_registro_sha256') IS NULL ALTER TABLE ref.tipo_registro_versao ADD schema_registro_sha256 BINARY(32) NULL;
GO
CREATE OR ALTER TRIGGER ref.tr_tipo_registro_versao_schema_hash ON ref.tipo_registro_versao AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE status='ATIVA' AND (schema_pessoa_sha256 IS NULL OR schema_registro_sha256 IS NULL))
   THROW 51072,'Versão ATIVA de Tipo exige SHA-256 aprovado dos schemas de Pessoa e Registro.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN ref.tipo_registro tr ON tr.tipo_registro_id=i.tipo_registro_id
           WHERE i.status='ATIVA' AND tr.natureza='BENEFICIO' AND (i.regime_vigencia IS NULL OR i.regime_vigencia='NAO_APLICAVEL'))
   THROW 51074,'Versão ATIVA de Benefício exige regime_vigencia PRAZO_DETERMINADO ou PRAZO_INDETERMINADO.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN ref.tipo_registro tr ON tr.tipo_registro_id=i.tipo_registro_id
           WHERE i.status='ATIVA' AND tr.natureza='SERVICO' AND (i.regime_vigencia IS NULL OR i.regime_vigencia<>'NAO_APLICAVEL' OR i.data_inicio_permitida_concessao IS NOT NULL OR i.data_fim_permitida_concessao IS NOT NULL))
   THROW 51075,'Versão ATIVA de Serviço exige regime_vigencia NAO_APLICAVEL e não admite janela de concessão.',1;
END;
GO

-- Catálogo que torna atributos transversais extensíveis sem ALTER TABLE em gold.pessoa.
IF OBJECT_ID('ref.atributo_transversal','U') IS NULL CREATE TABLE ref.atributo_transversal(
 atributo_codigo NVARCHAR(80) PRIMARY KEY,
 nome NVARCHAR(200) NOT NULL,
 formato_codigo NVARCHAR(80) NOT NULL,
 regra_temporal_codigo NVARCHAR(80) NOT NULL,
 descricao NVARCHAR(1000) NULL,
 ativo BIT NOT NULL DEFAULT(1),
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()));
GO


-- Contrato cadastral versionado por Gestor.
IF OBJECT_ID('ref.gestor_pessoa_versao','U') IS NULL CREATE TABLE ref.gestor_pessoa_versao(
 gestor_pessoa_versao_id BIGINT IDENTITY PRIMARY KEY, gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id), versao INT NOT NULL,
 vigencia_inicio DATE NOT NULL, vigencia_fim DATE NULL, pessoa_schema_ref NVARCHAR(255) NOT NULL, pessoa_schema_sha256 BINARY(32) NULL, status NVARCHAR(20) NOT NULL,
 ativado_em DATETIMEOFFSET(7) NULL, criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()), UNIQUE(gestor_id,versao),
 CONSTRAINT uq_gpv_gestor UNIQUE(gestor_pessoa_versao_id,gestor_id),
 CONSTRAINT ck_gpv_vig CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio),
 CONSTRAINT ck_gpv_status CHECK(status IN('RASCUNHO','ATIVA','ENCERRADA')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ref.gestor_pessoa_versao') AND name='UX_ref_gestor_pessoa_versao_ativa')
 CREATE UNIQUE INDEX UX_ref_gestor_pessoa_versao_ativa ON ref.gestor_pessoa_versao(gestor_id) WHERE status='ATIVA';
GO
IF COL_LENGTH('ref.gestor_pessoa_versao','pessoa_schema_sha256') IS NULL ALTER TABLE ref.gestor_pessoa_versao ADD pessoa_schema_sha256 BINARY(32) NULL;
GO
CREATE OR ALTER TRIGGER ref.tr_gestor_pessoa_versao_schema_hash ON ref.gestor_pessoa_versao AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE status='ATIVA' AND pessoa_schema_sha256 IS NULL)
   THROW 51073,'Versão ATIVA de contrato cadastral exige SHA-256 aprovado do pessoa.schema.json.',1;
END;
GO

-- v3.47 - finalidade declarada foi removida da fronteira de autorização da Fase 1.
-- A Jornada autoriza por credencial, scope e recurso; auditoria registra fatos observáveis da chamada.

-- Credenciais de Tipo distinguem BENEFICIO/SERVICO e permanecem limitadas ao próprio recurso por scope/código.
-- A população de Pessoa, porém, é municipal: nenhuma credencial autorizada depende de fato prévio daquele Tipo.
IF OBJECT_ID('controle.credencial_api','U') IS NULL CREATE TABLE controle.credencial_api(
 credencial_id UNIQUEIDENTIFIER PRIMARY KEY,
 tipo_credencial NVARCHAR(20) NOT NULL,
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 tipo_registro_id BIGINT NULL,
 codigo_publico NVARCHAR(200) NOT NULL,
 secret_ref NVARCHAR(300) NOT NULL,
 scopes NVARCHAR(MAX) NOT NULL,
 ativo BIT NOT NULL DEFAULT(1),
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 rotacionado_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT uq_credencial_codigo UNIQUE(tipo_credencial,codigo_publico),
 CONSTRAINT fk_cred_tipo_gestor FOREIGN KEY(tipo_registro_id,gestor_id) REFERENCES ref.tipo_registro(tipo_registro_id,gestor_id),
 CONSTRAINT ck_credencial_tipo CHECK(
   (tipo_credencial='GESTOR' AND tipo_registro_id IS NULL) OR
   (tipo_credencial IN('BENEFICIO','SERVICO') AND tipo_registro_id IS NOT NULL)));
GO
-- Migração de instalações anteriores à v3.40: a antiga lista JSON de finalidades deixa de ter semântica na v3.47.
IF COL_LENGTH('controle.credencial_api','finalidades') IS NOT NULL
BEGIN
 IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('controle.credencial_api') AND name='ck_credencial_finalidades_json')
  ALTER TABLE controle.credencial_api DROP CONSTRAINT ck_credencial_finalidades_json;
 DECLARE @df_credencial_finalidades SYSNAME=(SELECT dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID('controle.credencial_api') AND c.name='finalidades');
 IF @df_credencial_finalidades IS NOT NULL
 BEGIN
  DECLARE @sql_drop_credencial_finalidades NVARCHAR(MAX)=N'ALTER TABLE controle.credencial_api DROP CONSTRAINT '+QUOTENAME(@df_credencial_finalidades);
  EXEC sys.sp_executesql @sql_drop_credencial_finalidades;
 END;
 ALTER TABLE controle.credencial_api DROP COLUMN finalidades;
END;
GO
-- v3.47: controle.credencial_finalidade é legado e será removida após a migração das tabelas dependentes.

-- v3.47 - compartilhamento municipal por padrão. Esta tabela contém somente EXCEÇÕES NEGATIVAS de projeção.
-- Ausência de restrição ATIVA = projeção municipal padrão. O Gestor responsável assume institucionalmente a declaração;
-- ato_referencia aponta para o processo/ato que a sustenta. Não existe condicionamento por finalidade declarada.
IF OBJECT_ID('controle.restricao_projecao_jornada_versao','U') IS NULL CREATE TABLE controle.restricao_projecao_jornada_versao(
 restricao_projecao_id BIGINT IDENTITY PRIMARY KEY,
 regra_codigo NVARCHAR(100) NOT NULL,
 versao INT NOT NULL,
 gestor_responsavel_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 gestor_consumidor_id BIGINT NULL REFERENCES ref.gestor(gestor_id),
 alvo_tipo NVARCHAR(30) NOT NULL,
 alvo_codigo NVARCHAR(80) NULL,
 status NVARCHAR(20) NOT NULL,
 vigencia_inicio DATETIMEOFFSET(7) NOT NULL,
 vigencia_fim DATETIMEOFFSET(7) NULL,
 fundamento_legal NVARCHAR(2000) NOT NULL,
 justificativa NVARCHAR(2000) NOT NULL,
 ato_referencia NVARCHAR(300) NOT NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_restricao_projecao_versao UNIQUE(gestor_responsavel_id,regra_codigo,versao),
 CONSTRAINT ck_restricao_projecao_versao CHECK(versao>=1),
 CONSTRAINT ck_restricao_projecao_alvo CHECK(alvo_tipo IN('ATRIBUTO_PESSOA','REGISTRO','POSSIBILIDADE')),
 CONSTRAINT ck_restricao_projecao_status CHECK(status IN('RASCUNHO','ATIVA','REVOGADA')),
 CONSTRAINT ck_restricao_projecao_vigencia CHECK(vigencia_fim IS NULL OR vigencia_fim>vigencia_inicio));
GO
-- Migração: regras antes condicionadas a finalidade são preservadas como exceções negativas gerais para o mesmo
-- Gestor consumidor/alvo. A remoção não descarta proteção existente; a governança deve revisar eventual ampliação.
IF COL_LENGTH('controle.restricao_projecao_jornada_versao','ato_referencia') IS NULL ALTER TABLE controle.restricao_projecao_jornada_versao ADD ato_referencia NVARCHAR(300) NULL;
GO
IF COL_LENGTH('controle.restricao_projecao_jornada_versao','declarado_por') IS NOT NULL
 EXEC(N'UPDATE controle.restricao_projecao_jornada_versao SET ato_referencia=LEFT(CONCAT(N''MIGRADO_V3_40:'',declarado_por),300) WHERE ato_referencia IS NULL;');
UPDATE controle.restricao_projecao_jornada_versao SET ato_referencia=N'MIGRADO:SEM_REFERENCIA' WHERE ato_referencia IS NULL;
ALTER TABLE controle.restricao_projecao_jornada_versao ALTER COLUMN ato_referencia NVARCHAR(300) NOT NULL;
GO
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='IX_restricao_projecao_lookup') DROP INDEX IX_restricao_projecao_lookup ON controle.restricao_projecao_jornada_versao;
IF OBJECT_ID('controle.tr_restricao_projecao_jornada_versao_immutavel','TR') IS NOT NULL DROP TRIGGER controle.tr_restricao_projecao_jornada_versao_immutavel;
IF OBJECT_ID('controle.tr_restricao_projecao_jornada_validar_alvo','TR') IS NOT NULL DROP TRIGGER controle.tr_restricao_projecao_jornada_validar_alvo;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='fk_restricao_projecao_finalidade') ALTER TABLE controle.restricao_projecao_jornada_versao DROP CONSTRAINT fk_restricao_projecao_finalidade;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='ck_restricao_projecao_finalidade') ALTER TABLE controle.restricao_projecao_jornada_versao DROP CONSTRAINT ck_restricao_projecao_finalidade;
IF COL_LENGTH('controle.restricao_projecao_jornada_versao','finalidade_id') IS NOT NULL ALTER TABLE controle.restricao_projecao_jornada_versao DROP COLUMN finalidade_id;
IF COL_LENGTH('controle.restricao_projecao_jornada_versao','finalidade') IS NOT NULL ALTER TABLE controle.restricao_projecao_jornada_versao DROP COLUMN finalidade;
IF COL_LENGTH('controle.restricao_projecao_jornada_versao','declarado_por') IS NOT NULL ALTER TABLE controle.restricao_projecao_jornada_versao DROP COLUMN declarado_por;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='UX_restricao_projecao_regra_ativa')
 CREATE UNIQUE INDEX UX_restricao_projecao_regra_ativa ON controle.restricao_projecao_jornada_versao(gestor_responsavel_id,regra_codigo) WHERE status='ATIVA';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='IX_restricao_projecao_lookup')
 CREATE INDEX IX_restricao_projecao_lookup ON controle.restricao_projecao_jornada_versao(gestor_responsavel_id,alvo_tipo,alvo_codigo,gestor_consumidor_id,status,vigencia_inicio,vigencia_fim);
GO

-- Conteúdo de uma declaração é imutável: qualquer alteração material exige nova versão.
CREATE OR ALTER TRIGGER controle.tr_restricao_projecao_jornada_versao_immutavel
ON controle.restricao_projecao_jornada_versao
AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i JOIN deleted d ON d.restricao_projecao_id=i.restricao_projecao_id
   WHERE i.regra_codigo<>d.regra_codigo OR i.versao<>d.versao OR i.gestor_responsavel_id<>d.gestor_responsavel_id
      OR ISNULL(i.gestor_consumidor_id,-1)<>ISNULL(d.gestor_consumidor_id,-1) OR i.alvo_tipo<>d.alvo_tipo
      OR ISNULL(i.alvo_codigo,N'')<>ISNULL(d.alvo_codigo,N'') OR i.vigencia_inicio<>d.vigencia_inicio
      OR ISNULL(i.vigencia_fim,'9999-12-31')<>ISNULL(d.vigencia_fim,'9999-12-31')
      OR i.fundamento_legal<>d.fundamento_legal OR i.justificativa<>d.justificativa OR i.ato_referencia<>d.ato_referencia
 ) THROW 51040, 'Restrição de projeção é versionada e imutável; publique nova versão para alterar conteúdo.', 1;
 IF EXISTS(
   SELECT 1 FROM inserted i JOIN deleted d ON d.restricao_projecao_id=i.restricao_projecao_id
   WHERE NOT ((d.status='RASCUNHO' AND i.status IN('RASCUNHO','ATIVA','REVOGADA')) OR (d.status='ATIVA' AND i.status IN('ATIVA','REVOGADA')) OR (d.status='REVOGADA' AND i.status='REVOGADA'))
 ) THROW 51041, 'Transição de status inválida para restrição de projeção.', 1;
END;
GO

-- Uma restrição ATIVA não pode apontar para alvo inexistente. Isso evita exceção negativa silenciosamente fail-open por erro de cadastro.
CREATE OR ALTER TRIGGER controle.tr_restricao_projecao_jornada_validar_alvo
ON controle.restricao_projecao_jornada_versao
AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i
   WHERE i.status='ATIVA' AND i.alvo_codigo IS NOT NULL AND (
      (i.alvo_tipo='ATRIBUTO_PESSOA' AND NOT EXISTS(SELECT 1 FROM ref.atributo_transversal a WHERE a.atributo_codigo=i.alvo_codigo AND a.ativo=1)) OR
      (i.alvo_tipo IN('REGISTRO','POSSIBILIDADE') AND NOT EXISTS(SELECT 1 FROM ref.tipo_registro tr WHERE tr.gestor_id=i.gestor_responsavel_id AND tr.codigo=i.alvo_codigo AND tr.ativo=1))))
  THROW 51043, 'Restrição ATIVA referencia alvo inexistente/inativo para o Gestor responsável.', 1;
END;
GO

-- Default-allow: a projeção só é reduzida quando existe exceção negativa ATIVA aplicável ao consumidor e alvo.
CREATE OR ALTER FUNCTION controle.fn_projecao_jornada_permitida(
 @gestor_responsavel_id BIGINT,
 @gestor_consumidor_id BIGINT,
 @alvo_tipo NVARCHAR(30),
 @alvo_codigo NVARCHAR(80)
) RETURNS BIT
AS
BEGIN
 IF @gestor_responsavel_id IS NULL OR @gestor_consumidor_id IS NULL OR @alvo_tipo IS NULL RETURN 0;
 IF EXISTS(
   SELECT 1
   FROM controle.restricao_projecao_jornada_versao r
   WHERE r.gestor_responsavel_id=@gestor_responsavel_id
     AND r.status='ATIVA'
     AND r.vigencia_inicio<=SYSDATETIMEOFFSET()
     AND (r.vigencia_fim IS NULL OR r.vigencia_fim>SYSDATETIMEOFFSET())
     AND r.alvo_tipo=@alvo_tipo
     AND (r.alvo_codigo IS NULL OR r.alvo_codigo=@alvo_codigo)
     AND (r.gestor_consumidor_id IS NULL OR r.gestor_consumidor_id=@gestor_consumidor_id)
 ) RETURN 0;
 RETURN 1;
END;
GO

-- Auditoria da fronteira Jornada. O agente humano é informação opcional declarada pela finalística.
-- A Jornada valida o CPF e persiste somente HMAC-SHA-256 determinístico (BINARY(32)); não autentica o agente nem conserva o CPF em claro.
-- A v3.47 registra somente fatos observáveis da chamada; não persiste finalidade declarada nem flag de auditoria reforçada.
IF OBJECT_ID('controle.api_evento','U') IS NULL CREATE TABLE controle.api_evento(
 api_evento_id BIGINT IDENTITY PRIMARY KEY,
 credencial_id UNIQUEIDENTIFIER NULL REFERENCES controle.credencial_api(credencial_id),
 gestor_id BIGINT NULL REFERENCES ref.gestor(gestor_id),
 correlation_id UNIQUEIDENTIFIER NOT NULL,
 rota NVARCHAR(300) NOT NULL,
 metodo NVARCHAR(10) NOT NULL,
 status_http INT NOT NULL,
 duracao_ms INT NOT NULL,
 bytes_recebidos BIGINT NULL,
 pessoa_uuid UNIQUEIDENTIFIER NULL,
 recurso_codigo NVARCHAR(80) NULL,
 agente_cpf_hash BINARY(32) NULL,
 agente_hash_versao SMALLINT NULL,
 ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_api_evento_agente_hash CHECK((agente_cpf_hash IS NULL AND agente_hash_versao IS NULL) OR (agente_cpf_hash IS NOT NULL AND agente_hash_versao IS NOT NULL AND agente_hash_versao>=1)));
GO
IF COL_LENGTH('controle.api_evento','pessoa_uuid') IS NULL ALTER TABLE controle.api_evento ADD pessoa_uuid UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('controle.api_evento','recurso_codigo') IS NULL ALTER TABLE controle.api_evento ADD recurso_codigo NVARCHAR(80) NULL;
IF COL_LENGTH('controle.api_evento','agente_cpf_hash') IS NULL ALTER TABLE controle.api_evento ADD agente_cpf_hash BINARY(32) NULL;
IF COL_LENGTH('controle.api_evento','agente_hash_versao') IS NULL ALTER TABLE controle.api_evento ADD agente_hash_versao SMALLINT NULL;
GO
IF OBJECT_ID('serving.v_bi_api','V') IS NOT NULL DROP VIEW serving.v_bi_api;
GO
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.api_evento') AND name='IX_api_evento_pessoa_ocorrido') DROP INDEX IX_api_evento_pessoa_ocorrido ON controle.api_evento;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.api_evento') AND name='IX_api_evento_finalidade_ocorrido') DROP INDEX IX_api_evento_finalidade_ocorrido ON controle.api_evento;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('controle.api_evento') AND name='fk_api_evento_finalidade') ALTER TABLE controle.api_evento DROP CONSTRAINT fk_api_evento_finalidade;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('controle.api_evento') AND name='ck_api_evento_finalidade') ALTER TABLE controle.api_evento DROP CONSTRAINT ck_api_evento_finalidade;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('controle.api_evento') AND name='ck_api_evento_finalidade_dominio') ALTER TABLE controle.api_evento DROP CONSTRAINT ck_api_evento_finalidade_dominio;
IF COL_LENGTH('controle.api_evento','finalidade_id') IS NOT NULL ALTER TABLE controle.api_evento DROP COLUMN finalidade_id;
IF COL_LENGTH('controle.api_evento','finalidade') IS NOT NULL ALTER TABLE controle.api_evento DROP COLUMN finalidade;
IF COL_LENGTH('controle.api_evento','auditoria_reforcada') IS NOT NULL
BEGIN
 DECLARE @df_api_auditoria SYSNAME=(SELECT dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID('controle.api_evento') AND c.name='auditoria_reforcada');
 IF @df_api_auditoria IS NOT NULL
 BEGIN
  DECLARE @sql_drop_api_auditoria NVARCHAR(MAX)=N'ALTER TABLE controle.api_evento DROP CONSTRAINT '+QUOTENAME(@df_api_auditoria);
  EXEC sys.sp_executesql @sql_drop_api_auditoria;
 END;
 ALTER TABLE controle.api_evento DROP COLUMN auditoria_reforcada;
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('controle.api_evento') AND name='ck_api_evento_agente_hash')
 ALTER TABLE controle.api_evento ADD CONSTRAINT ck_api_evento_agente_hash CHECK((agente_cpf_hash IS NULL AND agente_hash_versao IS NULL) OR (agente_cpf_hash IS NOT NULL AND agente_hash_versao IS NOT NULL AND agente_hash_versao>=1));
GO
IF OBJECT_ID('controle.api_evento_pessoa','U') IS NULL CREATE TABLE controle.api_evento_pessoa(
 api_evento_id BIGINT NOT NULL REFERENCES controle.api_evento(api_evento_id) ON DELETE CASCADE,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL,
 PRIMARY KEY(api_evento_id,pessoa_uuid));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.api_evento') AND name='IX_api_evento_pessoa_ocorrido')
 CREATE INDEX IX_api_evento_pessoa_ocorrido ON controle.api_evento(pessoa_uuid,ocorrido_em DESC) INCLUDE(gestor_id,credencial_id,status_http,recurso_codigo) WHERE pessoa_uuid IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.api_evento') AND name='IX_api_evento_agente_hash_ocorrido')
 CREATE INDEX IX_api_evento_agente_hash_ocorrido ON controle.api_evento(agente_cpf_hash,agente_hash_versao,ocorrido_em DESC) INCLUDE(gestor_id,credencial_id,correlation_id,status_http) WHERE agente_cpf_hash IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.api_evento_pessoa') AND name='IX_api_evento_pessoa_uuid')
 CREATE INDEX IX_api_evento_pessoa_uuid ON controle.api_evento_pessoa(pessoa_uuid,api_evento_id);
GO

-- Remove o catálogo/allowlist de finalidade somente após eliminar suas dependências nas tabelas de controle.
IF OBJECT_ID('controle.credencial_finalidade','U') IS NOT NULL DROP TABLE controle.credencial_finalidade;
IF OBJECT_ID('ref.finalidade_versao','U') IS NOT NULL DROP TABLE ref.finalidade_versao;
IF OBJECT_ID('ref.finalidade','U') IS NOT NULL DROP TABLE ref.finalidade;
GO

-- Uma chamada externa = uma Entrega ZIP com envelope único: manifest.json + pessoas.jsonl + registros.jsonl.
-- pessoas.jsonl contém, no mínimo, Pessoas incluídas/alteradas no período e todas as Pessoas relacionadas aos fatos da Entrega.
-- registros.jsonl pode estar vazio. Natureza/Tipo são opcionais no cadastro puro e, quando informados, descrevem o contexto factual da Entrega.
-- Toda Entrega declara o sistema de origem; a finalística não envia versão do registro de origem.
-- O cliente não controla lote_seq/lote_total. O nome canônico do ZIP contém o SHA-256 dos próprios bytes.
IF OBJECT_ID('ingestao.entrega','U') IS NULL CREATE TABLE ingestao.entrega(
 entrega_id UNIQUEIDENTIFIER PRIMARY KEY,
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 sistema_origem_id BIGINT NOT NULL,
 gestor_pessoa_versao_id BIGINT NOT NULL REFERENCES ref.gestor_pessoa_versao(gestor_pessoa_versao_id),
 natureza NVARCHAR(30) NULL,
 tipo_registro_id BIGINT NULL,
 tipo_registro_versao_id BIGINT NULL,
 idempotency_key NVARCHAR(200) NOT NULL,
 payload_sha256 CHAR(64) NOT NULL,
 bytes_recebidos BIGINT NOT NULL,
 status NVARCHAR(30) NOT NULL,
 data_referencia DATETIMEOFFSET(7) NOT NULL,
 recebido_em DATETIMEOFFSET(7) NOT NULL,
 ultima_atualizacao DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 UNIQUE(gestor_id,idempotency_key),
 CONSTRAINT fk_entrega_sistema_origem FOREIGN KEY(sistema_origem_id,gestor_id) REFERENCES ref.sistema_origem(sistema_origem_id,gestor_id),
 CONSTRAINT fk_entrega_gestor_pessoa_versao FOREIGN KEY(gestor_pessoa_versao_id,gestor_id) REFERENCES ref.gestor_pessoa_versao(gestor_pessoa_versao_id,gestor_id),
 CONSTRAINT fk_entrega_tipo_contexto FOREIGN KEY(tipo_registro_id,gestor_id,natureza) REFERENCES ref.tipo_registro(tipo_registro_id,gestor_id,natureza),
 CONSTRAINT fk_entrega_tipo_versao FOREIGN KEY(tipo_registro_versao_id,tipo_registro_id) REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id,tipo_registro_id),
 CONSTRAINT ck_entrega_contexto_factual CHECK(
   (natureza IS NULL AND tipo_registro_id IS NULL AND tipo_registro_versao_id IS NULL) OR
   (natureza IN('BENEFICIO','SERVICO') AND tipo_registro_id IS NOT NULL AND tipo_registro_versao_id IS NOT NULL)),
 CONSTRAINT ck_entrega_recebido_utc CHECK(DATEPART(TZOFFSET,recebido_em)=0),
 CONSTRAINT ck_entrega_sha CHECK(LEN(payload_sha256)=64 AND payload_sha256 NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT ck_entrega_status CHECK(status IN('RECEBIDA','VALIDANDO','PROCESSANDO','PROCESSADA','REJEITADA','QUARENTENA')));
GO
-- Migração v3.26: remove a antiga distinção estrutural PESSOA/REGISTRO; o envelope passa a ser único.
IF COL_LENGTH('ingestao.entrega','familia_entrega') IS NOT NULL
BEGIN
 IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.entrega') AND name='ck_entrega_familia')
  ALTER TABLE ingestao.entrega DROP CONSTRAINT ck_entrega_familia;
 ALTER TABLE ingestao.entrega DROP COLUMN familia_entrega;
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.entrega') AND name='ck_entrega_contexto_factual')
 ALTER TABLE ingestao.entrega WITH CHECK ADD CONSTRAINT ck_entrega_contexto_factual CHECK(
   (natureza IS NULL AND tipo_registro_id IS NULL AND tipo_registro_versao_id IS NULL) OR
   (natureza IN('BENEFICIO','SERVICO') AND tipo_registro_id IS NOT NULL AND tipo_registro_versao_id IS NOT NULL));
GO

-- Lote é detalhe técnico interno do Processor, não parte do contrato HTTP.
IF OBJECT_ID('ingestao.lote','U') IS NULL CREATE TABLE ingestao.lote(
 lote_id UNIQUEIDENTIFIER PRIMARY KEY,
 entrega_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.entrega(entrega_id),
 lote_seq INT NOT NULL,
 lote_total INT NOT NULL,
 qtd_pessoas INT NOT NULL DEFAULT(0),
 qtd_registros INT NOT NULL DEFAULT(0),
 status NVARCHAR(40) NOT NULL,
 erro_codigo NVARCHAR(80) NULL,
 tentativa_count INT NOT NULL DEFAULT(0),
 recuperacao_count INT NOT NULL DEFAULT(0),
 ultima_tentativa_em DATETIMEOFFSET(7) NULL,
 proxima_tentativa_em DATETIMEOFFSET(7) NULL,
 lease_id UNIQUEIDENTIFIER NULL,
 lease_owner NVARCHAR(200) NULL,
 lease_adquirido_em DATETIMEOFFSET(7) NULL,
 heartbeat_em DATETIMEOFFSET(7) NULL,
 lease_expira_em DATETIMEOFFSET(7) NULL,
 poison_em DATETIMEOFFSET(7) NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 UNIQUE(entrega_id,lote_seq),
 CONSTRAINT ck_lote_seq CHECK(lote_seq>=1 AND lote_total>=1 AND lote_seq<=lote_total),
 CONSTRAINT ck_lote_qtd CHECK(qtd_pessoas>=0 AND qtd_registros>=0),
 CONSTRAINT ck_lote_tentativas CHECK(tentativa_count>=0 AND recuperacao_count>=0),
 CONSTRAINT ck_lote_status CHECK(status IN('PENDENTE','VALIDANDO','PROCESSANDO','PROCESSADO','REJEITADO','QUARENTENA','POISON')),
 CONSTRAINT ck_lote_lease CHECK(
   (status IN('VALIDANDO','PROCESSANDO') AND lease_id IS NOT NULL AND lease_owner IS NOT NULL AND lease_adquirido_em IS NOT NULL AND heartbeat_em IS NOT NULL AND lease_expira_em IS NOT NULL) OR
   (status NOT IN('VALIDANDO','PROCESSANDO') AND lease_id IS NULL AND lease_owner IS NULL AND lease_adquirido_em IS NULL AND heartbeat_em IS NULL AND lease_expira_em IS NULL)));
GO

-- v3.33: resiliência do Processor. Lease com fencing token, heartbeat, retry controlado e estado POISON.
IF COL_LENGTH('ingestao.lote','tentativa_count') IS NULL ALTER TABLE ingestao.lote ADD tentativa_count INT NOT NULL CONSTRAINT df_lote_tentativa_count DEFAULT(0);
IF COL_LENGTH('ingestao.lote','recuperacao_count') IS NULL ALTER TABLE ingestao.lote ADD recuperacao_count INT NOT NULL CONSTRAINT df_lote_recuperacao_count DEFAULT(0);
IF COL_LENGTH('ingestao.lote','ultima_tentativa_em') IS NULL ALTER TABLE ingestao.lote ADD ultima_tentativa_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('ingestao.lote','proxima_tentativa_em') IS NULL ALTER TABLE ingestao.lote ADD proxima_tentativa_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('ingestao.lote','lease_id') IS NULL ALTER TABLE ingestao.lote ADD lease_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('ingestao.lote','lease_owner') IS NULL ALTER TABLE ingestao.lote ADD lease_owner NVARCHAR(200) NULL;
IF COL_LENGTH('ingestao.lote','lease_adquirido_em') IS NULL ALTER TABLE ingestao.lote ADD lease_adquirido_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('ingestao.lote','heartbeat_em') IS NULL ALTER TABLE ingestao.lote ADD heartbeat_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('ingestao.lote','lease_expira_em') IS NULL ALTER TABLE ingestao.lote ADD lease_expira_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('ingestao.lote','poison_em') IS NULL ALTER TABLE ingestao.lote ADD poison_em DATETIMEOFFSET(7) NULL;
GO
-- Instalações anteriores podem ter ficado em VALIDANDO/PROCESSANDO sem lease; devolve-as a PENDENTE antes das constraints.
UPDATE ingestao.lote
 SET status='PENDENTE',erro_codigo='MIGRADO_PARA_LEASE_V333',atualizado_em=SYSUTCDATETIME()
 WHERE status IN('VALIDANDO','PROCESSANDO') AND lease_id IS NULL;
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.lote') AND name='ck_lote_status')
 ALTER TABLE ingestao.lote DROP CONSTRAINT ck_lote_status;
ALTER TABLE ingestao.lote WITH CHECK ADD CONSTRAINT ck_lote_status CHECK(status IN('PENDENTE','VALIDANDO','PROCESSANDO','PROCESSADO','REJEITADO','QUARENTENA','POISON'));
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.lote') AND name='ck_lote_tentativas')
 ALTER TABLE ingestao.lote WITH CHECK ADD CONSTRAINT ck_lote_tentativas CHECK(tentativa_count>=0 AND recuperacao_count>=0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.lote') AND name='ck_lote_lease')
 ALTER TABLE ingestao.lote WITH CHECK ADD CONSTRAINT ck_lote_lease CHECK(
   (status IN('VALIDANDO','PROCESSANDO') AND lease_id IS NOT NULL AND lease_owner IS NOT NULL AND lease_adquirido_em IS NOT NULL AND heartbeat_em IS NOT NULL AND lease_expira_em IS NOT NULL) OR
   (status NOT IN('VALIDANDO','PROCESSANDO') AND lease_id IS NULL AND lease_owner IS NULL AND lease_adquirido_em IS NULL AND heartbeat_em IS NULL AND lease_expira_em IS NULL));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.lote') AND name='IX_lote_fila_retry')
 CREATE INDEX IX_lote_fila_retry ON ingestao.lote(status,proxima_tentativa_em,criado_em) INCLUDE(entrega_id,lote_seq,tentativa_count);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.lote') AND name='IX_lote_lease_expira')
 CREATE INDEX IX_lote_lease_expira ON ingestao.lote(status,lease_expira_em) INCLUDE(entrega_id,lease_id,lease_owner,tentativa_count,recuperacao_count) WHERE lease_expira_em IS NOT NULL;
GO

-- Resultado individual do processamento. Retransmissões idênticas são observáveis sem criar nova versão Silver.
IF OBJECT_ID('ingestao.item_processado','U') IS NULL CREATE TABLE ingestao.item_processado(
 item_processado_id BIGINT IDENTITY PRIMARY KEY,
 lote_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.lote(lote_id),
 classe_item NVARCHAR(20) NOT NULL,
 pessoa_origem_id BIGINT NULL,
 registro_origem_id BIGINT NULL,
 codigo_origem NVARCHAR(255) NOT NULL,
 resultado NVARCHAR(30) NOT NULL,
 versao_interna INT NOT NULL,
 conteudo_hash CHAR(64) NOT NULL,
 data_referencia DATETIMEOFFSET(7) NOT NULL,
 processado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_item_processado_lote_codigo UNIQUE(lote_id,classe_item,codigo_origem),
 CONSTRAINT ck_item_processado_classe CHECK(classe_item IN('PESSOA','REGISTRO')),
 CONSTRAINT ck_item_processado_resultado CHECK(resultado IN('INCLUIDO','VERSIONADO','RETRANSMITIDO','EXCLUIDO','REABERTO')),
 CONSTRAINT ck_item_processado_versao CHECK(versao_interna>=1),
 CONSTRAINT ck_item_processado_hash CHECK(LEN(conteudo_hash)=64 AND conteudo_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT ck_item_processado_origem CHECK(
   (classe_item='PESSOA' AND pessoa_origem_id IS NOT NULL AND registro_origem_id IS NULL) OR
   (classe_item='REGISTRO' AND pessoa_origem_id IS NULL AND registro_origem_id IS NOT NULL)));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.item_processado') AND name='IX_item_processado_resultado')
 CREATE INDEX IX_item_processado_resultado ON ingestao.item_processado(resultado,processado_em DESC) INCLUDE(lote_id,classe_item,codigo_origem,versao_interna);
GO
-- Índice de apoio ao caminho natural do BI/observabilidade: item -> lote -> entrega -> Gestor/Tipo.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.item_processado') AND name='IX_item_processado_lote_classe')
 CREATE INDEX IX_item_processado_lote_classe ON ingestao.item_processado(lote_id,classe_item) INCLUDE(resultado,processado_em,codigo_origem,versao_interna,pessoa_origem_id,registro_origem_id);
GO
-- Retenção: item_processado é trilha operacional detalhada, não fato Golden. Na v3.39 a rotina de consolidação
-- existe, porém permanece DESABILITADA por configuração até a governança aprovar a janela. A consolidação preserva
-- contagens horárias por Entrega/classe/resultado e a última confirmação RETRANSMITIDO de cada origem.
IF COL_LENGTH('ingestao.item_processado','consolidado_em') IS NULL
 ALTER TABLE ingestao.item_processado ADD consolidado_em DATETIMEOFFSET(7) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.item_processado') AND name='IX_item_processado_retencao')
 CREATE INDEX IX_item_processado_retencao ON ingestao.item_processado(consolidado_em,processado_em,item_processado_id)
 INCLUDE(lote_id,classe_item,resultado,pessoa_origem_id,registro_origem_id);
GO
IF OBJECT_ID('ingestao.item_processado_resumo','U') IS NULL CREATE TABLE ingestao.item_processado_resumo(
 item_processado_resumo_id BIGINT IDENTITY PRIMARY KEY,
 entrega_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.entrega(entrega_id),
 processado_hora_utc DATETIME2(0) NOT NULL,
 classe_item NVARCHAR(20) NOT NULL,
 resultado NVARCHAR(30) NOT NULL,
 quantidade BIGINT NOT NULL,
 primeiro_processado_em DATETIMEOFFSET(7) NOT NULL,
 ultimo_processado_em DATETIMEOFFSET(7) NOT NULL,
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_item_processado_resumo UNIQUE(entrega_id,processado_hora_utc,classe_item,resultado),
 CONSTRAINT ck_item_processado_resumo_qtd CHECK(quantidade>0),
 CONSTRAINT ck_item_processado_resumo_classe CHECK(classe_item IN('PESSOA','REGISTRO')),
 CONSTRAINT ck_item_processado_resumo_resultado CHECK(resultado IN('INCLUIDO','VERSIONADO','RETRANSMITIDO','EXCLUIDO','REABERTO')));
GO
CREATE OR ALTER PROCEDURE ingestao.sp_consolidar_expurgar_item_processado
 @cutoff DATETIMEOFFSET(7), @max_rows INT = 100000
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @max_rows < 1 THROW 51230, 'max_rows deve ser positivo.', 1;
 CREATE TABLE #alvo(item_processado_id BIGINT PRIMARY KEY,entrega_id UNIQUEIDENTIFIER NOT NULL,processado_hora_utc DATETIME2(0) NOT NULL,classe_item NVARCHAR(20) NOT NULL,resultado NVARCHAR(30) NOT NULL,processado_em DATETIMEOFFSET(7) NOT NULL);
 BEGIN TRAN;
 INSERT #alvo
 SELECT TOP(@max_rows) ip.item_processado_id,l.entrega_id,
        DATEADD(HOUR,DATEDIFF(HOUR,CONVERT(datetime2(0),'20000101'),CONVERT(datetime2(0),SWITCHOFFSET(ip.processado_em,'+00:00'))),CONVERT(datetime2(0),'20000101')),
        ip.classe_item,ip.resultado,ip.processado_em
 FROM ingestao.item_processado ip WITH(UPDLOCK,READPAST,ROWLOCK)
 JOIN ingestao.lote l ON l.lote_id=ip.lote_id
 WHERE ip.processado_em<@cutoff AND ip.consolidado_em IS NULL ORDER BY ip.item_processado_id;
 MERGE ingestao.item_processado_resumo AS t
 USING(SELECT entrega_id,processado_hora_utc,classe_item,resultado,COUNT_BIG(*) quantidade,MIN(processado_em) primeiro_processado_em,MAX(processado_em) ultimo_processado_em FROM #alvo GROUP BY entrega_id,processado_hora_utc,classe_item,resultado) x
 ON t.entrega_id=x.entrega_id AND t.processado_hora_utc=x.processado_hora_utc AND t.classe_item=x.classe_item AND t.resultado=x.resultado
 WHEN MATCHED THEN UPDATE SET quantidade=t.quantidade+x.quantidade,primeiro_processado_em=CASE WHEN x.primeiro_processado_em<t.primeiro_processado_em THEN x.primeiro_processado_em ELSE t.primeiro_processado_em END,ultimo_processado_em=CASE WHEN x.ultimo_processado_em>t.ultimo_processado_em THEN x.ultimo_processado_em ELSE t.ultimo_processado_em END,atualizado_em=SYSDATETIMEOFFSET()
 WHEN NOT MATCHED THEN INSERT(entrega_id,processado_hora_utc,classe_item,resultado,quantidade,primeiro_processado_em,ultimo_processado_em) VALUES(x.entrega_id,x.processado_hora_utc,x.classe_item,x.resultado,x.quantidade,x.primeiro_processado_em,x.ultimo_processado_em);
 UPDATE ip SET consolidado_em=SYSDATETIMEOFFSET() FROM ingestao.item_processado ip JOIN #alvo a ON a.item_processado_id=ip.item_processado_id;
 COMMIT;
 DELETE TOP(@max_rows) ip FROM ingestao.item_processado ip WITH(ROWLOCK,READPAST)
 WHERE ip.processado_em<@cutoff AND ip.consolidado_em IS NOT NULL
   AND (ip.resultado<>'RETRANSMITIDO' OR EXISTS(SELECT 1 FROM ingestao.item_processado newer WHERE newer.resultado='RETRANSMITIDO' AND newer.item_processado_id<>ip.item_processado_id AND newer.processado_em>ip.processado_em AND newer.classe_item=ip.classe_item AND ((ip.classe_item='PESSOA' AND newer.pessoa_origem_id=ip.pessoa_origem_id) OR (ip.classe_item='REGISTRO' AND newer.registro_origem_id=ip.registro_origem_id))));
END;
GO
-- resultado='VERSIONADO' é categoria de ingestão e não distingue ALTERACAO de RETIFICACAO; essa semântica permanece
-- em silver.registro_observacao.operacao e serving.v_bi_registro_versoes.

-- Bronze preserva exatamente o ZIP recebido; reprocessamento não depende de reconstruir o payload.

-- Completude é derivada do controle interno de lotes, nunca informada pelo Gestor.
-- Uma Entrega é completa somente quando 1..N existem sem lacunas, todos declaram o mesmo N e todos foram PROCESSADOS.
CREATE OR ALTER VIEW ingestao.v_entrega_completude AS
SELECT l.entrega_id,
       CAST(CASE
          WHEN COUNT_BIG(*) > 0
           AND MIN(l.lote_total)=MAX(l.lote_total)
           AND COUNT_BIG(*)=MAX(l.lote_total)
           AND MIN(l.lote_seq)=1
           AND MAX(l.lote_seq)=MAX(l.lote_total)
           AND SUM(CASE WHEN l.status='PROCESSADO' THEN 1 ELSE 0 END)=COUNT_BIG(*)
          THEN 1 ELSE 0 END AS BIT) entrega_completa
FROM ingestao.lote l
GROUP BY l.entrega_id;
GO

IF OBJECT_ID('bronze.entrega_arquivo','U') IS NULL CREATE TABLE bronze.entrega_arquivo(
 entrega_id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES ingestao.entrega(entrega_id),
 nome_arquivo NVARCHAR(260) NOT NULL,
 content_type NVARCHAR(100) NOT NULL,
 objeto_chave NVARCHAR(1024) NOT NULL,
 payload_sha256 CHAR(64) NOT NULL,
 tamanho_bytes BIGINT NOT NULL,
 recebido_em DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT ck_bronze_objeto_sha CHECK(LEN(payload_sha256)=64 AND payload_sha256 NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT ck_bronze_objeto_tamanho CHECK(tamanho_bytes>0),
 CONSTRAINT ck_bronze_entrega_recebido_utc CHECK(DATEPART(TZOFFSET,recebido_em)=0));
GO

-- v3.37: Bronze externa content-addressed. Migração reiniciável e fail-closed.
-- Se payload_zip legado ainda existir e houver linhas, externalize os bytes e preencha metadados antes de prosseguir.
IF COL_LENGTH('bronze.entrega_arquivo','payload_zip') IS NOT NULL
   AND EXISTS(SELECT 1 FROM bronze.entrega_arquivo)
    THROW 51210, 'v3.37 requer externalizar a Bronze legada antes de remover payload_zip. Não descarte BLOBs existentes.', 1;
GO
IF COL_LENGTH('bronze.entrega_arquivo','objeto_chave') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD objeto_chave NVARCHAR(1024) NULL;
IF COL_LENGTH('bronze.entrega_arquivo','payload_sha256') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD payload_sha256 CHAR(64) NULL;
IF COL_LENGTH('bronze.entrega_arquivo','tamanho_bytes') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD tamanho_bytes BIGINT NULL;
GO
IF COL_LENGTH('bronze.entrega_arquivo','payload_zip') IS NOT NULL
    ALTER TABLE bronze.entrega_arquivo DROP COLUMN payload_zip;
GO
IF EXISTS(SELECT 1 FROM bronze.entrega_arquivo WHERE objeto_chave IS NULL OR payload_sha256 IS NULL OR tamanho_bytes IS NULL)
    THROW 51211, 'Metadados da Bronze externa estão incompletos; não é seguro tornar as colunas NOT NULL.', 1;
GO
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='objeto_chave' AND is_nullable=1)
    ALTER TABLE bronze.entrega_arquivo ALTER COLUMN objeto_chave NVARCHAR(1024) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='payload_sha256' AND is_nullable=1)
    ALTER TABLE bronze.entrega_arquivo ALTER COLUMN payload_sha256 CHAR(64) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='tamanho_bytes' AND is_nullable=1)
    ALTER TABLE bronze.entrega_arquivo ALTER COLUMN tamanho_bytes BIGINT NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='ck_bronze_objeto_sha')
    ALTER TABLE bronze.entrega_arquivo WITH CHECK ADD CONSTRAINT ck_bronze_objeto_sha CHECK(LEN(payload_sha256)=64 AND payload_sha256 NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='ck_bronze_objeto_tamanho')
    ALTER TABLE bronze.entrega_arquivo WITH CHECK ADD CONSTRAINT ck_bronze_objeto_tamanho CHECK(tamanho_bytes>0);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='IX_bronze_entrega_arquivo_objeto_chave')
    CREATE INDEX IX_bronze_entrega_arquivo_objeto_chave ON bronze.entrega_arquivo(objeto_chave) INCLUDE(entrega_id,payload_sha256,tamanho_bytes);
GO

IF OBJECT_ID('controle.bronze_manutencao_estado','U') IS NULL CREATE TABLE controle.bronze_manutencao_estado(
 estado_id TINYINT NOT NULL PRIMARY KEY CHECK(estado_id=1),bucket_cursor INT NOT NULL DEFAULT(0) CHECK(bucket_cursor BETWEEN 0 AND 65535),after_object_key NVARCHAR(1024) NULL,atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()));
IF NOT EXISTS(SELECT 1 FROM controle.bronze_manutencao_estado WHERE estado_id=1) INSERT controle.bronze_manutencao_estado(estado_id,bucket_cursor) VALUES(1,0);
GO
IF OBJECT_ID('controle.bronze_manutencao_ciclo','U') IS NULL CREATE TABLE controle.bronze_manutencao_ciclo(
 ciclo_id BIGINT IDENTITY PRIMARY KEY,iniciado_em DATETIMEOFFSET(7) NOT NULL,finalizado_em DATETIMEOFFSET(7) NOT NULL,bucket_inicial INT NOT NULL,after_inicial NVARCHAR(1024) NULL,bucket_proximo INT NOT NULL,after_proximo NVARCHAR(1024) NULL,objetos_examinados INT NOT NULL,orfaos_removidos INT NOT NULL,temporarios_removidos INT NOT NULL,locks_nao_adquiridos INT NOT NULL,falhas_storage INT NOT NULL,orfao_mais_antigo_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_bronze_manutencao_metricas CHECK(objetos_examinados>=0 AND orfaos_removidos>=0 AND temporarios_removidos>=0 AND locks_nao_adquiridos>=0 AND falhas_storage>=0));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.bronze_manutencao_ciclo') AND name='IX_bronze_manutencao_ciclo_finalizado') CREATE INDEX IX_bronze_manutencao_ciclo_finalizado ON controle.bronze_manutencao_ciclo(finalizado_em DESC);
GO

-- Regra operacional v3.38: nenhum expurgo físico pode apagar um objeto enquanto houver referência em
-- bronze.entrega_arquivo. A API segura app lock Shared por SHA desde antes do Put até o commit SQL;
-- Jornada.Bronze.Maintenance.Worker usa o mesmo recurso em Exclusive, confirma COUNT_BIG(*)=0 e só então apaga.
-- Objetos canônicos e temporários só são candidatos a GC após OrphanGraceHours, evitando a janela legítima
-- objeto-primeiro -> linha SQL e restos de escrita interrompida.

-- v3.29: recebido_em é o instante oficial atribuído pela API no recebimento do ZIP.
-- Não há relógio/default concorrente no banco para esse campo. Migra instalações anteriores removendo defaults legados.
DECLARE @df_recebido_entrega SYSNAME;
SELECT @df_recebido_entrega=dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
WHERE dc.parent_object_id=OBJECT_ID('ingestao.entrega') AND c.name='recebido_em';
IF @df_recebido_entrega IS NOT NULL
BEGIN
    DECLARE @sql_drop_recebido_entrega NVARCHAR(MAX)=N'ALTER TABLE ingestao.entrega DROP CONSTRAINT ' + QUOTENAME(@df_recebido_entrega);
    EXEC sys.sp_executesql @sql_drop_recebido_entrega;
END;

DECLARE @df_recebido_bronze SYSNAME;
SELECT @df_recebido_bronze=dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
WHERE dc.parent_object_id=OBJECT_ID('bronze.entrega_arquivo') AND c.name='recebido_em';
IF @df_recebido_bronze IS NOT NULL
BEGIN
    DECLARE @sql_drop_recebido_bronze NVARCHAR(MAX)=N'ALTER TABLE bronze.entrega_arquivo DROP CONSTRAINT ' + QUOTENAME(@df_recebido_bronze);
    EXEC sys.sp_executesql @sql_drop_recebido_bronze;
END;

-- Migração segura de instalações anteriores: preserva o instante e normaliza apenas a representação do offset para UTC.
UPDATE ingestao.entrega SET recebido_em=SWITCHOFFSET(recebido_em,'+00:00') WHERE DATEPART(TZOFFSET,recebido_em)<>0;
UPDATE bronze.entrega_arquivo SET recebido_em=SWITCHOFFSET(recebido_em,'+00:00') WHERE DATEPART(TZOFFSET,recebido_em)<>0;

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('ingestao.entrega') AND name='ck_entrega_recebido_utc')
    ALTER TABLE ingestao.entrega WITH CHECK ADD CONSTRAINT ck_entrega_recebido_utc CHECK(DATEPART(TZOFFSET,recebido_em)=0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='ck_bronze_entrega_recebido_utc')
    ALTER TABLE bronze.entrega_arquivo WITH CHECK ADD CONSTRAINT ck_bronze_entrega_recebido_utc CHECK(DATEPART(TZOFFSET,recebido_em)=0);
GO

-- Identidade estável da Pessoa no sistema de origem. A chave externa é obrigatória e não é o UUID municipal.
-- Modo de carga inicial compartilhado. Quando ativo, o Processor continua drenando a fila; Runner e GENERATE_DRAFT recusam execução.
IF OBJECT_ID('controle.modo_carga_inicial','U') IS NULL CREATE TABLE controle.modo_carga_inicial(estado_id TINYINT NOT NULL PRIMARY KEY CHECK(estado_id=1),ativo BIT NOT NULL DEFAULT(0),ativado_em DATETIMEOFFSET(7) NULL,desativado_em DATETIMEOFFSET(7) NULL,alterado_por NVARCHAR(200) NULL,observacao NVARCHAR(1000) NULL,atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()));
IF NOT EXISTS(SELECT 1 FROM controle.modo_carga_inicial WHERE estado_id=1) INSERT controle.modo_carga_inicial(estado_id,ativo) VALUES(1,0);
GO

IF OBJECT_ID('silver.pessoa_origem','U') IS NULL CREATE TABLE silver.pessoa_origem(
 pessoa_origem_id BIGINT IDENTITY PRIMARY KEY,
 sistema_origem_id BIGINT NOT NULL REFERENCES ref.sistema_origem(sistema_origem_id),
 codigo_pessoa_origem NVARCHAR(255) NOT NULL,
 ultima_recepcao_em DATETIMEOFFSET(7) NULL,
 ultima_referencia_recebida DATETIMEOFFSET(7) NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_pessoa_origem UNIQUE(sistema_origem_id,codigo_pessoa_origem),
 CONSTRAINT uq_pessoa_origem_id_codigo UNIQUE(pessoa_origem_id,codigo_pessoa_origem));
GO
IF COL_LENGTH('silver.pessoa_origem','ultima_recepcao_em') IS NULL ALTER TABLE silver.pessoa_origem ADD ultima_recepcao_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('silver.pessoa_origem','ultima_referencia_recebida') IS NULL ALTER TABLE silver.pessoa_origem ADD ultima_referencia_recebida DATETIMEOFFSET(7) NULL;
GO

-- Núcleo de identidade/cadastro observado. A Jornada atribui versao_interna automaticamente:
-- mesmo código + mesmo conteudo_hash = retransmissão idempotente; conteúdo diferente = nova versão interna.
IF OBJECT_ID('silver.pessoa_observacao','U') IS NULL CREATE TABLE silver.pessoa_observacao(
 pessoa_observacao_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_origem_id BIGINT NOT NULL,
 lote_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.lote(lote_id),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 codigo_pessoa_origem NVARCHAR(255) NOT NULL,
 versao_interna INT NOT NULL,
 conteudo_hash CHAR(64) NOT NULL,
 cpf CHAR(11) NULL,
 cpf_ausente_motivo NVARCHAR(30) NULL,
 nome_completo NVARCHAR(500) NOT NULL,
 nome_cmp NVARCHAR(500) NOT NULL,
 data_nascimento DATE NOT NULL,
 nome_mae NVARCHAR(500) NOT NULL,
 nome_mae_cmp NVARCHAR(500) NOT NULL,
 source_as_of DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT fk_pessoa_observacao_origem FOREIGN KEY(pessoa_origem_id,codigo_pessoa_origem) REFERENCES silver.pessoa_origem(pessoa_origem_id,codigo_pessoa_origem),
 CONSTRAINT uq_pessoa_observacao_versao UNIQUE(pessoa_origem_id,versao_interna),
 CONSTRAINT ck_pessoa_observacao_versao CHECK(versao_interna>=1),
 CONSTRAINT ck_pessoa_observacao_hash CHECK(LEN(conteudo_hash)=64 AND conteudo_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT ck_pessoa_cpf_motivo CHECK((cpf IS NULL AND cpf_ausente_motivo IN('SEM_CPF','EM_REGULARIZACAO','NAO_INFORMADO_ORIGEM')) OR (cpf IS NOT NULL AND cpf_ausente_motivo IS NULL)));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.pessoa_observacao') AND name='IX_pessoa_observacao_origem_corrente')
 CREATE INDEX IX_pessoa_observacao_origem_corrente ON silver.pessoa_observacao(pessoa_origem_id,versao_interna DESC) INCLUDE(conteudo_hash,pessoa_observacao_id,source_as_of);
GO

-- Conferência documental do núcleo é evidência por campo, recebida junto da observação de Pessoa.
-- A Jornada não armazena imagem de documento e não possui módulo próprio de regularização cadastral.
IF OBJECT_ID('silver.pessoa_campo_verificacao_observacao','U') IS NULL CREATE TABLE silver.pessoa_campo_verificacao_observacao(
 pessoa_campo_verificacao_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 campo_codigo NVARCHAR(40) NOT NULL,
 evidencia_tipo NVARCHAR(80) NOT NULL,
 referencia_evidencia NVARCHAR(255) NULL,
 verificado_em DATETIMEOFFSET(7) NOT NULL,
 source_transaction_id NVARCHAR(255) NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_pessoa_campo_verificacao_codigo CHECK(campo_codigo IN('CPF','NOME_COMPLETO','DATA_NASCIMENTO','NOME_MAE')),
 CONSTRAINT uq_pessoa_campo_verificacao UNIQUE(pessoa_observacao_id,campo_codigo));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.pessoa_campo_verificacao_observacao') AND name='IX_pessoa_campo_verificacao_campo_data')
 CREATE INDEX IX_pessoa_campo_verificacao_campo_data ON silver.pessoa_campo_verificacao_observacao(campo_codigo,verificado_em DESC) INCLUDE(pessoa_observacao_id,gestor_id);
GO

IF OBJECT_ID('silver.pessoa_atributo_observacao','U') IS NULL CREATE TABLE silver.pessoa_atributo_observacao(
 pessoa_atributo_observacao_id BIGINT IDENTITY PRIMARY KEY,
 source_record_id NVARCHAR(255) NULL,
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 fonte_gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 atributo_codigo NVARCHAR(80) NOT NULL REFERENCES ref.atributo_transversal(atributo_codigo),
 valor NVARCHAR(2000) NOT NULL,
 status_evidencia NVARCHAR(20) NOT NULL,
 evidencia_tipo NVARCHAR(80) NULL,
 referencia_evidencia DATETIMEOFFSET(7) NULL,
 verificado_em DATETIMEOFFSET(7) NULL,
 atualizado_em_origem DATETIMEOFFSET(7) NULL,
 ingested_at DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_pat_status CHECK(status_evidencia IN('DECLARADO','COMPROVADO')),
 CONSTRAINT ck_pat_comprovado CHECK(status_evidencia<>'COMPROVADO' OR verificado_em IS NOT NULL));
GO

-- v3.32: ENDERECO_RESIDENCIAL é atributo cadastral de endereço de residência; não é a superfície analítica territorial.
-- A camada territorial usa exclusivamente o snapshot de REFERENCIA_TERRITORIAL selecionado. Quando não houver
-- referência explícita, ENDERECO_RESIDENCIAL pode originar uma referência DOMICILIAR de fallback, preservando
-- a linhagem para o atributo cadastral que lhe deu origem. Não existe dual-write geográfico em tabela residencial.
-- A plataforma jamais escolhe unidade de atendimento, UBS, CRAS, Centro POP, abrigo ou outro equipamento
-- como referência por simples disponibilidade do endereço. ACOLHIMENTO_INSTITUCIONAL e
-- REFERENCIA_TERRITORIAL_DECLARADA só existem quando a fonte os declara explicitamente.
-- ENRIQUECIMENTO_PRODAM permanece apenas como domínio histórico de upgrade; o código v3.39 não produz novas linhas com essa origem.
IF OBJECT_ID('silver.referencia_territorial_observacao','U') IS NULL CREATE TABLE silver.referencia_territorial_observacao(
 referencia_territorial_observacao_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_atributo_observacao_id BIGINT NOT NULL UNIQUE REFERENCES silver.pessoa_atributo_observacao(pessoa_atributo_observacao_id),
 natureza_referencia NVARCHAR(50) NOT NULL,
 fonte_semantica NVARCHAR(30) NOT NULL,
 subprefeitura_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id),
 distrito_id BIGINT NULL REFERENCES ref.distrito(distrito_id),
 situacao_geografia NVARCHAR(40) NOT NULL,
 origem_geografia NVARCHAR(30) NULL,
 referencia_malha NVARCHAR(120) NULL,
 resolvido_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_referencia_territorial_natureza CHECK(natureza_referencia IN('DOMICILIAR','ACOLHIMENTO_INSTITUCIONAL','REFERENCIA_TERRITORIAL_DECLARADA')),
 CONSTRAINT ck_referencia_territorial_fonte CHECK(fonte_semantica IN('REFERENCIA_TERRITORIAL','ENDERECO_RESIDENCIAL')),
 CONSTRAINT ck_referencia_territorial_domiciliar CHECK(fonte_semantica<>'ENDERECO_RESIDENCIAL' OR natureza_referencia='DOMICILIAR'),
 CONSTRAINT ck_referencia_territorial_geo_par CHECK((subprefeitura_id IS NULL AND distrito_id IS NULL) OR (subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL)),
 CONSTRAINT ck_referencia_territorial_geo_situacao CHECK(situacao_geografia IN('RESOLVIDA','FORA_MUNICIPIO','SEM_ENDERECO_APTO','NAO_RESOLVIDA_ORIGEM')),
 CONSTRAINT ck_referencia_territorial_geo_coerencia CHECK((situacao_geografia='RESOLVIDA' AND subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL AND referencia_malha IS NOT NULL AND origem_geografia IN('ORIGEM','ENRIQUECIMENTO_PRODAM')) OR (situacao_geografia<>'RESOLVIDA' AND subprefeitura_id IS NULL AND distrito_id IS NULL AND origem_geografia IS NULL AND referencia_malha IS NULL AND resolvido_em IS NULL)),
 CONSTRAINT ck_referencia_territorial_geo_origem CHECK(origem_geografia IS NULL OR origem_geografia IN('ORIGEM','ENRIQUECIMENTO_PRODAM')));
GO
IF COL_LENGTH('silver.referencia_territorial_observacao','situacao_geografia') IS NULL ALTER TABLE silver.referencia_territorial_observacao ADD situacao_geografia NVARCHAR(40) NULL;
UPDATE silver.referencia_territorial_observacao SET referencia_malha=CASE WHEN subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL THEN COALESCE(referencia_malha,'LEGADO_PRE_V3.38') ELSE NULL END,situacao_geografia=CASE WHEN subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL THEN 'RESOLVIDA' ELSE 'NAO_RESOLVIDA_ORIGEM' END,origem_geografia=CASE WHEN subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL THEN COALESCE(origem_geografia,'ORIGEM') ELSE NULL END,resolvido_em=CASE WHEN subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL THEN COALESCE(resolvido_em,SYSDATETIMEOFFSET()) ELSE NULL END WHERE situacao_geografia IS NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('silver.referencia_territorial_observacao') AND name='situacao_geografia' AND is_nullable=1) ALTER TABLE silver.referencia_territorial_observacao ALTER COLUMN situacao_geografia NVARCHAR(40) NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('silver.referencia_territorial_observacao') AND name='ck_referencia_territorial_geo_situacao') ALTER TABLE silver.referencia_territorial_observacao WITH CHECK ADD CONSTRAINT ck_referencia_territorial_geo_situacao CHECK(situacao_geografia IN('RESOLVIDA','FORA_MUNICIPIO','SEM_ENDERECO_APTO','NAO_RESOLVIDA_ORIGEM'));
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('silver.referencia_territorial_observacao') AND name='ck_referencia_territorial_geo_coerencia') ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_geo_coerencia;
ALTER TABLE silver.referencia_territorial_observacao WITH CHECK ADD CONSTRAINT ck_referencia_territorial_geo_coerencia CHECK((situacao_geografia='RESOLVIDA' AND subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL AND referencia_malha IS NOT NULL AND origem_geografia IN('ORIGEM','ENRIQUECIMENTO_PRODAM')) OR (situacao_geografia<>'RESOLVIDA' AND subprefeitura_id IS NULL AND distrito_id IS NULL AND origem_geografia IS NULL AND referencia_malha IS NULL AND resolvido_em IS NULL));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.referencia_territorial_observacao') AND name='IX_referencia_territorial_geo')
 CREATE INDEX IX_referencia_territorial_geo ON silver.referencia_territorial_observacao(subprefeitura_id,distrito_id,natureza_referencia) INCLUDE(pessoa_atributo_observacao_id,fonte_semantica);
GO

-- Upgrade v3.31 -> v3.32: migra qualquer snapshot residencial legado para a referência territorial única e
-- remove a persistência/view redundantes. O INSERT é idempotente e preserva a observação de endereço de origem.
IF OBJECT_ID('silver.endereco_residencial_geografia_observacao','U') IS NOT NULL
BEGIN
 INSERT silver.referencia_territorial_observacao(pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
 SELECT eg.pessoa_atributo_observacao_id,'DOMICILIAR','ENDERECO_RESIDENCIAL',eg.subprefeitura_id,eg.distrito_id,CASE WHEN eg.subprefeitura_id IS NOT NULL AND eg.distrito_id IS NOT NULL THEN 'RESOLVIDA' ELSE 'NAO_RESOLVIDA_ORIGEM' END,CASE WHEN eg.subprefeitura_id IS NOT NULL AND eg.distrito_id IS NOT NULL THEN COALESCE(eg.origem,'ORIGEM') END,CASE WHEN eg.subprefeitura_id IS NOT NULL AND eg.distrito_id IS NOT NULL THEN COALESCE(eg.referencia_malha,'LEGADO_PRE_V3.38') END,CASE WHEN eg.subprefeitura_id IS NOT NULL AND eg.distrito_id IS NOT NULL THEN eg.resolvido_em END
 FROM silver.endereco_residencial_geografia_observacao eg
 WHERE NOT EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao rt WHERE rt.pessoa_atributo_observacao_id=eg.pessoa_atributo_observacao_id);

 IF OBJECT_ID('silver.v_pessoa_geografia_residencial','V') IS NOT NULL DROP VIEW silver.v_pessoa_geografia_residencial;
 DROP TABLE silver.endereco_residencial_geografia_observacao;
END;
GO

CREATE OR ALTER VIEW silver.v_pessoa_referencia_territorial AS
SELECT po.pessoa_observacao_id,x.referencia_territorial_observacao_id,x.pessoa_atributo_observacao_id,
       x.natureza_referencia,x.fonte_semantica,x.subprefeitura_id,x.distrito_id,x.situacao_geografia,x.origem_geografia,x.referencia_malha,x.resolvido_em
FROM silver.pessoa_observacao po
OUTER APPLY(
 SELECT TOP(1) rt.referencia_territorial_observacao_id,pa.pessoa_atributo_observacao_id,rt.natureza_referencia,rt.fonte_semantica,
               rt.subprefeitura_id,rt.distrito_id,rt.situacao_geografia,rt.origem_geografia,rt.referencia_malha,rt.resolvido_em
 FROM silver.pessoa_atributo_observacao pa
 JOIN silver.referencia_territorial_observacao rt ON rt.pessoa_atributo_observacao_id=pa.pessoa_atributo_observacao_id
 WHERE pa.pessoa_observacao_id=po.pessoa_observacao_id
 ORDER BY CASE WHEN rt.fonte_semantica='REFERENCIA_TERRITORIAL' THEN 0 ELSE 1 END,
          CASE WHEN pa.status_evidencia='COMPROVADO' THEN 0 ELSE 1 END,
          COALESCE(pa.referencia_evidencia,pa.verificado_em,pa.atualizado_em_origem,pa.ingested_at) DESC,
          pa.pessoa_atributo_observacao_id DESC
) x;
GO
-- Regra de consumo: camadas de visualização/BI territorial devem usar esta referência selecionada
-- (Distrito/Subprefeitura e natureza). ENDERECO_RESIDENCIAL não é fonte direta de mapa/segmentação; quando
-- servir de fallback, isso ocorre somente por meio do snapshot DOMICILIAR acima, preservando a semântica.

-- Identidade estável do fato no sistema de origem. O namespace é (sistema_origem, codigo_registro_origem).
-- O primeiro uso fixa Natureza e Tipo; a mesma chave não pode ser reciclada para outro fato/tipo.
IF OBJECT_ID('silver.registro_origem','U') IS NULL CREATE TABLE silver.registro_origem(
 registro_origem_id BIGINT IDENTITY PRIMARY KEY,
 sistema_origem_id BIGINT NOT NULL REFERENCES ref.sistema_origem(sistema_origem_id),
 codigo_registro_origem NVARCHAR(255) NOT NULL,
 natureza NVARCHAR(30) NOT NULL,
 tipo_registro_id BIGINT NOT NULL REFERENCES ref.tipo_registro(tipo_registro_id),
 ultima_recepcao_em DATETIMEOFFSET(7) NULL,
 ultima_referencia_recebida DATETIMEOFFSET(7) NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_registro_origem UNIQUE(sistema_origem_id,codigo_registro_origem),
 -- Redundância intencional: sustenta a FK composta de registro_observacao e garante a integridade do código desnormalizado.
 CONSTRAINT uq_registro_origem_id_codigo UNIQUE(registro_origem_id,codigo_registro_origem),
 CONSTRAINT ck_registro_origem_natureza CHECK(natureza IN('BENEFICIO','SERVICO')));
GO
IF COL_LENGTH('silver.registro_origem','ultima_recepcao_em') IS NULL ALTER TABLE silver.registro_origem ADD ultima_recepcao_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('silver.registro_origem','ultima_referencia_recebida') IS NULL ALTER TABLE silver.registro_origem ADD ultima_referencia_recebida DATETIMEOFFSET(7) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('ingestao.item_processado') AND name='fk_item_processado_pessoa_origem')
 ALTER TABLE ingestao.item_processado WITH CHECK ADD CONSTRAINT fk_item_processado_pessoa_origem FOREIGN KEY(pessoa_origem_id) REFERENCES silver.pessoa_origem(pessoa_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('ingestao.item_processado') AND name='fk_item_processado_registro_origem')
 ALTER TABLE ingestao.item_processado WITH CHECK ADD CONSTRAINT fk_item_processado_registro_origem FOREIGN KEY(registro_origem_id) REFERENCES silver.registro_origem(registro_origem_id);
GO

-- Silver factual versionada pela própria Jornada. A fonte envia chave estável + operacao, nunca número de versão.
-- Mesmo hash da última observação = retransmissão; hash diferente = nova versao_interna.
IF OBJECT_ID('silver.registro_observacao','U') IS NULL CREATE TABLE silver.registro_observacao(
 registro_observacao_id BIGINT IDENTITY PRIMARY KEY,
 registro_origem_id BIGINT NOT NULL,
 codigo_registro_origem NVARCHAR(255) NOT NULL,
 versao_interna INT NOT NULL,
 operacao NVARCHAR(20) NOT NULL,
 conteudo_hash CHAR(64) NOT NULL,
 lote_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.lote(lote_id),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 natureza NVARCHAR(30) NOT NULL,
 tipo_registro_id BIGINT NOT NULL,
 tipo_registro_versao_id BIGINT NOT NULL,
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 data_inicio_concessao DATE NULL,
 data_fim_concessao DATE NULL,
 data_evento_concessao DATE NULL,
 data_hora_servico DATETIMEOFFSET(7) NULL,
 unidade_servico NVARCHAR(200) NULL,
 situacao NVARCHAR(80) NULL,
 valor_concedido DECIMAL(18,2) NULL,
 quantidade DECIMAL(18,4) NULL,
 unidade NVARCHAR(50) NULL,
 source_as_of DATETIMEOFFSET(7) NOT NULL,
 registrado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT fk_registro_observacao_origem FOREIGN KEY(registro_origem_id,codigo_registro_origem) REFERENCES silver.registro_origem(registro_origem_id,codigo_registro_origem),
 CONSTRAINT uq_registro_observacao_versao UNIQUE(registro_origem_id,versao_interna),
 CONSTRAINT fk_registro_tipo_contexto FOREIGN KEY(tipo_registro_id,gestor_id,natureza) REFERENCES ref.tipo_registro(tipo_registro_id,gestor_id,natureza),
 CONSTRAINT fk_registro_tipo_versao FOREIGN KEY(tipo_registro_versao_id,tipo_registro_id) REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id,tipo_registro_id),
 CONSTRAINT ck_registro_observacao_operacao CHECK(operacao IN('INCLUSAO','ALTERACAO','RETIFICACAO','EXCLUSAO')),
 CONSTRAINT ck_registro_observacao_versao CHECK(versao_interna>=1),
 CONSTRAINT ck_registro_observacao_hash CHECK(LEN(conteudo_hash)=64 AND conteudo_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2),
 CONSTRAINT ck_registro_observacao_natureza CHECK(
   (natureza='BENEFICIO' AND data_hora_servico IS NULL AND unidade_servico IS NULL) OR
   (natureza='SERVICO' AND valor_concedido IS NULL AND quantidade IS NULL AND unidade IS NULL AND data_inicio_concessao IS NULL AND data_fim_concessao IS NULL AND data_evento_concessao IS NULL)));
GO
-- v3.48: renomeação física preserva os dados de instalações v3.47 sem manter aliases públicos.
IF COL_LENGTH('silver.registro_observacao','data_inicio') IS NOT NULL AND COL_LENGTH('silver.registro_observacao','data_inicio_concessao') IS NULL EXEC sp_rename 'silver.registro_observacao.data_inicio','data_inicio_concessao','COLUMN';
IF COL_LENGTH('silver.registro_observacao','data_fim') IS NOT NULL AND COL_LENGTH('silver.registro_observacao','data_fim_concessao') IS NULL EXEC sp_rename 'silver.registro_observacao.data_fim','data_fim_concessao','COLUMN';
IF COL_LENGTH('silver.registro_observacao','data_evento') IS NOT NULL AND COL_LENGTH('silver.registro_observacao','data_evento_concessao') IS NULL EXEC sp_rename 'silver.registro_observacao.data_evento','data_evento_concessao','COLUMN';
IF COL_LENGTH('silver.registro_observacao','valor_monetario') IS NOT NULL AND COL_LENGTH('silver.registro_observacao','valor_concedido') IS NULL EXEC sp_rename 'silver.registro_observacao.valor_monetario','valor_concedido','COLUMN';
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.registro_observacao') AND name='IX_registro_observacao_origem_corrente')
 CREATE INDEX IX_registro_observacao_origem_corrente ON silver.registro_observacao(registro_origem_id,versao_interna DESC) INCLUDE(conteudo_hash,operacao,registro_observacao_id,source_as_of);
GO

IF OBJECT_ID('identidade.modelo_linkage','U') IS NULL CREATE TABLE identidade.modelo_linkage(
 modelo_id UNIQUEIDENTIFIER PRIMARY KEY, versao INT NOT NULL UNIQUE, status NVARCHAR(20) NOT NULL, algoritmo_versao NVARCHAR(80) NOT NULL,
 normalizacao_versao NVARCHAR(80) NOT NULL, deduplicacao_metodo NVARCHAR(120) NOT NULL, base_referencia NVARCHAR(200) NOT NULL,
 snapshot_referencia NVARCHAR(300) NULL, registros_lidos BIGINT NULL, pessoas_unicas BIGINT NULL, gerado_em DATETIMEOFFSET(7) NOT NULL, ativado_em DATETIMEOFFSET(7) NULL,
 snapshot_capturado_em DATETIME2(7) NULL, amostra_metodo NVARCHAR(80) NULL, amostra_pool_tamanho INT NULL, amostra_m_tamanho INT NULL, amostra_u_tamanho INT NULL,
 falha_resumo NVARCHAR(500) NULL,
 CONSTRAINT ck_modelo_status CHECK(status IN('GERANDO','RASCUNHO','VALIDADO','ATIVO','INATIVO','FALHOU')));
GO
IF COL_LENGTH('identidade.modelo_linkage','normalizacao_versao') IS NULL
BEGIN
 ALTER TABLE identidade.modelo_linkage ADD normalizacao_versao NVARCHAR(80) NULL;
 UPDATE identidade.modelo_linkage SET normalizacao_versao='LEGACY_UNVERSIONED' WHERE normalizacao_versao IS NULL;
 ALTER TABLE identidade.modelo_linkage ALTER COLUMN normalizacao_versao NVARCHAR(80) NOT NULL;
END;
GO
IF COL_LENGTH('identidade.modelo_linkage','deduplicacao_metodo') IS NULL
BEGIN
 ALTER TABLE identidade.modelo_linkage ADD deduplicacao_metodo NVARCHAR(120) NULL;
 UPDATE identidade.modelo_linkage SET deduplicacao_metodo='LEGACY_UNSPECIFIED' WHERE deduplicacao_metodo IS NULL;
 ALTER TABLE identidade.modelo_linkage ALTER COLUMN deduplicacao_metodo NVARCHAR(120) NOT NULL;
END;
GO
IF COL_LENGTH('identidade.modelo_linkage','snapshot_capturado_em') IS NULL ALTER TABLE identidade.modelo_linkage ADD snapshot_capturado_em DATETIME2(7) NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','amostra_metodo') IS NULL ALTER TABLE identidade.modelo_linkage ADD amostra_metodo NVARCHAR(80) NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','amostra_pool_tamanho') IS NULL ALTER TABLE identidade.modelo_linkage ADD amostra_pool_tamanho INT NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','amostra_m_tamanho') IS NULL ALTER TABLE identidade.modelo_linkage ADD amostra_m_tamanho INT NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','amostra_u_tamanho') IS NULL ALTER TABLE identidade.modelo_linkage ADD amostra_u_tamanho INT NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','falha_resumo') IS NULL ALTER TABLE identidade.modelo_linkage ADD falha_resumo NVARCHAR(500) NULL;
GO
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('identidade.modelo_linkage') AND name='snapshot_referencia' AND max_length<600)
 ALTER TABLE identidade.modelo_linkage ALTER COLUMN snapshot_referencia NVARCHAR(300) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='ck_modelo_status')
 ALTER TABLE identidade.modelo_linkage DROP CONSTRAINT ck_modelo_status;
GO
ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT ck_modelo_status CHECK(status IN('GERANDO','RASCUNHO','VALIDADO','ATIVO','INATIVO','FALHOU'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.modelo_linkage') AND name='UX_identidade_modelo_linkage_ativo')
 CREATE UNIQUE INDEX UX_identidade_modelo_linkage_ativo ON identidade.modelo_linkage(status) WHERE status='ATIVO';
GO
-- Reservada para eventual term-frequency adjustment futuro. O baseline V1 não duplica
-- frequências de alta cardinalidade por modelo; isso evita dezenas de GB por versão em escala nacional.
IF OBJECT_ID('identidade.frequencia_linkage','U') IS NULL CREATE TABLE identidade.frequencia_linkage(
 frequencia_id BIGINT IDENTITY PRIMARY KEY, modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id), atributo NVARCHAR(80) NOT NULL,
 valor_normalizado NVARCHAR(500) NOT NULL, ocorrencias BIGINT NOT NULL, populacao_referencia BIGINT NOT NULL, frequencia DECIMAL(20,12) NOT NULL, UNIQUE(modelo_id,atributo,valor_normalizado));
GO
IF OBJECT_ID('identidade.estatistica_linkage','U') IS NULL CREATE TABLE identidade.estatistica_linkage(
 estatistica_id BIGINT IDENTITY PRIMARY KEY,
 modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
 nome NVARCHAR(100) NOT NULL,
 valor DECIMAL(30,6) NOT NULL,
 metodo NVARCHAR(80) NOT NULL,
 CONSTRAINT uq_estatistica_linkage UNIQUE(modelo_id,nome));
GO
IF OBJECT_ID('identidade.parametro_linkage','U') IS NULL CREATE TABLE identidade.parametro_linkage(
 parametro_id BIGINT IDENTITY PRIMARY KEY, modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id), nome NVARCHAR(100) NOT NULL, valor DECIMAL(30,12) NOT NULL, UNIQUE(modelo_id,nome));
GO
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('identidade.parametro_linkage') AND name='valor' AND precision<30)
 ALTER TABLE identidade.parametro_linkage ALTER COLUMN valor DECIMAL(30,12) NOT NULL;
GO
IF OBJECT_ID('identidade.pessoa','U') IS NULL CREATE TABLE identidade.pessoa(
 pessoa_uuid UNIQUEIDENTIFIER PRIMARY KEY, status NVARCHAR(30) NOT NULL, criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_identidade_pessoa_status CHECK(status IN('ATIVO','FUNDIDO','SEPARADO','INATIVO')));
GO
IF OBJECT_ID('identidade.identity_map','U') IS NULL CREATE TABLE identidade.identity_map(
 identity_map_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 tipo NVARCHAR(30) NOT NULL, identificador NVARCHAR(255) NOT NULL,
 vigencia_inicio DATETIMEOFFSET(7) NOT NULL, vigencia_fim DATETIMEOFFSET(7) NULL,
 gestor_origem_id BIGINT NULL REFERENCES ref.gestor(gestor_id), source_record_id NVARCHAR(255) NULL,
 metodo_resolucao NVARCHAR(40) NOT NULL, score DECIMAL(9,8) NULL,
 modelo_id UNIQUEIDENTIFIER NULL REFERENCES identidade.modelo_linkage(modelo_id),
 estado NVARCHAR(20) NOT NULL DEFAULT('ATIVO'), estado_motivo NVARCHAR(120) NULL, estado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_identity_map_tipo CHECK(tipo IN('CPF','NIS','CNS','LEGADO')),
 CONSTRAINT ck_identity_map_vig CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio),
 CONSTRAINT ck_identity_map_estado CHECK(estado IN('ATIVO','EM_CONFLITO','ENCERRADO')),
 CONSTRAINT ck_identity_map_estado_vig CHECK((vigencia_fim IS NULL AND estado IN('ATIVO','EM_CONFLITO')) OR (vigencia_fim IS NOT NULL AND estado='ENCERRADO')),
 CONSTRAINT ck_identity_map_metodo CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA')),
 CONSTRAINT ck_identity_map_modelo CHECK((metodo_resolucao IN('CPF_DETERMINISTICO','CORRECAO_GOVERNADA') AND score IS NULL AND modelo_id IS NULL) OR
                                        (metodo_resolucao='LINKAGE_PROBABILISTICO' AND modelo_id IS NOT NULL)));
GO
-- v3.43: o conflito de CPF sobe da observação para o identificador. Um CPF EM_CONFLITO deixa de resolver para UUID.
IF COL_LENGTH('identidade.identity_map','estado') IS NULL ALTER TABLE identidade.identity_map ADD estado NVARCHAR(20) NOT NULL CONSTRAINT DF_identity_map_estado DEFAULT('ATIVO') WITH VALUES;
IF COL_LENGTH('identidade.identity_map','estado_motivo') IS NULL ALTER TABLE identidade.identity_map ADD estado_motivo NVARCHAR(120) NULL;
IF COL_LENGTH('identidade.identity_map','estado_em') IS NULL ALTER TABLE identidade.identity_map ADD estado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_identity_map_estado_em DEFAULT(SYSDATETIMEOFFSET()) WITH VALUES;
UPDATE identidade.identity_map SET estado='ENCERRADO',estado_motivo=COALESCE(estado_motivo,'VIGENCIA_ENCERRADA'),estado_em=COALESCE(vigencia_fim,estado_em) WHERE vigencia_fim IS NOT NULL AND estado<>'ENCERRADO';
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.identity_map') AND name='ck_identity_map_estado') ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_estado;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.identity_map') AND name='ck_identity_map_estado_vig') ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_estado_vig;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.identity_map') AND name='ck_identity_map_metodo') ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_metodo;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.identity_map') AND name='ck_identity_map_modelo') ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_modelo;
ALTER TABLE identidade.identity_map WITH CHECK ADD CONSTRAINT ck_identity_map_estado CHECK(estado IN('ATIVO','EM_CONFLITO','ENCERRADO'));
ALTER TABLE identidade.identity_map WITH CHECK ADD CONSTRAINT ck_identity_map_estado_vig CHECK((vigencia_fim IS NULL AND estado IN('ATIVO','EM_CONFLITO')) OR (vigencia_fim IS NOT NULL AND estado='ENCERRADO'));
ALTER TABLE identidade.identity_map WITH CHECK ADD CONSTRAINT ck_identity_map_metodo CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA'));
ALTER TABLE identidade.identity_map WITH CHECK ADD CONSTRAINT ck_identity_map_modelo CHECK((metodo_resolucao IN('CPF_DETERMINISTICO','CORRECAO_GOVERNADA') AND score IS NULL AND modelo_id IS NULL) OR (metodo_resolucao='LINKAGE_PROBABILISTICO' AND modelo_id IS NOT NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.identity_map') AND name='UX_identidade_identity_map_cpf_ativo')
 CREATE UNIQUE INDEX UX_identidade_identity_map_cpf_ativo ON identidade.identity_map(tipo,identificador) WHERE tipo='CPF' AND vigencia_fim IS NULL;
GO
IF OBJECT_ID('identidade.identity_map_estado_evento','U') IS NULL CREATE TABLE identidade.identity_map_estado_evento(
 identity_map_estado_evento_id BIGINT IDENTITY PRIMARY KEY,
 identity_map_id BIGINT NOT NULL REFERENCES identidade.identity_map(identity_map_id),
 estado_anterior NVARCHAR(20) NULL, estado_novo NVARCHAR(20) NOT NULL, motivo NVARCHAR(120) NOT NULL,
 pessoa_observacao_id BIGINT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 referencia_operacao UNIQUEIDENTIFIER NULL, ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_identity_map_estado_evento_estado CHECK(estado_novo IN('ATIVO','EM_CONFLITO','ENCERRADO')));
GO
CREATE OR ALTER TRIGGER identidade.tr_identity_map_estado_evento_append_only ON identidade.identity_map_estado_evento INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51071,'Histórico de estado da IDENTITY_MAP é append-only.',1;
END;
GO

IF OBJECT_ID('identidade.vinculo_fonte','U') IS NULL CREATE TABLE identidade.vinculo_fonte(
 vinculo_id BIGINT IDENTITY PRIMARY KEY, pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id), pessoa_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 metodo_resolucao NVARCHAR(40) NOT NULL, score DECIMAL(9,8) NULL, status NVARCHAR(30) NOT NULL, modelo_id UNIQUEIDENTIFIER NULL REFERENCES identidade.modelo_linkage(modelo_id), ativo BIT NOT NULL DEFAULT(1),
 linkage_run_id UNIQUEIDENTIFIER NULL, resolvido_em DATETIMEOFFSET(7) NULL, motivo NVARCHAR(120) NULL,
 CONSTRAINT ck_vinculo_status CHECK(status IN('RESOLVIDO','NAO_RESOLVIDO','CONFLITO')),
 CONSTRAINT ck_vinculo_status_uuid CHECK((status='RESOLVIDO' AND pessoa_uuid IS NOT NULL) OR (status IN('NAO_RESOLVIDO','CONFLITO') AND pessoa_uuid IS NULL)),
 CONSTRAINT ck_vinculo_metodo CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA')),
 CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao='CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao='LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao='CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL)));
GO
IF COL_LENGTH('identidade.vinculo_fonte','linkage_run_id') IS NULL
 ALTER TABLE identidade.vinculo_fonte ADD linkage_run_id UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH('identidade.vinculo_fonte','resolvido_em') IS NULL
 ALTER TABLE identidade.vinculo_fonte ADD resolvido_em DATETIMEOFFSET(7) NULL;
GO
-- v3.42: motivo explícito da decisão determinística permite distinguir CPF inválido
-- de CPF válido usado possivelmente por terceiro, sem expor a observação à Gold.
IF COL_LENGTH('identidade.vinculo_fonte','motivo') IS NULL
 ALTER TABLE identidade.vinculo_fonte ADD motivo NVARCHAR(120) NULL;
GO
-- v3.42: vínculo corrente só pode carregar UUID quando a decisão estiver RESOLVIDA.
-- CONFLITO/NAO_RESOLVIDO permanecem sem UUID e, portanto, não podem promover fatos à Gold.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_status_uuid')
 ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_status_uuid;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_status_uuid
 CHECK((status='RESOLVIDO' AND pessoa_uuid IS NOT NULL) OR (status IN('NAO_RESOLVIDO','CONFLITO') AND pessoa_uuid IS NULL));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_metodo')
 ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_metodo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_metodo
 CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_modelo')
 ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_modelo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao='CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao='LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao='CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL));
GO

-- v3.43: correção governada de identidade. Não há CRUD genérico de UUID: toda reassociação exige ato, justificativa e trilha institucional.
IF OBJECT_ID('identidade.correcao_identidade','U') IS NULL CREATE TABLE identidade.correcao_identidade(
 correcao_id UNIQUEIDENTIFIER PRIMARY KEY,
 gestor_responsavel_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 identity_map_origem_id BIGINT NOT NULL REFERENCES identidade.identity_map(identity_map_id),
 pessoa_uuid_titular UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 tipo NVARCHAR(30) NOT NULL DEFAULT('REASSOCIACAO_GOVERNADA'),
 status NVARCHAR(20) NOT NULL DEFAULT('APLICANDO'),
 ato_referencia NVARCHAR(300) NOT NULL,
 justificativa NVARCHAR(2000) NOT NULL,
 correlation_id UNIQUEIDENTIFIER NULL,
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 aplicado_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_correcao_identidade_tipo CHECK(tipo IN('REASSOCIACAO_GOVERNADA')),
 CONSTRAINT ck_correcao_identidade_status CHECK(status IN('APLICANDO','APLICADA','FALHOU')),
 CONSTRAINT ck_correcao_identidade_aplicado CHECK((status='APLICADA' AND aplicado_em IS NOT NULL) OR status<>'APLICADA'));
GO
IF OBJECT_ID('identidade.correcao_identidade_item','U') IS NULL CREATE TABLE identidade.correcao_identidade_item(
 correcao_item_id BIGINT IDENTITY PRIMARY KEY,
 correcao_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.correcao_identidade(correcao_id),
 grupo_codigo NVARCHAR(80) NOT NULL,
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 pessoa_uuid_anterior UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 status_anterior NVARCHAR(30) NULL,
 pessoa_uuid_destino UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_correcao_identidade_item UNIQUE(correcao_id,pessoa_observacao_id));
GO
CREATE OR ALTER TRIGGER identidade.tr_correcao_identidade_item_append_only ON identidade.correcao_identidade_item INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51074,'Itens de correção de identidade são append-only.',1;
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_correcao_identidade_aplicada_immutavel ON identidade.correcao_identidade AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.correcao_id=d.correcao_id WHERE d.status='APLICADA')
   THROW 51075,'Correção de identidade APLICADA é imutável.',1;
END;
GO

-- A procedure é a única operação de escrita administrativa de identidade da Fase 1.
-- O request explicita o agrupamento das observações; a Jornada não tenta inferir qual núcleo é o titular correto.
CREATE OR ALTER PROCEDURE identidade.sp_aplicar_correcao_identidade
 @gestor_codigo NVARCHAR(30), @cpf CHAR(11), @grupo_titular NVARCHAR(80), @grupos_json NVARCHAR(MAX),
 @ato_referencia NVARCHAR(300), @justificativa NVARCHAR(2000), @correlation_id UNIQUEIDENTIFIER=NULL,
 @correcao_id UNIQUEIDENTIFIER OUTPUT, @pessoa_uuid_titular UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISJSON(@grupos_json)<>1 THROW 51076,'Grupos da correção devem ser JSON válido.',1;
 IF NULLIF(LTRIM(RTRIM(@ato_referencia)),'') IS NULL OR NULLIF(LTRIM(RTRIM(@justificativa)),'') IS NULL THROW 51077,'Ato e justificativa são obrigatórios.',1;
 DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor_codigo AND ativo=1);
 IF @gestor IS NULL THROW 51078,'Gestor responsável inexistente/inativo.',1;
 DECLARE @map BIGINT,@uuid_ant UNIQUEIDENTIFIER,@estado NVARCHAR(20);
 SELECT TOP(1) @map=identity_map_id,@uuid_ant=pessoa_uuid,@estado=estado FROM identidade.identity_map WITH(UPDLOCK,HOLDLOCK)
  WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;
 IF @map IS NULL OR @estado<>'EM_CONFLITO' THROW 51079,'CPF não possui conflito global ativo para correção.',1;

 DECLARE @g TABLE(grupo NVARCHAR(80) PRIMARY KEY,dest UNIQUEIDENTIFIER NULL);
 INSERT @g(grupo,dest)
 SELECT grupoCodigo,TRY_CONVERT(uniqueidentifier,pessoaUuidDestino)
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaUuidDestino NVARCHAR(40) '$.pessoaUuidDestino');
 IF NOT EXISTS(SELECT 1 FROM @g WHERE grupo=@grupo_titular) THROW 51080,'Grupo titular do CPF não foi informado.',1;
 IF EXISTS(SELECT 1 FROM @g WHERE NULLIF(LTRIM(RTRIM(grupo)),'') IS NULL) THROW 51081,'Código de grupo inválido.',1;
 IF EXISTS(SELECT 1 FROM @g g WHERE g.dest IS NOT NULL AND NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=g.dest)) THROW 51082,'UUID de destino informado não existe.',1;
 DECLARE @novos TABLE(grupo NVARCHAR(80) PRIMARY KEY,dest UNIQUEIDENTIFIER NOT NULL);
 INSERT @novos SELECT grupo,COALESCE(dest,NEWID()) FROM @g;
 INSERT identidade.pessoa(pessoa_uuid,status) SELECT n.dest,'ATIVO' FROM @novos n WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=n.dest);
 UPDATE p SET status='ATIVO' FROM identidade.pessoa p JOIN @novos n ON n.dest=p.pessoa_uuid;

 DECLARE @o TABLE(obs BIGINT PRIMARY KEY,grupo NVARCHAR(80) NOT NULL,dest UNIQUEIDENTIFIER NOT NULL,uuid_ant UNIQUEIDENTIFIER NULL,status_ant NVARCHAR(30) NULL,lote_id UNIQUEIDENTIFIER NOT NULL);
 INSERT @o(obs,grupo,dest,uuid_ant,status_ant,lote_id)
 SELECT j.pessoaObservacaoId,j.grupoCodigo,n.dest,vc.pessoa_uuid,vc.status,po.lote_id
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g
 CROSS APPLY OPENJSON(g.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') j0
 CROSS APPLY (SELECT g.grupoCodigo,j0.pessoaObservacaoId) j
 JOIN @novos n ON n.grupo=j.grupoCodigo
 JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=j.pessoaObservacaoId AND po.cpf=@cpf
 LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id;
 IF EXISTS(SELECT pessoaObservacaoId FROM OPENJSON(@grupos_json) WITH(pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g CROSS APPLY OPENJSON(g.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') x GROUP BY pessoaObservacaoId HAVING COUNT(*)>1)
   THROW 51083,'Uma observação não pode pertencer a mais de um grupo.',1;
 DECLARE @solicitadas INT=(SELECT COUNT(*) FROM OPENJSON(@grupos_json) WITH(pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g CROSS APPLY OPENJSON(g.pessoaObservacaoIds));
 IF @solicitadas<>(SELECT COUNT(*) FROM @o) THROW 51084,'Há observação inexistente ou cujo CPF não corresponde ao conflito.',1;
 -- Para reativar o CPF é obrigatório classificar todas as observações que carregam esse CPF; não pode sobrar evidência ambígua fora do ato.
 IF EXISTS(SELECT 1 FROM silver.pessoa_observacao po WHERE po.cpf=@cpf AND NOT EXISTS(SELECT 1 FROM @o x WHERE x.obs=po.pessoa_observacao_id))
   THROW 51085,'A correção deve classificar todas as observações existentes com o CPF em conflito.',1;
 SELECT @pessoa_uuid_titular=dest FROM @novos WHERE grupo=@grupo_titular;
 SET @correcao_id=NEWID();
 INSERT identidade.correcao_identidade(correcao_id,gestor_responsavel_id,identity_map_origem_id,pessoa_uuid_titular,ato_referencia,justificativa,correlation_id)
 VALUES(@correcao_id,@gestor,@map,@pessoa_uuid_titular,@ato_referencia,@justificativa,@correlation_id);
 INSERT identidade.correcao_identidade_item(correcao_id,grupo_codigo,pessoa_observacao_id,pessoa_uuid_anterior,status_anterior,pessoa_uuid_destino)
 SELECT @correcao_id,grupo,obs,uuid_ant,status_ant,dest FROM @o;

 UPDATE vf SET ativo=0 FROM identidade.vinculo_fonte vf JOIN @o o ON o.obs=vf.pessoa_observacao_id WHERE vf.ativo=1;
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 SELECT obs,dest,'CORRECAO_GOVERNADA',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CORRECAO_IDENTIDADE_APLICADA' FROM @o;

 DECLARE @agora DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
 UPDATE identidade.identity_map SET vigencia_fim=@agora,estado='ENCERRADO',estado_motivo='CORRECAO_GOVERNADA',estado_em=@agora WHERE identity_map_id=@map;
 INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo,referencia_operacao)
 VALUES(@map,'EM_CONFLITO','ENCERRADO','CORRECAO_GOVERNADA',@correcao_id);
 INSERT identidade.identity_map(pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo,estado_em)
 VALUES(@pessoa_uuid_titular,'CPF',@cpf,@agora,@gestor,NULL,'CORRECAO_GOVERNADA','ATIVO','CORRECAO_GOVERNADA',@agora);
 DECLARE @novo_map BIGINT=SCOPE_IDENTITY();
 INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo,referencia_operacao)
 VALUES(@novo_map,NULL,'ATIVO','CORRECAO_GOVERNADA',@correcao_id);

 -- Atualiza somente a atribuição canônica dos fatos já materializados; a ocorrência factual não depende de reprocessamento do lote.
 UPDATE b SET pessoa_uuid=o.dest,atualizado_em=@agora FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE sp SET pessoa_uuid=o.dest,atualizado_em=@agora FROM gold.servico_prestado sp JOIN silver.registro_observacao ro ON ro.registro_observacao_id=sp.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE ri SET pessoa_uuid=o.dest,atualizado_em=@agora FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;

 -- Reconstrói a associação das linhas históricas de atributos; as versões correntes são fechadas e recriadas segundo a precedência vigente.
 DECLARE @afetados TABLE(uuid UNIQUEIDENTIFIER PRIMARY KEY);
 INSERT @afetados SELECT DISTINCT dest FROM @o UNION SELECT DISTINCT uuid_ant FROM @o WHERE uuid_ant IS NOT NULL;
 UPDATE ga SET vigencia_fim=COALESCE(ga.vigencia_fim,@agora),atualizado_em=@agora FROM gold.pessoa_atributo ga JOIN @afetados a ON a.uuid=ga.pessoa_uuid WHERE ga.vigencia_fim IS NULL;
 UPDATE ga SET pessoa_uuid=o.dest,atualizado_em=@agora FROM gold.pessoa_atributo ga JOIN silver.pessoa_atributo_observacao pao ON pao.pessoa_atributo_observacao_id=ga.pessoa_atributo_observacao_id JOIN @o o ON o.obs=pao.pessoa_observacao_id;
 ;WITH candidatos AS(
   SELECT vc.pessoa_uuid,pao.*,COALESCE(pao.referencia_evidencia,pao.verificado_em) precedencia,
          ROW_NUMBER() OVER(PARTITION BY vc.pessoa_uuid,pao.atributo_codigo ORDER BY COALESCE(pao.referencia_evidencia,pao.verificado_em) DESC,pao.verificado_em DESC,pao.pessoa_atributo_observacao_id DESC) rn
   FROM silver.pessoa_atributo_observacao pao JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=pao.pessoa_observacao_id AND vc.status='RESOLVIDO'
   JOIN @afetados a ON a.uuid=vc.pessoa_uuid WHERE pao.status_evidencia='COMPROVADO'
 )
 INSERT gold.pessoa_atributo(pessoa_uuid,atributo_codigo,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,vigencia_fim,atualizado_em)
 SELECT pessoa_uuid,atributo_codigo,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia,@agora,NULL,@agora FROM candidatos WHERE rn=1;

 -- Derived possibilities are invalid after reassociation and must be recomputed.
 DELETE ap FROM qualidade.avaliacao_possibilidade ap JOIN @afetados a ON a.uuid=ap.pessoa_uuid;
 -- Reabre apenas lotes que contêm observações corrigidas. Retransmissão é idempotente; v3.43 materializa fato Silver ausente na Gold após correção.
 UPDATE l SET status='PENDENTE',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,proxima_tentativa_em=NULL,atualizado_em=@agora
 FROM ingestao.lote l WHERE EXISTS(SELECT 1 FROM @o o WHERE o.lote_id=l.lote_id);
 UPDATE e SET status='RECEBIDA',ultima_atualizacao=@agora FROM ingestao.entrega e WHERE EXISTS(SELECT 1 FROM ingestao.lote l JOIN @o o ON o.lote_id=l.lote_id WHERE l.entrega_id=e.entrega_id);

 -- UUID sem vínculo corrente nem identificador ativo deixa de ser apresentado como ativo; histórico nunca é apagado.
 UPDATE p SET status='SEPARADO' FROM identidade.pessoa p JOIN @afetados a ON a.uuid=p.pessoa_uuid
 WHERE NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente v WHERE v.pessoa_uuid=p.pessoa_uuid AND v.status='RESOLVIDO')
   AND NOT EXISTS(SELECT 1 FROM identidade.identity_map m WHERE m.pessoa_uuid=p.pessoa_uuid AND m.vigencia_fim IS NULL AND m.estado='ATIVO');
 UPDATE identidade.correcao_identidade SET status='APLICADA',aplicado_em=@agora WHERE correcao_id=@correcao_id;
END;
GO

IF OBJECT_ID('identidade.linkage_run','U') IS NULL CREATE TABLE identidade.linkage_run(
 linkage_run_id UNIQUEIDENTIFIER PRIMARY KEY,
 modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
 modelo_versao INT NOT NULL,
 tipo_run NVARCHAR(30) NOT NULL,
 status NVARCHAR(30) NOT NULL,
 pessoa_observacao_id_filtro BIGINT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 gestor_codigo_filtro NVARCHAR(30) NULL,
 desde_filtro DATETIMEOFFSET(7) NULL,
 limite_solicitado BIGINT NULL,
 escopo_json NVARCHAR(MAX) NULL,
 batch_size INT NULL,
 max_parallelism INT NULL,
 pessoa_observacao_id_high_watermark BIGINT NULL,
 registros_elegiveis BIGINT NOT NULL DEFAULT(0),
 ultimo_observacao_id BIGINT NULL,
 avaliados BIGINT NOT NULL DEFAULT(0), resolvidos BIGINT NOT NULL DEFAULT(0), nao_resolvidos BIGINT NOT NULL DEFAULT(0), conflitos BIGINT NOT NULL DEFAULT(0), sem_candidato_no_bloco BIGINT NOT NULL DEFAULT(0),
 solicitado_por NVARCHAR(120) NULL, motivo NVARCHAR(400) NULL, correlation_id UNIQUEIDENTIFIER NULL,
 iniciado_em DATETIMEOFFSET(7) NOT NULL, finalizado_em DATETIMEOFFSET(7) NULL, publicado_em DATETIMEOFFSET(7) NULL, erro_resumo NVARCHAR(200) NULL,
 CONSTRAINT ck_linkage_run_tipo CHECK(tipo_run IN('ON_DEMAND','INCREMENTAL','REPLAY','FULL','MODEL_VALIDATION')),
 CONSTRAINT ck_linkage_run_status CHECK(status IN('PREPARANDO','EXECUTANDO','PUBLICADO','CONCLUIDO_SEM_PUBLICACAO','FALHOU','CANCELADO')),
 CONSTRAINT ck_linkage_run_contagens CHECK(registros_elegiveis>=0 AND avaliados>=0 AND resolvidos>=0 AND nao_resolvidos>=0 AND conflitos>=0 AND sem_candidato_no_bloco>=0 AND sem_candidato_no_bloco<=nao_resolvidos AND avaliados=resolvidos+nao_resolvidos+conflitos),
 CONSTRAINT ck_linkage_run_datas CHECK(finalizado_em IS NULL OR finalizado_em>=iniciado_em),
 CONSTRAINT ck_linkage_run_publicacao CHECK((status='PUBLICADO' AND publicado_em IS NOT NULL AND finalizado_em IS NOT NULL) OR status<>'PUBLICADO'),
 CONSTRAINT ck_linkage_run_escopo_json CHECK(escopo_json IS NULL OR ISJSON(escopo_json)=1));
GO
-- Migração aditiva de bases v3.5 ou anteriores.
IF COL_LENGTH('identidade.linkage_run','desde_filtro') IS NULL ALTER TABLE identidade.linkage_run ADD desde_filtro DATETIMEOFFSET(7) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','escopo_json') IS NULL ALTER TABLE identidade.linkage_run ADD escopo_json NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','batch_size') IS NULL ALTER TABLE identidade.linkage_run ADD batch_size INT NULL;
GO
IF COL_LENGTH('identidade.linkage_run','max_parallelism') IS NULL ALTER TABLE identidade.linkage_run ADD max_parallelism INT NULL;
GO
IF COL_LENGTH('identidade.linkage_run','pessoa_observacao_id_high_watermark') IS NULL AND COL_LENGTH('identidade.linkage_run','pessoa_observacao_id_max_snapshot') IS NOT NULL
 EXEC sys.sp_rename 'identidade.linkage_run.pessoa_observacao_id_max_snapshot','pessoa_observacao_id_high_watermark','COLUMN';
GO
IF COL_LENGTH('identidade.linkage_run','pessoa_observacao_id_high_watermark') IS NULL ALTER TABLE identidade.linkage_run ADD pessoa_observacao_id_high_watermark BIGINT NULL;
GO
IF COL_LENGTH('identidade.linkage_run','registros_elegiveis') IS NULL ALTER TABLE identidade.linkage_run ADD registros_elegiveis BIGINT NOT NULL CONSTRAINT df_linkage_run_elegiveis DEFAULT(0);
GO
IF COL_LENGTH('identidade.linkage_run','ultimo_observacao_id') IS NULL ALTER TABLE identidade.linkage_run ADD ultimo_observacao_id BIGINT NULL;
GO
IF COL_LENGTH('identidade.linkage_run','solicitado_por') IS NULL ALTER TABLE identidade.linkage_run ADD solicitado_por NVARCHAR(120) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','motivo') IS NULL ALTER TABLE identidade.linkage_run ADD motivo NVARCHAR(400) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','correlation_id') IS NULL ALTER TABLE identidade.linkage_run ADD correlation_id UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH('identidade.linkage_run','publicado_em') IS NULL ALTER TABLE identidade.linkage_run ADD publicado_em DATETIMEOFFSET(7) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','sem_candidato_no_bloco') IS NULL ALTER TABLE identidade.linkage_run ADD sem_candidato_no_bloco BIGINT NOT NULL CONSTRAINT df_linkage_run_sem_bloco DEFAULT(0);
GO
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('identidade.linkage_run') AND name='limite_solicitado' AND system_type_id=56)
 ALTER TABLE identidade.linkage_run ALTER COLUMN limite_solicitado BIGINT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_tipo')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_tipo;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_status')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_status;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_contagens')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_contagens;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_datas')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_datas;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_publicacao')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_publicacao;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.linkage_run') AND name='ck_linkage_run_escopo_json')
 ALTER TABLE identidade.linkage_run DROP CONSTRAINT ck_linkage_run_escopo_json;
GO
ALTER TABLE identidade.linkage_run ALTER COLUMN status NVARCHAR(30) NOT NULL;
GO
-- Em v3.5 CONCLUIDO significava execução operacional já aplicada. Na migração ela é tratada como PUBLICADO.
UPDATE identidade.linkage_run
SET status='PUBLICADO', publicado_em=COALESCE(publicado_em,finalizado_em), registros_elegiveis=CASE WHEN registros_elegiveis=0 THEN avaliados ELSE registros_elegiveis END
WHERE status='CONCLUIDO';
GO
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_tipo CHECK(tipo_run IN('ON_DEMAND','INCREMENTAL','REPLAY','FULL','MODEL_VALIDATION'));
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_status CHECK(status IN('PREPARANDO','EXECUTANDO','PUBLICADO','CONCLUIDO_SEM_PUBLICACAO','FALHOU','CANCELADO'));
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_contagens CHECK(registros_elegiveis>=0 AND avaliados>=0 AND resolvidos>=0 AND nao_resolvidos>=0 AND conflitos>=0 AND sem_candidato_no_bloco>=0 AND sem_candidato_no_bloco<=nao_resolvidos AND avaliados=resolvidos+nao_resolvidos+conflitos);
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_datas CHECK(finalizado_em IS NULL OR finalizado_em>=iniciado_em);
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_publicacao CHECK((status='PUBLICADO' AND publicado_em IS NOT NULL AND finalizado_em IS NOT NULL) OR status<>'PUBLICADO');
ALTER TABLE identidade.linkage_run WITH CHECK ADD CONSTRAINT ck_linkage_run_escopo_json CHECK(escopo_json IS NULL OR ISJSON(escopo_json)=1);
GO
-- Universo congelado de cada execução probabilística. Na Fase 1 a lista é materializada enquanto
-- o Runner possui a janela exclusiva do corpus; linkage_run_item preserva o universo auditável do run.
IF OBJECT_ID('identidade.linkage_run_item','U') IS NULL CREATE TABLE identidade.linkage_run_item(
 linkage_run_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.linkage_run(linkage_run_id),
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 incluido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT pk_linkage_run_item PRIMARY KEY(linkage_run_id,pessoa_observacao_id));
GO
IF OBJECT_ID('identidade.linkage_resultado','U') IS NULL CREATE TABLE identidade.linkage_resultado(
 linkage_resultado_id BIGINT IDENTITY PRIMARY KEY,
 linkage_run_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.linkage_run(linkage_run_id),
 modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
 modelo_versao INT NOT NULL,
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 pessoa_uuid_resolvido UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 melhor_candidato_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 score_melhor DECIMAL(9,8) NOT NULL,
 segundo_candidato_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 score_segundo DECIMAL(9,8) NULL,
 margem DECIMAL(9,8) NULL,
 status NVARCHAR(30) NOT NULL, motivo NVARCHAR(120) NULL, calculado_em DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT uq_linkage_resultado_run_observacao UNIQUE(linkage_run_id,pessoa_observacao_id),
 CONSTRAINT ck_linkage_resultado_status CHECK(status IN('RESOLVIDO','NAO_RESOLVIDO','CONFLITO')),
 CONSTRAINT ck_linkage_resultado_scores CHECK(score_melhor>=0 AND score_melhor<=1 AND (score_segundo IS NULL OR (score_segundo>=0 AND score_segundo<=1)) AND (margem IS NULL OR (margem>=0 AND margem<=1))),
 CONSTRAINT ck_linkage_resultado_resolvido CHECK((status='RESOLVIDO' AND pessoa_uuid_resolvido IS NOT NULL) OR (status<>'RESOLVIDO' AND pessoa_uuid_resolvido IS NULL)));
GO
-- Publicação logicamente atômica: resultados de um run só entram no vínculo corrente
-- depois da transição única do cabeçalho para PUBLICADO. Não há transação de horas.
CREATE OR ALTER VIEW identidade.v_vinculo_corrente AS
WITH probabilistico_publicado AS (
    SELECT r.pessoa_observacao_id,r.pessoa_uuid_resolvido,r.score_melhor,r.status,r.motivo,r.modelo_id,r.linkage_run_id,r.calculado_em,
           ROW_NUMBER() OVER(PARTITION BY r.pessoa_observacao_id ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) rn
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
    WHERE lr.status='PUBLICADO'
),
prob_corrente AS (
    SELECT * FROM probabilistico_publicado WHERE rn=1
),
base_ativa AS (
    SELECT vf.* FROM identidade.vinculo_fonte vf WHERE vf.ativo=1
)
SELECT b.vinculo_id,b.pessoa_observacao_id,b.pessoa_uuid,b.metodo_resolucao,b.score,b.status,b.motivo,b.modelo_id,b.linkage_run_id,b.resolvido_em
FROM base_ativa b
WHERE b.metodo_resolucao='CPF_DETERMINISTICO'
   OR NOT EXISTS (SELECT 1 FROM prob_corrente p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id)
UNION ALL
SELECT CAST(NULL AS BIGINT) vinculo_id,p.pessoa_observacao_id,p.pessoa_uuid_resolvido,'LINKAGE_PROBABILISTICO' metodo_resolucao,
       p.score_melhor score,p.status,p.motivo,p.modelo_id,p.linkage_run_id,p.calculado_em resolvido_em
FROM prob_corrente p
WHERE NOT EXISTS (
    SELECT 1 FROM base_ativa b
    WHERE b.pessoa_observacao_id=p.pessoa_observacao_id
      AND b.metodo_resolucao='CPF_DETERMINISTICO');
GO
-- Compatibilidade de atualização a partir da estrutura v3.4: migra resumos históricos de execução.
IF OBJECT_ID('identidade.linkage_execucao','U') IS NOT NULL
BEGIN
 INSERT identidade.linkage_run(
     linkage_run_id,modelo_id,modelo_versao,tipo_run,status,pessoa_observacao_id_filtro,gestor_codigo_filtro,
     limite_solicitado,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,iniciado_em,finalizado_em,publicado_em)
 SELECT le.linkage_run_id,le.modelo_id,le.modelo_versao,'ON_DEMAND','PUBLICADO',le.pessoa_observacao_id_filtro,le.gestor_codigo_filtro,
        NULL,le.avaliados,le.avaliados,le.resolvidos,le.nao_resolvidos,le.conflitos,le.iniciado_em,le.finalizado_em,le.finalizado_em
 FROM identidade.linkage_execucao le
 WHERE NOT EXISTS (SELECT 1 FROM identidade.linkage_run lr WHERE lr.linkage_run_id=le.linkage_run_id);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='fk_vinculo_linkage_run')
 ALTER TABLE identidade.vinculo_fonte ADD CONSTRAINT fk_vinculo_linkage_run FOREIGN KEY(linkage_run_id) REFERENCES identidade.linkage_run(linkage_run_id);
GO


IF OBJECT_ID('qualidade.qc_registro_implementacao','U') IS NULL CREATE TABLE qualidade.qc_registro_implementacao(
 tipo_registro_versao_id BIGINT PRIMARY KEY REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id), implementacao_versao NVARCHAR(80) NOT NULL, status NVARCHAR(20) NOT NULL,
 CONSTRAINT ck_qc_reg_status CHECK(status IN('IMPLEMENTADO','NAO_IMPLEMENTADO')));
GO
IF OBJECT_ID('qualidade.qc_registro_resultado','U') IS NULL CREATE TABLE qualidade.qc_registro_resultado(
 qc_resultado_id BIGINT IDENTITY PRIMARY KEY, registro_observacao_id BIGINT NOT NULL REFERENCES silver.registro_observacao(registro_observacao_id), resultado NVARCHAR(30) NOT NULL,
 regra_codigo NVARCHAR(80) NULL, motivo NVARCHAR(500) NULL, executado_em DATETIMEOFFSET(7) NOT NULL, CONSTRAINT ck_qc_res CHECK(resultado IN('VALIDO','DIVERGENTE','NAO_VERIFICAVEL')));
GO
IF OBJECT_ID('qualidade.possibilidade_implementacao','U') IS NULL CREATE TABLE qualidade.possibilidade_implementacao(
 possibilidade_impl_id BIGINT IDENTITY PRIMARY KEY,
 natureza NVARCHAR(30) NOT NULL,
 tipo_registro_versao_id BIGINT NOT NULL REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id),
 implementacao_versao NVARCHAR(80) NOT NULL,
 status NVARCHAR(20) NOT NULL,
 CONSTRAINT ck_poss_impl_status CHECK(status IN('IMPLEMENTADO','NAO_IMPLEMENTADO')),
 UNIQUE(tipo_registro_versao_id));
GO
IF OBJECT_ID('qualidade.avaliacao_possibilidade','U') IS NULL CREATE TABLE qualidade.avaliacao_possibilidade(
 avaliacao_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 natureza NVARCHAR(30) NOT NULL,
 tipo_registro_versao_id BIGINT NOT NULL REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id),
 resultado NVARCHAR(30) NOT NULL,
 motivo NVARCHAR(500) NOT NULL,
 implementacao_versao NVARCHAR(80) NOT NULL,
 avaliado_em DATETIMEOFFSET(7) NOT NULL,
 validade_ate DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_av_poss_result CHECK(resultado IN('COMPATIVEL','NAO_COMPATIVEL','NAO_AVALIAVEL')));
GO

-- Fase 1 - decisão de arquitetura/LGPD: CPF permanece em claro apenas nas camadas operacionais restritas
-- necessárias à resolução determinística e à qualidade de identidade. Não é publicado em views de BI nem em auditoria HTTP.
-- Criptografia/tokenização de coluna fica fora do escopo da Fase 1; infraestrutura deve manter TLS e proteção de disco/backup.
IF OBJECT_ID('gold.pessoa','U') IS NULL CREATE TABLE gold.pessoa(
 pessoa_uuid UNIQUEIDENTIFIER PRIMARY KEY REFERENCES identidade.pessoa(pessoa_uuid), cpf CHAR(11) NULL, status_cpf NVARCHAR(30) NOT NULL,
 nome_completo NVARCHAR(500) NOT NULL, data_nascimento DATE NOT NULL, nome_mae NVARCHAR(500) NOT NULL,
 fontes_distintas INT NOT NULL, estado_concordancia NVARCHAR(40) NOT NULL, atualizado_em DATETIMEOFFSET(7) NOT NULL,
 CONSTRAINT ck_gold_pessoa_status_cpf CHECK((cpf IS NOT NULL AND status_cpf='PRESENTE') OR (cpf IS NULL AND status_cpf IN('SEM_CPF','EM_REGULARIZACAO'))),
 CONSTRAINT ck_gold_pessoa_fontes CHECK(fontes_distintas>=0),
 CONSTRAINT ck_gold_pessoa_concordancia CHECK(estado_concordancia IN('BASELINE_FONTE_UNICA','BASELINE_FALLBACK','CORROBORADO','DIVERGENTE','CONFLITO_EVIDENCIA')));
GO
GO
-- v3.40: o contador representa fontes distintas observadas; uma fonte única não é 'corroboração'.
IF COL_LENGTH('gold.pessoa','fontes_distintas') IS NULL AND COL_LENGTH('gold.pessoa','fontes_corrobora') IS NOT NULL
 EXEC sp_rename 'gold.pessoa.fontes_corrobora','fontes_distintas','COLUMN';
GO
-- Blocking probabilístico V1 consulta exatamente data_nascimento e ordena por pessoa_uuid.
-- Índice coberto evita scan completo de gold.pessoa a cada observação em escala municipal.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.pessoa') AND name='IX_gold_pessoa_linkage_nascimento')
 CREATE INDEX IX_gold_pessoa_linkage_nascimento ON gold.pessoa(data_nascimento,pessoa_uuid) INCLUDE(nome_completo,nome_mae);
GO

-- Histórico SCD2 dos atributos transversais. Novo atributo = nova linha em ref.atributo_transversal; zero DDL.
IF OBJECT_ID('gold.pessoa_atributo','U') IS NULL CREATE TABLE gold.pessoa_atributo(
 pessoa_atributo_id BIGINT IDENTITY PRIMARY KEY,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 atributo_codigo NVARCHAR(80) NOT NULL REFERENCES ref.atributo_transversal(atributo_codigo),
 valor NVARCHAR(2000) NOT NULL,
 fonte_gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 pessoa_atributo_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_atributo_observacao(pessoa_atributo_observacao_id),
 source_record_id NVARCHAR(255) NULL,
 evidencia_tipo NVARCHAR(80) NULL,
 referencia_evidencia DATETIMEOFFSET(7) NULL,
 verificado_em DATETIMEOFFSET(7) NOT NULL,
 precedencia_em DATETIMEOFFSET(7) NOT NULL,
 vigencia_inicio DATETIMEOFFSET(7) NOT NULL,
 vigencia_fim DATETIMEOFFSET(7) NULL,
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT ck_gold_pessoa_atributo_vig CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.pessoa_atributo') AND name='UX_gold_pessoa_atributo_corrente')
 CREATE UNIQUE INDEX UX_gold_pessoa_atributo_corrente ON gold.pessoa_atributo(pessoa_uuid,atributo_codigo) WHERE vigencia_fim IS NULL;
GO
CREATE OR ALTER VIEW gold.v_pessoa_atributo_corrente AS
SELECT pessoa_atributo_id,pessoa_uuid,atributo_codigo,valor,fonte_gestor_id,pessoa_atributo_observacao_id,
       source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em
FROM gold.pessoa_atributo WHERE vigencia_fim IS NULL;
GO


-- Gold especializada da Fase 1. A nomenclatura explicita fatos ocorridos: Benefício Concedido e Serviço Prestado.
-- status_analitico controla o que entra nas consultas/BI sem apagar a proveniência histórica.
IF OBJECT_ID('gold.beneficio_concedido','U') IS NULL CREATE TABLE gold.beneficio_concedido(
 beneficio_concedido_id BIGINT IDENTITY PRIMARY KEY,
 registro_observacao_id BIGINT NOT NULL UNIQUE REFERENCES silver.registro_observacao(registro_observacao_id),
 registro_origem_id BIGINT NOT NULL REFERENCES silver.registro_origem(registro_origem_id),
 codigo_registro_origem NVARCHAR(255) NOT NULL,
 versao_interna INT NOT NULL,
 operacao NVARCHAR(20) NOT NULL,
 status_analitico NVARCHAR(20) NOT NULL,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 tipo_registro_id BIGINT NOT NULL REFERENCES ref.tipo_registro(tipo_registro_id),
 tipo_registro_versao_id BIGINT NOT NULL REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id),
 entrega_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.entrega(entrega_id),
 data_inicio_concessao DATE NULL, data_fim_concessao DATE NULL, data_evento_concessao DATE NULL, situacao NVARCHAR(80) NULL,
 referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id),
 natureza_referencia_territorial NVARCHAR(50) NULL,
 subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id), distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id),
 valor_concedido DECIMAL(18,2) NULL, quantidade DECIMAL(18,4) NULL, unidade NVARCHAR(50) NULL,
 source_as_of DATETIMEOFFSET(7) NOT NULL, qc_resultado NVARCHAR(30) NULL, qc_especifico_implementado BIT NOT NULL,
 vigencia_versao_inicio DATETIMEOFFSET(7) NOT NULL, vigencia_versao_fim DATETIMEOFFSET(7) NULL,
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_beneficio_concedido_origem_versao UNIQUE(registro_origem_id,versao_interna),
 CONSTRAINT ck_beneficio_concedido_operacao CHECK(operacao IN('INCLUSAO','ALTERACAO','RETIFICACAO')),
 CONSTRAINT ck_beneficio_concedido_status CHECK(status_analitico IN('VIGENTE','HISTORICO','RETIFICADO','EXCLUIDO')),
 CONSTRAINT ck_beneficio_concedido_vigencia CHECK(vigencia_versao_fim IS NULL OR vigencia_versao_fim>=vigencia_versao_inicio));
GO
-- v3.48: BENEFICIO_CONCEDIDO usa nomes semânticos próprios; migração é in-place.
IF COL_LENGTH('gold.beneficio_concedido','data_inicio') IS NOT NULL AND COL_LENGTH('gold.beneficio_concedido','data_inicio_concessao') IS NULL EXEC sp_rename 'gold.beneficio_concedido.data_inicio','data_inicio_concessao','COLUMN';
IF COL_LENGTH('gold.beneficio_concedido','data_fim') IS NOT NULL AND COL_LENGTH('gold.beneficio_concedido','data_fim_concessao') IS NULL EXEC sp_rename 'gold.beneficio_concedido.data_fim','data_fim_concessao','COLUMN';
IF COL_LENGTH('gold.beneficio_concedido','data_evento') IS NOT NULL AND COL_LENGTH('gold.beneficio_concedido','data_evento_concessao') IS NULL EXEC sp_rename 'gold.beneficio_concedido.data_evento','data_evento_concessao','COLUMN';
IF COL_LENGTH('gold.beneficio_concedido','valor_monetario') IS NOT NULL AND COL_LENGTH('gold.beneficio_concedido','valor_concedido') IS NULL EXEC sp_rename 'gold.beneficio_concedido.valor_monetario','valor_concedido','COLUMN';
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.beneficio_concedido') AND name='UX_beneficio_concedido_corrente')
 CREATE UNIQUE INDEX UX_beneficio_concedido_corrente ON gold.beneficio_concedido(registro_origem_id) WHERE status_analitico='VIGENTE';
GO
IF OBJECT_ID('gold.servico_prestado','U') IS NULL CREATE TABLE gold.servico_prestado(
 servico_prestado_id BIGINT IDENTITY PRIMARY KEY,
 registro_observacao_id BIGINT NOT NULL UNIQUE REFERENCES silver.registro_observacao(registro_observacao_id),
 registro_origem_id BIGINT NOT NULL REFERENCES silver.registro_origem(registro_origem_id),
 codigo_registro_origem NVARCHAR(255) NOT NULL,
 versao_interna INT NOT NULL,
 operacao NVARCHAR(20) NOT NULL,
 status_analitico NVARCHAR(20) NOT NULL,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 tipo_registro_id BIGINT NOT NULL REFERENCES ref.tipo_registro(tipo_registro_id),
 tipo_registro_versao_id BIGINT NOT NULL REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id),
 entrega_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.entrega(entrega_id),
 data_hora_servico DATETIMEOFFSET(7) NOT NULL, unidade_servico NVARCHAR(200) NULL, situacao NVARCHAR(80) NULL,
 referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id),
 natureza_referencia_territorial NVARCHAR(50) NULL,
 subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id), distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id),
 source_as_of DATETIMEOFFSET(7) NOT NULL,
 vigencia_versao_inicio DATETIMEOFFSET(7) NOT NULL, vigencia_versao_fim DATETIMEOFFSET(7) NULL,
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_servico_prestado_origem_versao UNIQUE(registro_origem_id,versao_interna),
 CONSTRAINT ck_servico_prestado_operacao CHECK(operacao IN('INCLUSAO','ALTERACAO','RETIFICACAO')),
 CONSTRAINT ck_servico_prestado_status CHECK(status_analitico IN('VIGENTE','HISTORICO','RETIFICADO','EXCLUIDO')),
 CONSTRAINT ck_servico_prestado_vigencia CHECK(vigencia_versao_fim IS NULL OR vigencia_versao_fim>=vigencia_versao_inicio));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.servico_prestado') AND name='UX_servico_prestado_corrente')
 CREATE UNIQUE INDEX UX_servico_prestado_corrente ON gold.servico_prestado(registro_origem_id) WHERE status_analitico='VIGENTE';
GO

CREATE OR ALTER VIEW gold.v_registros_jornada AS
SELECT CONCAT('B:',b.beneficio_concedido_id) registro_id,b.pessoa_uuid,CAST('BENEFICIO' AS NVARCHAR(30)) natureza,
       g.codigo gestor,tr.codigo codigo_tipo,tr.nome nome_tipo,
       COALESCE(CAST(b.data_evento_concessao AS DATETIMEOFFSET),CAST(b.data_inicio_concessao AS DATETIMEOFFSET),b.source_as_of) ocorrido_em,
       b.source_as_of data_referencia,b.situacao,b.registro_observacao_id source_observacao_id
FROM gold.beneficio_concedido b JOIN ref.tipo_registro tr ON tr.tipo_registro_id=b.tipo_registro_id JOIN ref.gestor g ON g.gestor_id=b.gestor_id
WHERE b.status_analitico='VIGENTE'
UNION ALL
SELECT CONCAT('S:',s.servico_prestado_id),s.pessoa_uuid,CAST('SERVICO' AS NVARCHAR(30)),
       g.codigo,tr.codigo,tr.nome,s.data_hora_servico,s.source_as_of,s.situacao,s.registro_observacao_id
FROM gold.servico_prestado s JOIN ref.tipo_registro tr ON tr.tipo_registro_id=s.tipo_registro_id JOIN ref.gestor g ON g.gestor_id=s.gestor_id
WHERE s.status_analitico='VIGENTE';
GO

-- Serving generalizada espelha as versões analíticas dos fatos, sem transformar Natureza em estrutura.
-- Apenas a versão lógica VIGENTE entra nas views operacionais/BI; HISTORICO, RETIFICADO e EXCLUIDO ficam disponíveis na trilha de versões.
IF OBJECT_ID('serving.registro_integrado','U') IS NULL CREATE TABLE serving.registro_integrado(
 registro_observacao_id BIGINT PRIMARY KEY REFERENCES silver.registro_observacao(registro_observacao_id),
 registro_origem_id BIGINT NOT NULL REFERENCES silver.registro_origem(registro_origem_id),
 codigo_registro_origem NVARCHAR(255) NOT NULL,
 versao_interna INT NOT NULL,
 operacao NVARCHAR(20) NOT NULL,
 status_analitico NVARCHAR(20) NOT NULL,
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 natureza NVARCHAR(30) NOT NULL,
 tipo_registro_id BIGINT NOT NULL,
 tipo_registro_versao_id BIGINT NOT NULL,
 entrega_id UNIQUEIDENTIFIER NOT NULL REFERENCES ingestao.entrega(entrega_id),
 entrega_completa BIT NOT NULL,
 data_inicio_concessao DATE NULL, data_fim_concessao DATE NULL, data_evento_concessao DATE NULL,
 data_hora_servico DATETIMEOFFSET(7) NULL, unidade_servico NVARCHAR(200) NULL,
 situacao NVARCHAR(80) NULL,
 referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id),
 natureza_referencia_territorial NVARCHAR(50) NULL,
 subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id), distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id),
 valor_concedido DECIMAL(18,2) NULL, quantidade DECIMAL(18,4) NULL, unidade NVARCHAR(50) NULL,
 source_as_of DATETIMEOFFSET(7) NOT NULL, qc_resultado NVARCHAR(30) NULL, qc_especifico_implementado BIT NULL,
 vigencia_versao_inicio DATETIMEOFFSET(7) NOT NULL, vigencia_versao_fim DATETIMEOFFSET(7) NULL,
 atualizado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_serving_registro_origem_versao UNIQUE(registro_origem_id,versao_interna),
 CONSTRAINT fk_serving_tipo_contexto FOREIGN KEY(tipo_registro_id,gestor_id,natureza) REFERENCES ref.tipo_registro(tipo_registro_id,gestor_id,natureza),
 CONSTRAINT fk_serving_tipo_versao FOREIGN KEY(tipo_registro_versao_id,tipo_registro_id) REFERENCES ref.tipo_registro_versao(tipo_registro_versao_id,tipo_registro_id),
 CONSTRAINT ck_serving_registro_operacao CHECK(operacao IN('INCLUSAO','ALTERACAO','RETIFICACAO')),
 CONSTRAINT ck_serving_registro_status CHECK(status_analitico IN('VIGENTE','HISTORICO','RETIFICADO','EXCLUIDO')),
 CONSTRAINT ck_serving_registro_vigencia CHECK(vigencia_versao_fim IS NULL OR vigencia_versao_fim>=vigencia_versao_inicio),
 CONSTRAINT ck_serving_registro_natureza CHECK(
   (natureza='BENEFICIO' AND data_hora_servico IS NULL AND unidade_servico IS NULL) OR
   (natureza='SERVICO' AND valor_concedido IS NULL AND quantidade IS NULL AND unidade IS NULL AND data_inicio_concessao IS NULL AND data_fim_concessao IS NULL AND data_evento_concessao IS NULL)));
GO
-- v3.48: Serving generalizado mantém colunas de Benefício semanticamente explícitas.
IF COL_LENGTH('serving.registro_integrado','data_inicio') IS NOT NULL AND COL_LENGTH('serving.registro_integrado','data_inicio_concessao') IS NULL EXEC sp_rename 'serving.registro_integrado.data_inicio','data_inicio_concessao','COLUMN';
IF COL_LENGTH('serving.registro_integrado','data_fim') IS NOT NULL AND COL_LENGTH('serving.registro_integrado','data_fim_concessao') IS NULL EXEC sp_rename 'serving.registro_integrado.data_fim','data_fim_concessao','COLUMN';
IF COL_LENGTH('serving.registro_integrado','data_evento') IS NOT NULL AND COL_LENGTH('serving.registro_integrado','data_evento_concessao') IS NULL EXEC sp_rename 'serving.registro_integrado.data_evento','data_evento_concessao','COLUMN';
IF COL_LENGTH('serving.registro_integrado','valor_monetario') IS NOT NULL AND COL_LENGTH('serving.registro_integrado','valor_concedido') IS NULL EXEC sp_rename 'serving.registro_integrado.valor_monetario','valor_concedido','COLUMN';
GO
-- Migração aditiva v3.31 para instalações anteriores ao modelo de Referência Territorial.
IF COL_LENGTH('gold.beneficio_concedido','referencia_territorial_observacao_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id);
IF COL_LENGTH('gold.beneficio_concedido','natureza_referencia_territorial') IS NULL ALTER TABLE gold.beneficio_concedido ADD natureza_referencia_territorial NVARCHAR(50) NULL;
IF COL_LENGTH('gold.beneficio_concedido','subprefeitura_referencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id);
IF COL_LENGTH('gold.beneficio_concedido','distrito_referencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id);
IF COL_LENGTH('gold.servico_prestado','referencia_territorial_observacao_id') IS NULL ALTER TABLE gold.servico_prestado ADD referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id);
IF COL_LENGTH('gold.servico_prestado','natureza_referencia_territorial') IS NULL ALTER TABLE gold.servico_prestado ADD natureza_referencia_territorial NVARCHAR(50) NULL;
IF COL_LENGTH('gold.servico_prestado','subprefeitura_referencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id);
IF COL_LENGTH('gold.servico_prestado','distrito_referencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id);
IF COL_LENGTH('serving.registro_integrado','referencia_territorial_observacao_id') IS NULL ALTER TABLE serving.registro_integrado ADD referencia_territorial_observacao_id BIGINT NULL REFERENCES silver.referencia_territorial_observacao(referencia_territorial_observacao_id);
IF COL_LENGTH('serving.registro_integrado','natureza_referencia_territorial') IS NULL ALTER TABLE serving.registro_integrado ADD natureza_referencia_territorial NVARCHAR(50) NULL;
IF COL_LENGTH('serving.registro_integrado','subprefeitura_referencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD subprefeitura_referencia_id BIGINT NULL REFERENCES ref.subprefeitura(subprefeitura_id);
IF COL_LENGTH('serving.registro_integrado','distrito_referencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD distrito_referencia_id BIGINT NULL REFERENCES ref.distrito(distrito_id);
GO
-- Backfill da linhagem territorial para fatos já materializados no baseline anterior.
UPDATE b SET referencia_territorial_observacao_id=rt.referencia_territorial_observacao_id,natureza_referencia_territorial=rt.natureza_referencia,
             subprefeitura_referencia_id=rt.subprefeitura_id,distrito_referencia_id=rt.distrito_id
FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id
LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE b.referencia_territorial_observacao_id IS NULL;
UPDATE s SET referencia_territorial_observacao_id=rt.referencia_territorial_observacao_id,natureza_referencia_territorial=rt.natureza_referencia,
             subprefeitura_referencia_id=rt.subprefeitura_id,distrito_referencia_id=rt.distrito_id
FROM gold.servico_prestado s JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id
LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE s.referencia_territorial_observacao_id IS NULL;
UPDATE ri SET referencia_territorial_observacao_id=rt.referencia_territorial_observacao_id,natureza_referencia_territorial=rt.natureza_referencia,
              subprefeitura_referencia_id=rt.subprefeitura_id,distrito_referencia_id=rt.distrito_id
FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id
LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE ri.referencia_territorial_observacao_id IS NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('serving.registro_integrado') AND name='IX_serving_registro_origem_corrente')
 CREATE INDEX IX_serving_registro_origem_corrente ON serving.registro_integrado(registro_origem_id,status_analitico,versao_interna DESC);
GO

-- Recalcula status e Serving a partir da completude derivada. Definida após a tabela Serving para evitar dependência adiada.
CREATE OR ALTER PROCEDURE ingestao.sp_recalcular_entrega
    @entrega_id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @completa BIT=COALESCE((SELECT entrega_completa FROM ingestao.v_entrega_completude WHERE entrega_id=@entrega_id),0);
    DECLARE @rejeitada BIT=CASE WHEN EXISTS(SELECT 1 FROM ingestao.lote WHERE entrega_id=@entrega_id AND status='REJEITADO') THEN 1 ELSE 0 END;
    DECLARE @quarentena BIT=CASE WHEN EXISTS(SELECT 1 FROM ingestao.lote WHERE entrega_id=@entrega_id AND status IN('QUARENTENA','POISON')) THEN 1 ELSE 0 END;

    UPDATE serving.registro_integrado SET entrega_completa=@completa WHERE entrega_id=@entrega_id AND entrega_completa<>@completa;
    UPDATE ingestao.entrega
       SET status=CASE WHEN @quarentena=1 THEN 'QUARENTENA'
                       WHEN @rejeitada=1 THEN 'REJEITADA'
                       WHEN @completa=1 THEN 'PROCESSADA'
                       ELSE 'PROCESSANDO' END,
           ultima_atualizacao=SYSDATETIMEOFFSET()
     WHERE entrega_id=@entrega_id;
END;
GO

CREATE OR ALTER VIEW serving.v_registros_pessoa AS
SELECT r.registro_id,r.pessoa_uuid,r.natureza,r.codigo_tipo codigo,r.nome_tipo nome,r.gestor,
       r.data_referencia,r.ocorrido_em,r.situacao,r.source_observacao_id
FROM gold.v_registros_jornada r;
GO
CREATE OR ALTER VIEW serving.v_beneficios_concedidos_pessoa AS
SELECT bc.beneficio_concedido_id,bc.pessoa_uuid,g.codigo gestor,tr.codigo beneficio,tr.nome nome_beneficio,
       trv.versao versao_tipo,bc.versao_interna,bc.operacao,bc.status_analitico,bc.codigo_registro_origem,trv.tipo_medida,
       trv.regime_vigencia,trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,
       bc.data_inicio_concessao,bc.data_fim_concessao,bc.data_evento_concessao,bc.situacao,bc.valor_concedido,bc.quantidade,
       bc.unidade unidade_medida,bc.source_as_of data_referencia,bc.qc_resultado,COALESCE(bc.qc_especifico_implementado,0) qc_especifico_implementado
FROM gold.beneficio_concedido bc
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=bc.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=bc.tipo_registro_versao_id
JOIN ref.gestor g ON g.gestor_id=bc.gestor_id
WHERE bc.status_analitico='VIGENTE';
GO
CREATE OR ALTER VIEW serving.v_servicos_prestados_pessoa AS
SELECT sp.servico_prestado_id,sp.pessoa_uuid,g.codigo gestor,tr.codigo servico,tr.nome nome_servico,
       trv.versao versao_tipo,sp.versao_interna,sp.operacao,sp.status_analitico,sp.codigo_registro_origem,
       sp.data_hora_servico,sp.unidade_servico,sp.situacao,sp.source_as_of data_referencia
FROM gold.servico_prestado sp
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=sp.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=sp.tipo_registro_versao_id
JOIN ref.gestor g ON g.gestor_id=sp.gestor_id
WHERE sp.status_analitico='VIGENTE';
GO
CREATE OR ALTER VIEW serving.v_possibilidades_compativeis AS
SELECT ap.pessoa_uuid,ap.natureza,tr.codigo,tr.nome,g.codigo gestor,ap.implementacao_versao regra_versao,ap.avaliado_em,ap.validade_ate,ap.motivo
FROM qualidade.avaliacao_possibilidade ap
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ap.tipo_registro_versao_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=trv.tipo_registro_id
JOIN ref.gestor g ON g.gestor_id=tr.gestor_id
WHERE ap.resultado='COMPATIVEL';
GO

-- Views estáveis para API/Power BI. BI não deve apontar diretamente para serving.registro_integrado.
CREATE OR ALTER VIEW serving.v_bi_entregas AS
SELECT e.entrega_id,g.codigo gestor,so.codigo sistema_origem,e.natureza,
       CASE WHEN e.tipo_registro_id IS NULL THEN 'CADASTRO' ELSE 'FATUAL' END contexto_entrega,
       COALESCE(tr.codigo,'CADASTRO') codigo_tipo,COALESCE(tr.nome,'Atualização cadastral de Pessoas') nome_tipo,e.status,
       COALESCE(l.lotes_validos,0) lotes_validos,COALESCE(l.lote_total_declarado,0) lote_total_declarado,
       COALESCE(c.entrega_completa,0) entrega_completa,
       e.data_referencia,e.recebido_em first_seen_at,e.ultima_atualizacao,DATEDIFF(MINUTE,e.recebido_em,e.ultima_atualizacao) duracao_minutos
FROM ingestao.entrega e
JOIN ref.gestor g ON g.gestor_id=e.gestor_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=e.entrega_id
OUTER APPLY (SELECT SUM(CASE WHEN il.status='PROCESSADO' THEN 1 ELSE 0 END) lotes_validos,MAX(il.lote_total) lote_total_declarado FROM ingestao.lote il WHERE il.entrega_id=e.entrega_id) l;
GO
CREATE OR ALTER VIEW serving.v_bi_ingestao_itens AS
SELECT ip.item_processado_id,ip.lote_id,l.entrega_id,g.codigo gestor,so.codigo sistema_origem,e.natureza,
       COALESCE(tr.codigo,'CADASTRO') codigo_tipo,ip.classe_item,ip.codigo_origem,ip.resultado,ip.versao_interna,
       ip.data_referencia,ip.processado_em,
       CASE WHEN ip.resultado='RETRANSMITIDO' THEN 1 ELSE 0 END retransmitido,
       CASE WHEN ip.resultado IN('VERSIONADO','REABERTO','EXCLUIDO') THEN 1 ELSE 0 END nova_versao
FROM ingestao.item_processado ip
JOIN ingestao.lote l ON l.lote_id=ip.lote_id
JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
JOIN ref.gestor g ON g.gestor_id=e.gestor_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
WHERE ip.consolidado_em IS NULL;
GO
CREATE OR ALTER VIEW serving.v_bi_ingestao_itens_resumo AS
SELECT r.entrega_id,g.codigo gestor,so.codigo sistema_origem,e.natureza,COALESCE(tr.codigo,'CADASTRO') codigo_tipo,r.processado_hora_utc,r.classe_item,r.resultado,r.quantidade,r.primeiro_processado_em,r.ultimo_processado_em
FROM ingestao.item_processado_resumo r JOIN ingestao.entrega e ON e.entrega_id=r.entrega_id JOIN ref.gestor g ON g.gestor_id=e.gestor_id JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id;
GO
-- Qualidade do envio por pacote/Entrega. Não expõe CPF; somente volumetria, processamento e redundância.
CREATE OR ALTER VIEW serving.v_bi_qualidade_envios AS
WITH item_fonte AS (
 SELECT l.entrega_id,ip.classe_item,ip.resultado,CAST(1 AS BIGINT) quantidade FROM ingestao.item_processado ip JOIN ingestao.lote l ON l.lote_id=ip.lote_id WHERE ip.consolidado_em IS NULL
 UNION ALL SELECT r.entrega_id,r.classe_item,r.resultado,r.quantidade FROM ingestao.item_processado_resumo r
), itens AS (
 SELECT entrega_id,SUM(quantidade) itens_recebidos,SUM(CASE WHEN classe_item='PESSOA' THEN quantidade ELSE 0 END) pessoas_recebidas,SUM(CASE WHEN classe_item='REGISTRO' THEN quantidade ELSE 0 END) registros_recebidos,SUM(CASE WHEN resultado='RETRANSMITIDO' THEN quantidade ELSE 0 END) retransmitidos,SUM(CASE WHEN resultado IN('VERSIONADO','REABERTO','EXCLUIDO') THEN quantidade ELSE 0 END) novas_versoes,SUM(CASE WHEN resultado='INCLUIDO' THEN quantidade ELSE 0 END) incluidos FROM item_fonte GROUP BY entrega_id
), lotes AS (
 SELECT entrega_id,COUNT_BIG(*) lotes,
        SUM(CASE WHEN status='PROCESSADO' THEN 1 ELSE 0 END) lotes_processados,
        SUM(CASE WHEN status='REJEITADO' THEN 1 ELSE 0 END) lotes_rejeitados,
        SUM(CASE WHEN status='QUARENTENA' THEN 1 ELSE 0 END) lotes_quarentena,
        SUM(CASE WHEN status='POISON' THEN 1 ELSE 0 END) lotes_poison,
        SUM(tentativa_count) tentativas_processamento,
        SUM(recuperacao_count) recuperacoes_lease,
        MAX(erro_codigo) erro_codigo
 FROM ingestao.lote GROUP BY entrega_id
)
SELECT e.entrega_id,g.codigo gestor,so.codigo sistema_origem,e.natureza,
       CASE WHEN e.tipo_registro_id IS NULL THEN 'CADASTRO' ELSE 'FATUAL' END contexto_entrega,
       COALESCE(tr.codigo,'CADASTRO') codigo_tipo,COALESCE(tr.nome,'Atualização cadastral de Pessoas') nome_tipo,
       e.status,e.data_referencia,e.recebido_em,e.bytes_recebidos,
       COALESCE(c.entrega_completa,0) entrega_completa,
       CASE WHEN e.status='PROCESSADA' THEN 1 ELSE 0 END processada,
       CASE WHEN e.status IN('REJEITADA','QUARENTENA') THEN 1 ELSE 0 END com_falha,
       COALESCE(i.itens_recebidos,0) itens_recebidos,COALESCE(i.pessoas_recebidas,0) pessoas_recebidas,
       COALESCE(i.registros_recebidos,0) registros_recebidos,COALESCE(i.retransmitidos,0) retransmitidos,
       COALESCE(i.novas_versoes,0) novas_versoes,COALESCE(i.incluidos,0) incluidos,
       CASE WHEN COALESCE(i.itens_recebidos,0)>0 THEN CAST(COALESCE(i.retransmitidos,0)*1.0/i.itens_recebidos AS DECIMAL(9,6)) ELSE NULL END taxa_retransmissao,
       CASE WHEN e.tipo_registro_id IS NOT NULL AND COALESCE(i.registros_recebidos,0)=0 THEN 1 ELSE 0 END zero_fatos_no_periodo,
       COALESCE(l.lotes,0) lotes,COALESCE(l.lotes_processados,0) lotes_processados,COALESCE(l.lotes_rejeitados,0) lotes_rejeitados,
       COALESCE(l.lotes_quarentena,0) lotes_quarentena,COALESCE(l.lotes_poison,0) lotes_poison,
       COALESCE(l.tentativas_processamento,0) tentativas_processamento,COALESCE(l.recuperacoes_lease,0) recuperacoes_lease,l.erro_codigo,
       DATEDIFF(MINUTE,e.recebido_em,e.ultima_atualizacao) duracao_minutos
FROM ingestao.entrega e
JOIN ref.gestor g ON g.gestor_id=e.gestor_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=e.entrega_id
LEFT JOIN itens i ON i.entrega_id=e.entrega_id
LEFT JOIN lotes l ON l.entrega_id=e.entrega_id;
GO

-- Pessoa: campos de identificação compartilhados por padrão entre Gestores/credenciais autorizados em âmbito municipal.
-- Exceções de projeção não incidem sobre estes campos; qualquer mudança deste conjunto exige alteração explícita desta view e da especificação.
-- v3.50: nomenclatura institucional - a Pessoa é compartilhada em âmbito municipal; a view pública passa a usar nome descritivo.
IF OBJECT_ID('serving.v_pessoa_nucleo_municipal','V') IS NOT NULL
    DROP VIEW serving.v_pessoa_nucleo_municipal;
GO

-- v3.51: nomenclatura simplificada - o objeto público é simplesmente Pessoa.
IF OBJECT_ID('serving.v_pessoa_municipal','V') IS NOT NULL
    DROP VIEW serving.v_pessoa_municipal;
GO

CREATE OR ALTER VIEW serving.v_pessoa AS
SELECT pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em
FROM gold.pessoa;
GO

-- BI operacional deliberadamente não expõe UUID da Pessoa, hash do agente, credencial_id nem api_evento_id.
-- Investigação individual usa a camada restrita controle.api_evento/controle.api_evento_pessoa por correlation_id.
CREATE OR ALTER VIEW serving.v_bi_api AS
SELECT COALESCE(g.codigo,'NAO_IDENTIFICADO') gestor,ae.rota,ae.metodo,ae.status_http,ae.duracao_ms,ae.bytes_recebidos,
       ae.recurso_codigo,
       CASE WHEN t.qtd_pessoas=0 AND ae.pessoa_uuid IS NOT NULL THEN 1 ELSE t.qtd_pessoas END qtd_pessoas,ae.ocorrido_em,
       CASE WHEN ae.status_http>=400 THEN 1 ELSE 0 END erro
FROM controle.api_evento ae
LEFT JOIN ref.gestor g ON g.gestor_id=ae.gestor_id
OUTER APPLY(SELECT COUNT_BIG(*) qtd_pessoas FROM controle.api_evento_pessoa ep WHERE ep.api_evento_id=ae.api_evento_id) t;
GO
CREATE OR ALTER VIEW serving.v_bi_qualidade_pessoa AS
SELECT po.pessoa_observacao_id,g.codigo gestor,po.source_as_of,COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
 COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,pg.natureza_referencia natureza_referencia_territorial,pg.situacao_geografia,pg.referencia_malha,CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,
 CASE WHEN LEN(LTRIM(RTRIM(po.nome_completo)))>0 THEN 1 ELSE 0 END nome_preenchido,CASE WHEN po.data_nascimento IS NULL THEN 0 ELSE 1 END nascimento_preenchido,
 CASE WHEN LEN(LTRIM(RTRIM(po.nome_mae)))>0 THEN 1 ELSE 0 END nome_mae_preenchido,
 CASE WHEN pg.subprefeitura_id IS NULL OR pg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_preenchida,
 po.cpf_ausente_motivo,gp.status_cpf
FROM silver.pessoa_observacao po JOIN ref.gestor g ON g.gestor_id=po.gestor_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=vc.pessoa_uuid
LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id;
GO
CREATE OR ALTER VIEW serving.v_bi_territorializacao AS
SELECT g.codigo gestor,po.source_as_of,pg.natureza_referencia natureza_referencia_territorial,COALESCE(pg.situacao_geografia,'SEM_REFERENCIA_TERRITORIAL') situacao_geografia,pg.referencia_malha,COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,COUNT_BIG(*) pessoas_observacao
FROM silver.pessoa_observacao po JOIN ref.gestor g ON g.gestor_id=po.gestor_id LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id
GROUP BY g.codigo,po.source_as_of,pg.natureza_referencia,COALESCE(pg.situacao_geografia,'SEM_REFERENCIA_TERRITORIAL'),pg.referencia_malha,COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL'),COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL');
GO
CREATE OR ALTER VIEW serving.v_bi_manutencao_bronze AS SELECT ciclo_id,iniciado_em,finalizado_em,DATEDIFF(SECOND,iniciado_em,finalizado_em) duracao_segundos,bucket_inicial,after_inicial,bucket_proximo,after_proximo,objetos_examinados,orfaos_removidos,temporarios_removidos,locks_nao_adquiridos,falhas_storage,orfao_mais_antigo_em FROM controle.bronze_manutencao_ciclo;
GO
CREATE OR ALTER VIEW serving.v_bi_carga_inicial AS
WITH serie AS (
 SELECT DATEADD(HOUR,DATEDIFF(HOUR,CONVERT(datetime2(0),'20000101'),CONVERT(datetime2(0),SWITCHOFFSET(ip.processado_em,'+00:00'))),CONVERT(datetime2(0),'20000101')) hora_utc,SUM(CASE WHEN ip.classe_item='PESSOA' THEN CAST(1 AS BIGINT) ELSE 0 END) pessoas FROM ingestao.item_processado ip WHERE ip.consolidado_em IS NULL GROUP BY DATEADD(HOUR,DATEDIFF(HOUR,CONVERT(datetime2(0),'20000101'),CONVERT(datetime2(0),SWITCHOFFSET(ip.processado_em,'+00:00'))),CONVERT(datetime2(0),'20000101'))
 UNION ALL SELECT processado_hora_utc,SUM(CASE WHEN classe_item='PESSOA' THEN quantidade ELSE 0 END) FROM ingestao.item_processado_resumo GROUP BY processado_hora_utc), agg AS (SELECT hora_utc,SUM(pessoas) pessoas_processadas FROM serie GROUP BY hora_utc)
SELECT a.hora_utc,a.pessoas_processadas,m.ativo modo_carga_inicial,CAST(a.pessoas_processadas AS DECIMAL(18,2)) pessoas_por_hora_ativa FROM agg a CROSS JOIN controle.modo_carga_inicial m WHERE m.estado_id=1;
GO

CREATE OR ALTER VIEW serving.v_bi_linkage AS
SELECT po.pessoa_observacao_id,g.codigo gestor,so.codigo sistema_origem,po.source_as_of,vc.status,vc.metodo_resolucao,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' THEN vc.score END score,
       CASE WHEN vc.pessoa_uuid IS NULL THEN 0 ELSE 1 END possui_uuid,
       CASE WHEN vc.metodo_resolucao='CPF_DETERMINISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_cpf,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_fallback,
       CASE WHEN vc.status='NAO_RESOLVIDO' THEN 1 ELSE 0 END nao_resolvido,
       CASE WHEN vc.status='CONFLITO' THEN 1 ELSE 0 END conflito,
       CASE WHEN lrres.motivo='SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO' THEN 1 ELSE 0 END sem_candidato_no_bloco,
       lrres.motivo motivo_linkage,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,po.cpf_ausente_motivo,
       CASE WHEN DATEPART(DAY,po.data_nascimento)=1 AND DATEPART(MONTH,po.data_nascimento)=1 THEN 1 ELSE 0 END nascimento_0101,
       CASE WHEN pg.subprefeitura_id IS NULL OR pg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_preenchida,
       ml.versao modelo_fallback_versao,vc.linkage_run_id,lr.tipo_run,lr.iniciado_em linkage_run_iniciado_em,
       lr.finalizado_em linkage_run_finalizado_em,ptl.valor t_linkage,lr.status linkage_run_status,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,pg.natureza_referencia natureza_referencia_territorial
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=pori.sistema_origem_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id=vc.modelo_id
LEFT JOIN identidade.linkage_run lr ON lr.linkage_run_id=vc.linkage_run_id
LEFT JOIN identidade.parametro_linkage ptl ON ptl.modelo_id=vc.modelo_id AND ptl.nome='T_LINKAGE'
LEFT JOIN identidade.linkage_resultado lrres ON lrres.linkage_run_id=vc.linkage_run_id AND lrres.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id;
GO
-- Qualidade de identidade por origem. CPF é representado somente por flags/motivo, nunca pelo valor.
CREATE OR ALTER VIEW serving.v_bi_qualidade_identidade_origem AS
SELECT l.* FROM serving.v_bi_linkage l;
GO

CREATE OR ALTER VIEW serving.v_bi_linkage_runs AS
SELECT lr.linkage_run_id,lr.modelo_id,lr.modelo_versao,ml.algoritmo_versao,lr.tipo_run,lr.status,
       lr.gestor_codigo_filtro,lr.pessoa_observacao_id_filtro,lr.desde_filtro,lr.limite_solicitado,
       lr.batch_size,lr.max_parallelism,lr.pessoa_observacao_id_high_watermark,lr.registros_elegiveis,
       lr.avaliados,lr.resolvidos,lr.nao_resolvidos,lr.conflitos,lr.sem_candidato_no_bloco,lr.solicitado_por,lr.motivo,
       lr.correlation_id,lr.iniciado_em,lr.finalizado_em,lr.publicado_em,
       ml.snapshot_capturado_em,ml.amostra_metodo,ml.amostra_pool_tamanho,ml.amostra_m_tamanho,ml.amostra_u_tamanho,
       DATEDIFF_BIG(MILLISECOND,lr.iniciado_em,COALESCE(lr.finalizado_em,SYSDATETIMEOFFSET())) duracao_ms,
       ptl.valor t_linkage,pcm.valor conflict_margin,pprior.valor prior_referencia,pmin.valor prior_block_min,pmax.valor prior_block_max,pminm.valor min_m_independent_pairs
FROM identidade.linkage_run lr JOIN identidade.modelo_linkage ml ON ml.modelo_id=lr.modelo_id
LEFT JOIN identidade.parametro_linkage ptl ON ptl.modelo_id=lr.modelo_id AND ptl.nome='T_LINKAGE'
LEFT JOIN identidade.parametro_linkage pcm ON pcm.modelo_id=lr.modelo_id AND pcm.nome='CONFLICT_MARGIN'
LEFT JOIN identidade.parametro_linkage pprior ON pprior.modelo_id=lr.modelo_id AND pprior.nome='PRIOR_MATCH_PROBABILITY'
LEFT JOIN identidade.parametro_linkage pmin ON pmin.modelo_id=lr.modelo_id AND pmin.nome='PRIOR_BLOCK_MIN'
LEFT JOIN identidade.parametro_linkage pmax ON pmax.modelo_id=lr.modelo_id AND pmax.nome='PRIOR_BLOCK_MAX'
LEFT JOIN identidade.parametro_linkage pminm ON pminm.modelo_id=lr.modelo_id AND pminm.nome='MIN_M_INDEPENDENT_PAIRS';
GO
CREATE OR ALTER VIEW serving.v_bi_qc AS
SELECT ro.registro_observacao_id,g.codigo gestor,ro.natureza,tr.codigo codigo_tipo,tr.nome nome_tipo,trv.versao,ro.source_as_of,
 qci.status qc_implementacao,qcr.resultado,qcr.regra_codigo,qcr.motivo
FROM silver.registro_observacao ro
JOIN ref.gestor g ON g.gestor_id=ro.gestor_id JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ro.tipo_registro_versao_id
LEFT JOIN qualidade.qc_registro_implementacao qci ON qci.tipo_registro_versao_id=ro.tipo_registro_versao_id
LEFT JOIN qualidade.qc_registro_resultado qcr ON qcr.registro_observacao_id=ro.registro_observacao_id;
GO
-- Qualidade de dados por Secretaria/Gestor e Benefício Concedido. Uma linha por fato VIGENTE,
-- com flags aditivas para que o Power BI agregue por Gestor, Tipo, período e geografia sem score opaco.
CREATE OR ALTER VIEW serving.v_bi_qualidade_beneficios_concedidos AS
SELECT ri.registro_observacao_id,ri.registro_origem_id,g.codigo gestor,
       tr.codigo beneficio,tr.nome nome_beneficio,trv.tipo_medida,ri.source_as_of,
       trv.regime_vigencia,trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
       COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       ri.natureza_referencia_territorial,ri.versao_interna,ri.operacao,
       CAST(CASE WHEN ri.entrega_completa=1 THEN 1 ELSE 0 END AS INT) entrega_completa,
       CAST(CASE WHEN ri.subprefeitura_referencia_id IS NOT NULL AND ri.distrito_referencia_id IS NOT NULL THEN 1 ELSE 0 END AS INT) geografia_preenchida,
       CAST(CASE trv.tipo_medida
              WHEN 'MONETARIO' THEN CASE WHEN ri.valor_concedido IS NOT NULL THEN 1 ELSE 0 END
              WHEN 'QUANTIDADE' THEN CASE WHEN ri.quantidade IS NOT NULL AND NULLIF(LTRIM(RTRIM(ri.unidade)),'') IS NOT NULL THEN 1 ELSE 0 END
              WHEN 'MONETARIO_E_QUANTIDADE' THEN CASE WHEN ri.valor_concedido IS NOT NULL AND ri.quantidade IS NOT NULL AND NULLIF(LTRIM(RTRIM(ri.unidade)),'') IS NOT NULL THEN 1 ELSE 0 END
              WHEN 'SEM_MEDIDA' THEN 1 ELSE 0 END AS INT) medida_preenchida,
       CAST(CASE WHEN ri.data_inicio_concessao IS NOT NULL AND ri.data_fim_concessao IS NOT NULL AND ri.data_fim_concessao<ri.data_inicio_concessao THEN 0
                      WHEN trv.regime_vigencia='PRAZO_DETERMINADO' AND ri.data_fim_concessao IS NULL THEN 0
                      WHEN trv.regime_vigencia IS NULL OR trv.regime_vigencia='NAO_APLICAVEL' THEN 0 ELSE 1 END AS INT) vigencia_coerente,
       CAST(CASE WHEN trv.regime_vigencia='PRAZO_INDETERMINADO' THEN 1
                      WHEN trv.regime_vigencia='PRAZO_DETERMINADO' AND ri.data_fim_concessao IS NOT NULL THEN 1 ELSE 0 END AS INT) vigencia_conforme_regime,
       CAST(CASE WHEN (trv.data_inicio_permitida_concessao IS NOT NULL OR trv.data_fim_permitida_concessao IS NOT NULL)
                          AND ri.data_inicio_concessao IS NULL THEN NULL
                      WHEN (trv.data_inicio_permitida_concessao IS NULL OR ri.data_inicio_concessao>=trv.data_inicio_permitida_concessao)
                       AND (trv.data_fim_permitida_concessao IS NULL OR ri.data_inicio_concessao<=trv.data_fim_permitida_concessao) THEN 1 ELSE 0 END AS INT) concessao_na_janela_permitida,
       CAST(CASE WHEN (trv.data_inicio_permitida_concessao IS NOT NULL OR trv.data_fim_permitida_concessao IS NOT NULL)
                          AND ri.data_inicio_concessao IS NULL THEN 0 ELSE 1 END AS INT) concessao_janela_verificavel,
       CAST(CASE WHEN qci.status='IMPLEMENTADO' THEN 1 ELSE 0 END AS INT) qc_implementado,
       CAST(CASE WHEN qca.qc_resultado='VALIDO' THEN 1 ELSE 0 END AS INT) qc_valido,
       CAST(CASE WHEN qca.qc_resultado='DIVERGENTE' THEN 1 ELSE 0 END AS INT) qc_divergente,
       CAST(CASE WHEN qca.qc_resultado='NAO_VERIFICAVEL' THEN 1 ELSE 0 END AS INT) qc_nao_verificavel,
       COALESCE(qca.qtd_regras_qc,0) qtd_regras_qc,
       CAST(CASE WHEN ri.versao_interna>1 THEN 1 ELSE 0 END AS INT) possui_versionamento,
       CAST(CASE WHEN ri.operacao='RETIFICACAO' THEN 1 ELSE 0 END AS INT) retificacao_vigente
FROM serving.registro_integrado ri
JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ri.tipo_registro_versao_id
LEFT JOIN qualidade.qc_registro_implementacao qci ON qci.tipo_registro_versao_id=ri.tipo_registro_versao_id
OUTER APPLY(
 SELECT COUNT(*) qtd_regras_qc,
        CASE WHEN COUNT(*)=0 THEN NULL
             WHEN SUM(CASE WHEN q.resultado='DIVERGENTE' THEN 1 ELSE 0 END)>0 THEN 'DIVERGENTE'
             WHEN SUM(CASE WHEN q.resultado='NAO_VERIFICAVEL' THEN 1 ELSE 0 END)>0 THEN 'NAO_VERIFICAVEL'
             ELSE 'VALIDO' END qc_resultado
 FROM qualidade.qc_registro_resultado q WHERE q.registro_observacao_id=ri.registro_observacao_id
) qca
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE';
GO

-- Qualidade de dados por Secretaria/Gestor e Serviço Prestado. Campos opcionais são expostos como taxa de
-- preenchimento, não como erro contratual; QC continua sendo a fonte de validade/divergência específica do Tipo.
CREATE OR ALTER VIEW serving.v_bi_qualidade_servicos_prestados AS
SELECT ri.registro_observacao_id,ri.registro_origem_id,g.codigo gestor,
       tr.codigo servico,tr.nome nome_servico,ri.source_as_of,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
       COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       ri.natureza_referencia_territorial,ri.versao_interna,ri.operacao,
       CAST(CASE WHEN ri.entrega_completa=1 THEN 1 ELSE 0 END AS INT) entrega_completa,
       CAST(CASE WHEN ri.subprefeitura_referencia_id IS NOT NULL AND ri.distrito_referencia_id IS NOT NULL THEN 1 ELSE 0 END AS INT) geografia_preenchida,
       CAST(CASE WHEN ri.data_hora_servico IS NOT NULL THEN 1 ELSE 0 END AS INT) data_hora_preenchida,
       CAST(CASE WHEN NULLIF(LTRIM(RTRIM(ri.unidade_servico)),'') IS NOT NULL THEN 1 ELSE 0 END AS INT) unidade_servico_preenchida,
       CAST(CASE WHEN NULLIF(LTRIM(RTRIM(ri.situacao)),'') IS NOT NULL THEN 1 ELSE 0 END AS INT) situacao_preenchida,
       CAST(CASE WHEN qci.status='IMPLEMENTADO' THEN 1 ELSE 0 END AS INT) qc_implementado,
       CAST(CASE WHEN qca.qc_resultado='VALIDO' THEN 1 ELSE 0 END AS INT) qc_valido,
       CAST(CASE WHEN qca.qc_resultado='DIVERGENTE' THEN 1 ELSE 0 END AS INT) qc_divergente,
       CAST(CASE WHEN qca.qc_resultado='NAO_VERIFICAVEL' THEN 1 ELSE 0 END AS INT) qc_nao_verificavel,
       COALESCE(qca.qtd_regras_qc,0) qtd_regras_qc,
       CAST(CASE WHEN ri.versao_interna>1 THEN 1 ELSE 0 END AS INT) possui_versionamento,
       CAST(CASE WHEN ri.operacao='RETIFICACAO' THEN 1 ELSE 0 END AS INT) retificacao_vigente
FROM serving.registro_integrado ri
JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ri.tipo_registro_versao_id
LEFT JOIN qualidade.qc_registro_implementacao qci ON qci.tipo_registro_versao_id=ri.tipo_registro_versao_id
OUTER APPLY(
 SELECT COUNT(*) qtd_regras_qc,
        CASE WHEN COUNT(*)=0 THEN NULL
             WHEN SUM(CASE WHEN q.resultado='DIVERGENTE' THEN 1 ELSE 0 END)>0 THEN 'DIVERGENTE'
             WHEN SUM(CASE WHEN q.resultado='NAO_VERIFICAVEL' THEN 1 ELSE 0 END)>0 THEN 'NAO_VERIFICAVEL'
             ELSE 'VALIDO' END qc_resultado
 FROM qualidade.qc_registro_resultado q WHERE q.registro_observacao_id=ri.registro_observacao_id
) qca
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE';
GO
CREATE OR ALTER VIEW serving.v_bi_beneficios_concedidos AS
SELECT ri.registro_observacao_id beneficio_observacao_id,ri.registro_origem_id,ri.codigo_registro_origem,ri.versao_interna,ri.operacao,ri.status_analitico,
 g.codigo gestor,tr.codigo beneficio,tr.nome nome_beneficio,trv.tipo_medida,trv.regime_vigencia,
 trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,
 ri.data_inicio_concessao,ri.data_fim_concessao,ri.data_evento_concessao,ri.valor_concedido,ri.quantidade,ri.unidade unidade_medida,ri.source_as_of,
 ri.vigencia_versao_inicio,ri.vigencia_versao_fim,
 COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
 ri.natureza_referencia_territorial,ri.referencia_territorial_observacao_id,
 ri.entrega_completa,ri.qc_resultado,COALESCE(ri.qc_especifico_implementado,0) qc_especifico_implementado
FROM serving.registro_integrado ri JOIN ref.gestor g ON g.gestor_id=ri.gestor_id JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ri.tipo_registro_versao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE';
GO
CREATE OR ALTER VIEW serving.v_bi_registros AS
-- Proteção explícita: esta view nunca expõe valor_concedido/quantidade/unidade.
SELECT r.registro_id,r.natureza,r.codigo,r.nome,r.gestor,r.data_referencia,r.ocorrido_em,r.situacao,r.source_observacao_id
FROM serving.v_registros_pessoa r;
GO
CREATE OR ALTER VIEW serving.v_bi_servicos_prestados AS
-- Proteção explícita: Serviços Prestados não expõem colunas monetárias da tabela Serving generalizada.
SELECT ri.registro_observacao_id servico_observacao_id,ri.registro_origem_id,ri.codigo_registro_origem,ri.versao_interna,ri.operacao,ri.status_analitico,
       g.codigo gestor,tr.codigo servico,tr.nome nome_servico,
       ri.data_hora_servico,ri.unidade_servico,ri.situacao,ri.source_as_of,ri.vigencia_versao_inicio,ri.vigencia_versao_fim,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,ri.natureza_referencia_territorial,ri.referencia_territorial_observacao_id,ri.entrega_completa
FROM serving.registro_integrado ri JOIN ref.gestor g ON g.gestor_id=ri.gestor_id JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE';
GO
-- v3.43: contagens distintas de Pessoas são pré-agregadas no SQL; o modelo semântico padrão não importa pessoa_uuid.
CREATE OR ALTER VIEW serving.v_bi_beneficios_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(30)) gestor,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE'
UNION ALL
SELECT 'GESTOR',g.codigo,COUNT_BIG(DISTINCT ri.pessoa_uuid)
FROM serving.registro_integrado ri JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE' GROUP BY g.codigo;
GO
CREATE OR ALTER VIEW serving.v_bi_servicos_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(200)) nome_servico,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE'
UNION ALL
SELECT 'SERVICO',tr.nome,COUNT_BIG(DISTINCT ri.pessoa_uuid)
FROM serving.registro_integrado ri JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE' GROUP BY tr.nome;
GO
CREATE OR ALTER VIEW serving.v_bi_registros_pessoas AS
SELECT COUNT_BIG(DISTINCT pessoa_uuid) pessoas FROM serving.registro_integrado WHERE status_analitico='VIGENTE';
GO
CREATE OR ALTER VIEW serving.v_bi_possibilidades_pessoas AS
SELECT COUNT_BIG(DISTINCT pessoa_uuid) pessoas FROM serving.v_possibilidades_compativeis;
GO
-- Auditoria de versionamento interno: mostra inclusive retificações e exclusões sem reintroduzi-las nas métricas factuais.
-- View de apoio/auditoria de versionamento; deliberadamente não integra o modelo semântico Power BI padrão para evitar cardinalidade/volume desnecessários. Consultas institucionais de ALTERACAO/RETIFICACAO devem usar ro.operacao nesta view.
CREATE OR ALTER VIEW serving.v_bi_registro_versoes AS
SELECT ro.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,ro.versao_interna,ro.operacao,ro.conteudo_hash,
       so.codigo sistema_origem,g.codigo gestor,ro.natureza,tr.codigo codigo_tipo,tr.nome nome_tipo,ro.source_as_of,ro.registrado_em,
       ri.status_analitico,CASE WHEN ri.registro_observacao_id IS NULL AND ro.operacao='EXCLUSAO' THEN 'EXCLUSAO' ELSE 'VERSAO_FATO' END classe_versao
FROM silver.registro_observacao ro
JOIN silver.registro_origem rorig ON rorig.registro_origem_id=ro.registro_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=rorig.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=ro.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
LEFT JOIN serving.registro_integrado ri ON ri.registro_observacao_id=ro.registro_observacao_id;
GO
CREATE OR ALTER VIEW serving.v_bi_possibilidades AS
SELECT natureza,codigo,nome,gestor,regra_versao,avaliado_em,validade_ate,motivo FROM serving.v_possibilidades_compativeis;
GO
CREATE OR ALTER VIEW serving.v_bi_pendencias_identidade AS
SELECT po.pessoa_observacao_id,g.codigo gestor,po.source_as_of,e.recebido_em pendente_desde,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,pg.natureza_referencia natureza_referencia_territorial,
       CASE WHEN po.cpf IS NULL THEN COALESCE(po.cpf_ausente_motivo,'SEM_CPF') ELSE COALESCE(vf.motivo,vf.status) END motivo,
       CASE WHEN vf.status='CONFLITO' THEN 'ALTA' WHEN po.cpf IS NULL THEN 'ALTA' ELSE 'MEDIA' END prioridade,
       CASE WHEN vf.metodo_resolucao='LINKAGE_PROBABILISTICO' THEN vf.score END score,vf.status linkage_status,vf.metodo_resolucao,
       DATEDIFF(DAY,
                CAST(e.recebido_em AT TIME ZONE 'E. South America Standard Time' AS date),
                CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. South America Standard Time' AS date)) dias_pendente,
       CASE WHEN DATEDIFF(DAY,CAST(e.recebido_em AT TIME ZONE 'E. South America Standard Time' AS date),CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. South America Standard Time' AS date))<=30 THEN '0-30_DIAS'
            WHEN DATEDIFF(DAY,CAST(e.recebido_em AT TIME ZONE 'E. South America Standard Time' AS date),CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. South America Standard Time' AS date))<=90 THEN '31-90_DIAS'
            WHEN DATEDIFF(DAY,CAST(e.recebido_em AT TIME ZONE 'E. South America Standard Time' AS date),CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. South America Standard Time' AS date))<=180 THEN '91-180_DIAS'
            WHEN DATEDIFF(DAY,CAST(e.recebido_em AT TIME ZONE 'E. South America Standard Time' AS date),CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. South America Standard Time' AS date))<=365 THEN '181-365_DIAS' ELSE '365+_DIAS' END faixa_envelhecimento
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN ingestao.lote l ON l.lote_id=po.lote_id JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
LEFT JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id
WHERE po.cpf IS NULL OR vf.status IN('NAO_RESOLVIDO','CONFLITO');
GO
-- SLA operacional de recebimento. O prazo e o marco são propriedades da versão do Tipo, não do código da aplicação.
-- v3.29: recebido_em é um instante oficial atribuído pela API e persistido em UTC. Para regras de calendário,
-- a data operacional é derivada explicitamente no fuso institucional de São Paulo ('E. South America Standard Time').
-- dias_para_recebimento mede a latência total; dias_atraso mede somente o excedente sobre o SLA.
CREATE OR ALTER VIEW serving.v_bi_atrasos AS
WITH base AS (
 SELECT ri.registro_observacao_id,g.codigo gestor,ri.natureza,tr.codigo codigo_tipo,tr.nome nome_tipo,trv.versao,
        trv.prazo_recebimento_dias,trv.marco_atraso_codigo,ri.source_as_of,e.recebido_em,ri.entrega_completa,ri.qc_resultado,
        COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,ri.natureza_referencia_territorial,
        CASE trv.marco_atraso_codigo
          WHEN 'DATA_EVENTO_CONCESSAO' THEN ri.data_evento_concessao
          WHEN 'DATA_INICIO_CONCESSAO' THEN ri.data_inicio_concessao
          WHEN 'DATA_FIM_CONCESSAO' THEN ri.data_fim_concessao
          WHEN 'DATA_HORA_SERVICO' THEN CAST(ri.data_hora_servico AS DATE)
          WHEN 'SOURCE_AS_OF' THEN CAST(ri.source_as_of AS DATE)
        END data_marco_atraso
 FROM serving.registro_integrado ri
 JOIN ingestao.entrega e ON e.entrega_id=ri.entrega_id
 JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
 JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ri.tipo_registro_versao_id
 JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
 LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
 LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
 WHERE trv.monitorar_atraso=1 AND ri.status_analitico='VIGENTE'
), calc_data AS (
 SELECT b.*,CAST(b.recebido_em AT TIME ZONE 'E. South America Standard Time' AS DATE) data_recebimento_sla
 FROM base b
), calc AS (
 SELECT b.*,CASE WHEN b.data_marco_atraso IS NULL THEN NULL
                 ELSE DATEDIFF(DAY,b.data_marco_atraso,b.data_recebimento_sla) END dias_para_recebimento
 FROM calc_data b
)
SELECT c.registro_observacao_id,c.gestor,c.natureza,c.codigo_tipo,c.nome_tipo,c.versao,
       c.prazo_recebimento_dias,c.marco_atraso_codigo,c.data_marco_atraso,c.recebido_em,c.data_recebimento_sla,c.source_as_of,
       c.dias_para_recebimento,
       CASE WHEN c.dias_para_recebimento IS NULL OR c.dias_para_recebimento<0 THEN NULL
            WHEN c.dias_para_recebimento>c.prazo_recebimento_dias THEN c.dias_para_recebimento-c.prazo_recebimento_dias ELSE 0 END dias_atraso,
       CASE WHEN c.dias_para_recebimento IS NULL THEN 'SEM_MARCO'
            WHEN c.dias_para_recebimento<0 THEN 'DATA_FUTURA'
            WHEN c.dias_para_recebimento>c.prazo_recebimento_dias THEN 'EM_ATRASO' ELSE 'NO_PRAZO' END status_atraso,
       CASE WHEN c.dias_para_recebimento IS NULL THEN 'SEM_MARCO'
            WHEN c.dias_para_recebimento<0 THEN 'DATA_FUTURA'
            WHEN c.dias_para_recebimento<=c.prazo_recebimento_dias THEN 'NO_PRAZO'
            WHEN c.dias_para_recebimento-c.prazo_recebimento_dias<=3 THEN '1-3_DIAS'
            WHEN c.dias_para_recebimento-c.prazo_recebimento_dias<=7 THEN '4-7_DIAS'
            WHEN c.dias_para_recebimento-c.prazo_recebimento_dias<=30 THEN '8-30_DIAS' ELSE '31+_DIAS' END faixa_atraso,
       c.subprefeitura,c.distrito,c.natureza_referencia_territorial,c.entrega_completa,c.qc_resultado
FROM calc c;
GO
CREATE OR ALTER VIEW serving.v_bi_catalogo AS
SELECT tr.natureza,tr.codigo,tr.nome,g.codigo gestor,trv.versao,trv.vigencia_inicio,trv.vigencia_fim,trv.tipo_medida,
       trv.regime_vigencia,trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,
       trv.monitorar_atraso,trv.prazo_recebimento_dias,trv.marco_atraso_codigo,trv.qc_status,trv.status
FROM ref.tipo_registro tr JOIN ref.gestor g ON g.gestor_id=tr.gestor_id JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.lote') AND name='IX_ingestao_lote_status')
 CREATE INDEX IX_ingestao_lote_status ON ingestao.lote(status,criado_em);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.entrega') AND name='IX_ingestao_entrega_status')
 CREATE INDEX IX_ingestao_entrega_status ON ingestao.entrega(status,recebido_em);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.pessoa_observacao') AND name='IX_silver_pessoa_observacao_cpf')
 CREATE INDEX IX_silver_pessoa_observacao_cpf ON silver.pessoa_observacao(cpf) WHERE cpf IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.pessoa_observacao') AND name='IX_silver_pessoa_observacao_gestor')
 CREATE INDEX IX_silver_pessoa_observacao_gestor ON silver.pessoa_observacao(gestor_id,pessoa_observacao_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('serving.registro_integrado') AND name='IX_serving_registro_contexto_pessoa')
 CREATE INDEX IX_serving_registro_contexto_pessoa ON serving.registro_integrado(gestor_id,tipo_registro_id,pessoa_uuid) INCLUDE(tipo_registro_versao_id,natureza);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.pessoa_atributo_observacao') AND name='IX_silver_pessoa_atributo')
 CREATE INDEX IX_silver_pessoa_atributo ON silver.pessoa_atributo_observacao(pessoa_observacao_id,atributo_codigo,status_evidencia,referencia_evidencia DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('silver.registro_observacao') AND name='IX_silver_registro_tipo_pessoa')
 CREATE INDEX IX_silver_registro_tipo_pessoa ON silver.registro_observacao(tipo_registro_id,pessoa_observacao_id,source_as_of DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('serving.registro_integrado') AND name='IX_serving_registro_tipo_entrega')
 CREATE INDEX IX_serving_registro_tipo_entrega ON serving.registro_integrado(tipo_registro_versao_id,entrega_id) INCLUDE(natureza,gestor_id,source_as_of,data_evento_concessao,data_inicio_concessao,data_fim_concessao,data_hora_servico,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,referencia_territorial_observacao_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='IX_identidade_vinculo_fonte_uuid_ativo')
 CREATE INDEX IX_identidade_vinculo_fonte_uuid_ativo ON identidade.vinculo_fonte(pessoa_uuid) WHERE pessoa_uuid IS NOT NULL AND ativo=1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='IX_identidade_vinculo_fonte_pendente')
 CREATE INDEX IX_identidade_vinculo_fonte_pendente ON identidade.vinculo_fonte(pessoa_observacao_id,status,metodo_resolucao) WHERE ativo=1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_run') AND name='IX_identidade_linkage_run_modelo_inicio')
 CREATE INDEX IX_identidade_linkage_run_modelo_inicio ON identidade.linkage_run(modelo_id,iniciado_em DESC) INCLUDE(status,tipo_run,avaliados,resolvidos,nao_resolvidos,conflitos);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_resultado') AND name='IX_identidade_linkage_resultado_run_observacao')
 CREATE INDEX IX_identidade_linkage_resultado_run_observacao ON identidade.linkage_resultado(linkage_run_id,pessoa_observacao_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_resultado') AND name='IX_identidade_linkage_resultado_observacao_modelo')
 CREATE INDEX IX_identidade_linkage_resultado_observacao_modelo ON identidade.linkage_resultado(pessoa_observacao_id,modelo_id,calculado_em DESC) INCLUDE(status,score_melhor,pessoa_uuid_resolvido);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_run') AND name='IX_identidade_linkage_run_status_publicacao')
 CREATE INDEX IX_identidade_linkage_run_status_publicacao ON identidade.linkage_run(status,publicado_em DESC,linkage_run_id) INCLUDE(modelo_id,modelo_versao,tipo_run);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_resultado') AND name='IX_identidade_linkage_resultado_corrente')
 CREATE INDEX IX_identidade_linkage_resultado_corrente ON identidade.linkage_resultado(pessoa_observacao_id,linkage_run_id) INCLUDE(pessoa_uuid_resolvido,score_melhor,status,modelo_id,calculado_em);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.linkage_run_item') AND name='IX_identidade_linkage_run_item_observacao')
 CREATE INDEX IX_identidade_linkage_run_item_observacao ON identidade.linkage_run_item(pessoa_observacao_id,linkage_run_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.estatistica_linkage') AND name='IX_identidade_estatistica_linkage_modelo_nome')
 CREATE INDEX IX_identidade_estatistica_linkage_modelo_nome ON identidade.estatistica_linkage(modelo_id,nome);
GO

/* ============================================================================
   v3.44/v3.45 — Integridade governada de identidade + Gold factual independente
   da atribuição canônica. Migração aditiva/override idempotente.
   ============================================================================ */
GO
-- v3.44: estado de Pessoa em conflito governado e precedência de decisão institucional.
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.pessoa') AND name='ck_identidade_pessoa_status')
 ALTER TABLE identidade.pessoa DROP CONSTRAINT ck_identidade_pessoa_status;
ALTER TABLE identidade.pessoa WITH CHECK ADD CONSTRAINT ck_identidade_pessoa_status
 CHECK(status IN('ATIVO','EM_CONFLITO','FUNDIDO','SEPARADO','INATIVO'));
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_metodo')
 ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_metodo;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_modelo')
 ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_modelo;
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_metodo
 CHECK(metodo_resolucao IN('CPF_DETERMINISTICO','PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'));
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao='CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao='LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao='CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL) OR
    (metodo_resolucao='CONFLITO_GOVERNADO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL));
GO
CREATE OR ALTER VIEW identidade.v_vinculo_corrente AS
WITH probabilistico_publicado AS (
    SELECT r.pessoa_observacao_id,r.pessoa_uuid_resolvido,r.score_melhor,r.status,r.motivo,r.modelo_id,r.linkage_run_id,r.calculado_em,
           ROW_NUMBER() OVER(PARTITION BY r.pessoa_observacao_id ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) rn
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
    WHERE lr.status='PUBLICADO'
), prob_corrente AS (SELECT * FROM probabilistico_publicado WHERE rn=1),
base_ativa AS (SELECT vf.* FROM identidade.vinculo_fonte vf WHERE vf.ativo=1)
SELECT b.vinculo_id,b.pessoa_observacao_id,b.pessoa_uuid,b.metodo_resolucao,b.score,b.status,b.motivo,b.modelo_id,b.linkage_run_id,b.resolvido_em
FROM base_ativa b
WHERE b.metodo_resolucao IN('CPF_DETERMINISTICO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO')
   OR NOT EXISTS (SELECT 1 FROM prob_corrente p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id)
UNION ALL
SELECT CAST(NULL AS BIGINT),p.pessoa_observacao_id,p.pessoa_uuid_resolvido,'LINKAGE_PROBABILISTICO',p.score_melhor,p.status,p.motivo,p.modelo_id,p.linkage_run_id,p.calculado_em
FROM prob_corrente p
WHERE NOT EXISTS (
    SELECT 1 FROM base_ativa b WHERE b.pessoa_observacao_id=p.pessoa_observacao_id
      AND b.metodo_resolucao IN('CPF_DETERMINISTICO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'));
GO

-- v3.45: sujeito declarado do fato. O código de origem é opaco; formato semelhante a CPF nunca é inferido como CPF.
IF COL_LENGTH('silver.registro_observacao','sujeito_declarado_hash') IS NULL
 ALTER TABLE silver.registro_observacao ADD sujeito_declarado_hash CHAR(64) NULL;
GO
DECLARE @t NVARCHAR(128);
DECLARE fact_tables CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM (VALUES('gold.beneficio_concedido'),('gold.servico_prestado'),('serving.registro_integrado'))x(name);
OPEN fact_tables; FETCH NEXT FROM fact_tables INTO @t;
WHILE @@FETCH_STATUS=0
BEGIN
 IF COL_LENGTH(@t,'pessoa_origem_id') IS NULL EXEC('ALTER TABLE '+@t+' ADD pessoa_origem_id BIGINT NULL');
 IF COL_LENGTH(@t,'sistema_origem_id') IS NULL EXEC('ALTER TABLE '+@t+' ADD sistema_origem_id BIGINT NULL');
 IF COL_LENGTH(@t,'codigo_pessoa_origem') IS NULL EXEC('ALTER TABLE '+@t+' ADD codigo_pessoa_origem NVARCHAR(255) NULL');
 IF COL_LENGTH(@t,'cpf_declarado') IS NULL EXEC('ALTER TABLE '+@t+' ADD cpf_declarado CHAR(11) NULL');
 IF COL_LENGTH(@t,'cpf_ausente_motivo') IS NULL EXEC('ALTER TABLE '+@t+' ADD cpf_ausente_motivo NVARCHAR(30) NULL');
 IF COL_LENGTH(@t,'estado_atribuicao_identidade') IS NULL EXEC('ALTER TABLE '+@t+' ADD estado_atribuicao_identidade NVARCHAR(30) NULL');
 FETCH NEXT FROM fact_tables INTO @t;
END
CLOSE fact_tables; DEALLOCATE fact_tables;
GO
-- Backfill para instalações anteriores. O snapshot vem da pessoa_observacao historicamente ligada à versão do fato.
UPDATE b SET pessoa_origem_id=po.pessoa_origem_id,sistema_origem_id=pori.sistema_origem_id,codigo_pessoa_origem=po.codigo_pessoa_origem,
             cpf_declarado=po.cpf,cpf_ausente_motivo=po.cpf_ausente_motivo,
             estado_atribuicao_identidade=CASE WHEN b.pessoa_uuid IS NULL THEN 'PENDENTE_IDENTIDADE' ELSE 'ATRIBUIDA' END
FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
WHERE b.pessoa_origem_id IS NULL;
UPDATE s SET pessoa_origem_id=po.pessoa_origem_id,sistema_origem_id=pori.sistema_origem_id,codigo_pessoa_origem=po.codigo_pessoa_origem,
             cpf_declarado=po.cpf,cpf_ausente_motivo=po.cpf_ausente_motivo,
             estado_atribuicao_identidade=CASE WHEN s.pessoa_uuid IS NULL THEN 'PENDENTE_IDENTIDADE' ELSE 'ATRIBUIDA' END
FROM gold.servico_prestado s JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
WHERE s.pessoa_origem_id IS NULL;
UPDATE ri SET pessoa_origem_id=po.pessoa_origem_id,sistema_origem_id=pori.sistema_origem_id,codigo_pessoa_origem=po.codigo_pessoa_origem,
              cpf_declarado=po.cpf,cpf_ausente_motivo=po.cpf_ausente_motivo,
              estado_atribuicao_identidade=CASE WHEN ri.pessoa_uuid IS NULL THEN 'PENDENTE_IDENTIDADE' ELSE 'ATRIBUIDA' END
FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
WHERE ri.pessoa_origem_id IS NULL;
GO
-- A atribuição canônica é projeção mutável; a declaração de origem permanece obrigatória e imutável por versão factual.
ALTER TABLE gold.beneficio_concedido ALTER COLUMN pessoa_uuid UNIQUEIDENTIFIER NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN pessoa_uuid UNIQUEIDENTIFIER NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN pessoa_uuid UNIQUEIDENTIFIER NULL;
ALTER TABLE gold.beneficio_concedido ALTER COLUMN pessoa_origem_id BIGINT NOT NULL;
ALTER TABLE gold.beneficio_concedido ALTER COLUMN sistema_origem_id BIGINT NOT NULL;
ALTER TABLE gold.beneficio_concedido ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NOT NULL;
ALTER TABLE gold.beneficio_concedido ALTER COLUMN estado_atribuicao_identidade NVARCHAR(30) NOT NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN pessoa_origem_id BIGINT NOT NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN sistema_origem_id BIGINT NOT NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NOT NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN estado_atribuicao_identidade NVARCHAR(30) NOT NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN pessoa_origem_id BIGINT NOT NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN sistema_origem_id BIGINT NOT NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NOT NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN estado_atribuicao_identidade NVARCHAR(30) NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('gold.beneficio_concedido') AND name='fk_beneficio_pessoa_origem')
 ALTER TABLE gold.beneficio_concedido ADD CONSTRAINT fk_beneficio_pessoa_origem FOREIGN KEY(pessoa_origem_id) REFERENCES silver.pessoa_origem(pessoa_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('gold.beneficio_concedido') AND name='fk_beneficio_sistema_origem')
 ALTER TABLE gold.beneficio_concedido ADD CONSTRAINT fk_beneficio_sistema_origem FOREIGN KEY(sistema_origem_id) REFERENCES ref.sistema_origem(sistema_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('gold.servico_prestado') AND name='fk_servico_pessoa_origem')
 ALTER TABLE gold.servico_prestado ADD CONSTRAINT fk_servico_pessoa_origem FOREIGN KEY(pessoa_origem_id) REFERENCES silver.pessoa_origem(pessoa_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('gold.servico_prestado') AND name='fk_servico_sistema_origem')
 ALTER TABLE gold.servico_prestado ADD CONSTRAINT fk_servico_sistema_origem FOREIGN KEY(sistema_origem_id) REFERENCES ref.sistema_origem(sistema_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('serving.registro_integrado') AND name='fk_serving_pessoa_origem')
 ALTER TABLE serving.registro_integrado ADD CONSTRAINT fk_serving_pessoa_origem FOREIGN KEY(pessoa_origem_id) REFERENCES silver.pessoa_origem(pessoa_origem_id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('serving.registro_integrado') AND name='fk_serving_sistema_origem')
 ALTER TABLE serving.registro_integrado ADD CONSTRAINT fk_serving_sistema_origem FOREIGN KEY(sistema_origem_id) REFERENCES ref.sistema_origem(sistema_origem_id);
GO
DECLARE @c NVARCHAR(128);
DECLARE checks CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM (VALUES('gold.beneficio_concedido'),('gold.servico_prestado'),('serving.registro_integrado'))x(name);
OPEN checks; FETCH NEXT FROM checks INTO @c;
WHILE @@FETCH_STATUS=0
BEGIN
 DECLARE @n sysname=CASE @c WHEN 'gold.beneficio_concedido' THEN 'ck_beneficio_atribuicao_identidade' WHEN 'gold.servico_prestado' THEN 'ck_servico_atribuicao_identidade' ELSE 'ck_serving_atribuicao_identidade' END;
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(@c) AND name=@n)
 BEGIN
  DECLARE @sql_add_identity_check NVARCHAR(MAX)=N'ALTER TABLE '+@c+N' ADD CONSTRAINT '+QUOTENAME(@n)+N' CHECK(estado_atribuicao_identidade IN(''ATRIBUIDA'',''PENDENTE_IDENTIDADE'',''CONFLITO_IDENTIDADE'') AND ((estado_atribuicao_identidade=''ATRIBUIDA'' AND pessoa_uuid IS NOT NULL) OR (estado_atribuicao_identidade IN(''PENDENTE_IDENTIDADE'',''CONFLITO_IDENTIDADE'') AND pessoa_uuid IS NULL)))';
  EXEC sys.sp_executesql @sql_add_identity_check;
 END;
 FETCH NEXT FROM checks INTO @c;
END
CLOSE checks; DEALLOCATE checks;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.beneficio_concedido') AND name='IX_beneficio_cpf_declarado')
 CREATE INDEX IX_beneficio_cpf_declarado ON gold.beneficio_concedido(cpf_declarado,status_analitico) INCLUDE(estado_atribuicao_identidade,gestor_id) WHERE cpf_declarado IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('gold.servico_prestado') AND name='IX_servico_cpf_declarado')
 CREATE INDEX IX_servico_cpf_declarado ON gold.servico_prestado(cpf_declarado,status_analitico) INCLUDE(estado_atribuicao_identidade,gestor_id) WHERE cpf_declarado IS NOT NULL;
GO

-- Divergência institucional: indício enviado ao Gestor; a finalística mantém responsabilidade pelo saneamento e desfecho.
IF OBJECT_ID('qualidade.divergencia_gestor','U') IS NULL CREATE TABLE qualidade.divergencia_gestor(
 divergencia_id BIGINT IDENTITY PRIMARY KEY,
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 tipo NVARCHAR(50) NOT NULL,
 motivo NVARCHAR(120) NOT NULL,
 pessoa_observacao_id BIGINT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 registro_observacao_id BIGINT NULL REFERENCES silver.registro_observacao(registro_observacao_id),
 codigo_pessoa_origem NVARCHAR(255) NULL,
 status NVARCHAR(20) NOT NULL DEFAULT('ABERTA'),
 desfecho NVARCHAR(80) NULL, observacao_desfecho NVARCHAR(2000) NULL,
 correlation_id UNIQUEIDENTIFIER NULL,
 aberta_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()), encerrada_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_divergencia_gestor_status CHECK(status IN('ABERTA','RESOLVIDA','DESCARTADA')),
 CONSTRAINT ck_divergencia_gestor_datas CHECK(encerrada_em IS NULL OR encerrada_em>=aberta_em));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('qualidade.divergencia_gestor') AND name='IX_divergencia_gestor_fila')
 CREATE INDEX IX_divergencia_gestor_fila ON qualidade.divergencia_gestor(gestor_id,status,aberta_em DESC);
GO

-- v3.44: caso governado geral, independente de CPF, para fusão histórica/linkage incorreto/outro indício.
IF OBJECT_ID('identidade.caso_conflito_identidade','U') IS NULL CREATE TABLE identidade.caso_conflito_identidade(
 caso_id UNIQUEIDENTIFIER PRIMARY KEY,
 gestor_responsavel_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 motivo NVARCHAR(50) NOT NULL,
 ato_referencia NVARCHAR(300) NOT NULL, justificativa NVARCHAR(2000) NOT NULL,
 correlation_id UNIQUEIDENTIFIER NULL, status NVARCHAR(20) NOT NULL,
 aberto_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()), aplicado_em DATETIMEOFFSET(7) NULL,
 CONSTRAINT ck_caso_conflito_motivo CHECK(motivo IN('CPF_COMPARTILHADO','FUSAO_HISTORICA','LINKAGE_INCORRETO','OUTRO')),
 CONSTRAINT ck_caso_conflito_status CHECK(status IN('ABERTO','APLICADO','CANCELADO')));
IF OBJECT_ID('identidade.caso_conflito_identidade_item','U') IS NULL CREATE TABLE identidade.caso_conflito_identidade_item(
 caso_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.caso_conflito_identidade(caso_id),
 pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
 pessoa_uuid_anterior UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
 CONSTRAINT pk_caso_conflito_item PRIMARY KEY(caso_id,pessoa_observacao_id));
GO
CREATE OR ALTER PROCEDURE identidade.sp_abrir_caso_conflito_identidade
 @gestor_codigo NVARCHAR(30),@motivo NVARCHAR(50),@observacoes_json NVARCHAR(MAX),@ato_referencia NVARCHAR(300),@justificativa NVARCHAR(2000),@correlation_id UNIQUEIDENTIFIER=NULL,@caso_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISJSON(@observacoes_json)<>1 THROW 51100,'Observações devem ser JSON válido.',1;
 IF @motivo NOT IN('CPF_COMPARTILHADO','FUSAO_HISTORICA','LINKAGE_INCORRETO','OUTRO') THROW 51101,'Motivo de conflito governado inválido.',1;
 DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor_codigo AND ativo=1);
 IF @gestor IS NULL THROW 51102,'Gestor responsável inexistente/inativo.',1;
 DECLARE @o TABLE(obs BIGINT PRIMARY KEY,uuid UNIQUEIDENTIFIER NULL);
 INSERT @o(obs,uuid)
 SELECT j.obs,vc.pessoa_uuid FROM OPENJSON(@observacoes_json) WITH(obs BIGINT '$') j
 JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=j.obs
 LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=j.obs;
 IF NOT EXISTS(SELECT 1 FROM @o) THROW 51103,'Caso governado exige ao menos uma observação existente.',1;
 IF (SELECT COUNT(*) FROM @o)<>(SELECT COUNT(*) FROM OPENJSON(@observacoes_json)) THROW 51104,'Observação inexistente/duplicada no caso governado.',1;
 SET @caso_id=NEWID();
 INSERT identidade.caso_conflito_identidade(caso_id,gestor_responsavel_id,motivo,ato_referencia,justificativa,correlation_id,status)
 VALUES(@caso_id,@gestor,@motivo,@ato_referencia,@justificativa,@correlation_id,'ABERTO');
 INSERT identidade.caso_conflito_identidade_item(caso_id,pessoa_observacao_id,pessoa_uuid_anterior) SELECT @caso_id,obs,uuid FROM @o;
 UPDATE vf SET ativo=0 FROM identidade.vinculo_fonte vf JOIN @o o ON o.obs=vf.pessoa_observacao_id WHERE vf.ativo=1;
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 SELECT obs,NULL,'CONFLITO_GOVERNADO',NULL,'CONFLITO',NULL,1,NULL,@motivo FROM @o;
 UPDATE p SET status='EM_CONFLITO' FROM identidade.pessoa p WHERE p.pessoa_uuid IN(SELECT uuid FROM @o WHERE uuid IS NOT NULL) AND p.status='ATIVO';
 UPDATE im SET estado='EM_CONFLITO',estado_motivo='CONFLITO_GOVERNADO',estado_em=SYSDATETIMEOFFSET()
 WHERE vigencia_fim IS NULL AND estado='ATIVO' AND pessoa_uuid IN(SELECT uuid FROM @o WHERE uuid IS NOT NULL);
 UPDATE b SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id WHERE b.status_analitico='VIGENTE';
 UPDATE s SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.servico_prestado s JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id WHERE s.status_analitico='VIGENTE';
 UPDATE ri SET pessoa_uuid=NULL,estado_atribuicao_identidade='CONFLITO_IDENTIDADE',atualizado_em=SYSDATETIMEOFFSET()
 FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id WHERE ri.status_analitico='VIGENTE';
 DELETE gp FROM gold.pessoa gp JOIN identidade.pessoa p ON p.pessoa_uuid=gp.pessoa_uuid WHERE p.status='EM_CONFLITO';
END;
GO

-- Sincronização factual é derivada do vínculo corrente; nunca altera o sujeito declarado histórico.
CREATE OR ALTER PROCEDURE identidade.sp_sincronizar_atribuicao_fatos @pessoa_observacao_id BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @uuid UNIQUEIDENTIFIER=NULL,@status NVARCHAR(30)=NULL;
 SELECT TOP(1) @uuid=pessoa_uuid,@status=status FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id=@pessoa_observacao_id;
 DECLARE @estado NVARCHAR(30)=CASE WHEN @status='RESOLVIDO' AND @uuid IS NOT NULL THEN 'ATRIBUIDA' WHEN @status='CONFLITO' THEN 'CONFLITO_IDENTIDADE' ELSE 'PENDENTE_IDENTIDADE' END;
 UPDATE b SET pessoa_uuid=CASE WHEN @estado='ATRIBUIDA' THEN @uuid ELSE NULL END,estado_atribuicao_identidade=@estado,atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id WHERE ro.pessoa_observacao_id=@pessoa_observacao_id;
 UPDATE s SET pessoa_uuid=CASE WHEN @estado='ATRIBUIDA' THEN @uuid ELSE NULL END,estado_atribuicao_identidade=@estado,atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.servico_prestado s JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id WHERE ro.pessoa_observacao_id=@pessoa_observacao_id;
 UPDATE ri SET pessoa_uuid=CASE WHEN @estado='ATRIBUIDA' THEN @uuid ELSE NULL END,estado_atribuicao_identidade=@estado,atualizado_em=SYSDATETIMEOFFSET()
 FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id WHERE ro.pessoa_observacao_id=@pessoa_observacao_id;
END;
GO

-- Projeções individuais: somente fatos inequivocamente atribuídos. Agregados factuais abaixo incluem todos os fatos vigentes.
CREATE OR ALTER VIEW gold.v_registros_jornada AS
SELECT CONCAT('B:',b.beneficio_concedido_id) registro_id,b.pessoa_uuid,CAST('BENEFICIO' AS NVARCHAR(30)) natureza,
       g.codigo gestor,tr.codigo codigo_tipo,tr.nome nome_tipo,COALESCE(CAST(b.data_evento_concessao AS DATETIMEOFFSET),CAST(b.data_inicio_concessao AS DATETIMEOFFSET),b.source_as_of) ocorrido_em,
       b.source_as_of data_referencia,b.situacao,b.registro_observacao_id source_observacao_id
FROM gold.beneficio_concedido b JOIN ref.tipo_registro tr ON tr.tipo_registro_id=b.tipo_registro_id JOIN ref.gestor g ON g.gestor_id=b.gestor_id
WHERE b.status_analitico='VIGENTE' AND b.estado_atribuicao_identidade='ATRIBUIDA'
UNION ALL
SELECT CONCAT('S:',s.servico_prestado_id),s.pessoa_uuid,CAST('SERVICO' AS NVARCHAR(30)),g.codigo,tr.codigo,tr.nome,s.data_hora_servico,s.source_as_of,s.situacao,s.registro_observacao_id
FROM gold.servico_prestado s JOIN ref.tipo_registro tr ON tr.tipo_registro_id=s.tipo_registro_id JOIN ref.gestor g ON g.gestor_id=s.gestor_id
WHERE s.status_analitico='VIGENTE' AND s.estado_atribuicao_identidade='ATRIBUIDA';
GO
CREATE OR ALTER VIEW serving.v_beneficios_concedidos_pessoa AS
SELECT bc.beneficio_concedido_id,bc.pessoa_uuid,g.codigo gestor,tr.codigo beneficio,tr.nome nome_beneficio,
       trv.versao versao_tipo,bc.versao_interna,bc.operacao,bc.status_analitico,bc.codigo_registro_origem,trv.tipo_medida,
       bc.data_inicio_concessao,bc.data_fim_concessao,bc.data_evento_concessao,bc.situacao,bc.valor_concedido,bc.quantidade,bc.unidade unidade_medida,bc.source_as_of data_referencia,bc.qc_resultado,COALESCE(bc.qc_especifico_implementado,0) qc_especifico_implementado
FROM gold.beneficio_concedido bc JOIN ref.tipo_registro tr ON tr.tipo_registro_id=bc.tipo_registro_id JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=bc.tipo_registro_versao_id JOIN ref.gestor g ON g.gestor_id=bc.gestor_id
WHERE bc.status_analitico='VIGENTE' AND bc.estado_atribuicao_identidade='ATRIBUIDA';
GO
CREATE OR ALTER VIEW serving.v_servicos_prestados_pessoa AS
SELECT sp.servico_prestado_id,sp.pessoa_uuid,g.codigo gestor,tr.codigo servico,tr.nome nome_servico,trv.versao versao_tipo,sp.versao_interna,sp.operacao,sp.status_analitico,sp.codigo_registro_origem,
       sp.data_hora_servico,sp.unidade_servico,sp.situacao,sp.source_as_of data_referencia
FROM gold.servico_prestado sp JOIN ref.tipo_registro tr ON tr.tipo_registro_id=sp.tipo_registro_id JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=sp.tipo_registro_versao_id JOIN ref.gestor g ON g.gestor_id=sp.gestor_id
WHERE sp.status_analitico='VIGENTE' AND sp.estado_atribuicao_identidade='ATRIBUIDA';
GO
CREATE OR ALTER VIEW serving.v_pessoa AS
SELECT gp.pessoa_uuid,gp.cpf,gp.status_cpf,gp.nome_completo,gp.data_nascimento,gp.nome_mae,gp.fontes_distintas,gp.estado_concordancia,gp.atualizado_em
FROM gold.pessoa gp JOIN identidade.pessoa p ON p.pessoa_uuid=gp.pessoa_uuid WHERE p.status='ATIVO';
GO
-- v3.46: v_bi_fatos_identidade removida; o estado de atribuição pertence às views factuais.
CREATE OR ALTER VIEW serving.v_bi_beneficios_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(30)) gestor,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
UNION ALL
SELECT 'GESTOR',g.codigo,COUNT_BIG(DISTINCT ri.pessoa_uuid) FROM serving.registro_integrado ri JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA' GROUP BY g.codigo;
GO
CREATE OR ALTER VIEW serving.v_bi_servicos_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(200)) nome_servico,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
UNION ALL
SELECT 'SERVICO',tr.nome,COUNT_BIG(DISTINCT ri.pessoa_uuid) FROM serving.registro_integrado ri JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA' GROUP BY tr.nome;
GO
CREATE OR ALTER VIEW serving.v_bi_registros_pessoas AS SELECT COUNT_BIG(DISTINCT pessoa_uuid) pessoas FROM serving.registro_integrado WHERE status_analitico='VIGENTE' AND estado_atribuicao_identidade='ATRIBUIDA';
GO

-- ============================================================================
-- v3.45 FINAL - fechamento do ciclo fato x identidade e correção governada
-- ============================================================================
-- identidade.pessoa pode ficar temporariamente EM_CONFLITO durante um caso governado.
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.pessoa') AND name='ck_identidade_pessoa_status')
 ALTER TABLE identidade.pessoa DROP CONSTRAINT ck_identidade_pessoa_status;
ALTER TABLE identidade.pessoa WITH CHECK ADD CONSTRAINT ck_identidade_pessoa_status
 CHECK(status IN('ATIVO','EM_CONFLITO','FUNDIDO','SEPARADO','INATIVO'));
GO

-- Recompõe o núcleo Gold exclusivamente a partir dos vínculos correntes resolvidos.
-- UUID EM_CONFLITO/SEPARADO não permanece publicado.
CREATE OR ALTER PROCEDURE identidade.sp_recompor_gold_pessoa @pessoa_uuid UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@pessoa_uuid AND status='ATIVO')
 BEGIN
   DELETE FROM gold.pessoa WHERE pessoa_uuid=@pessoa_uuid;
   RETURN;
 END;
 ;WITH obs AS(
    SELECT po.*
    FROM silver.pessoa_observacao po
    JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
    WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO'
 ), stats AS(
    SELECT COUNT(DISTINCT gestor_id) fontes,
           CASE WHEN COUNT(DISTINCT CONCAT(nome_cmp,'|',CONVERT(char(10),data_nascimento,23),'|',nome_mae_cmp))>1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END divergente
    FROM obs
 )
 MERGE gold.pessoa AS t
 USING(
    SELECT @pessoa_uuid pessoa_uuid,
           COALESCE((SELECT TOP(1) identificador FROM identidade.identity_map WHERE pessoa_uuid=@pessoa_uuid AND tipo='CPF' AND vigencia_fim IS NULL AND estado='ATIVO' ORDER BY vigencia_inicio DESC),cpf_src.cpf) cpf,
           nome_src.nome_completo,nasc_src.data_nascimento,mae_src.nome_mae,st.fontes,st.divergente,cpf_src.cpf_ausente_motivo
    FROM stats st
    OUTER APPLY(SELECT TOP(1) o.cpf,o.cpf_ausente_motivo FROM obs o LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='CPF' ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC) cpf_src
    OUTER APPLY(SELECT TOP(1) o.nome_completo FROM obs o LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='NOME_COMPLETO' ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC) nome_src
    OUTER APPLY(SELECT TOP(1) o.data_nascimento FROM obs o LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='DATA_NASCIMENTO' ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC) nasc_src
    OUTER APPLY(SELECT TOP(1) o.nome_mae FROM obs o LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='NOME_MAE' ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC) mae_src
    WHERE nome_src.nome_completo IS NOT NULL AND nasc_src.data_nascimento IS NOT NULL AND mae_src.nome_mae IS NOT NULL
 ) s ON t.pessoa_uuid=s.pessoa_uuid
 WHEN MATCHED THEN UPDATE SET cpf=s.cpf,status_cpf=CASE WHEN s.cpf IS NOT NULL THEN 'PRESENTE' WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO' ELSE 'SEM_CPF' END,
      nome_completo=s.nome_completo,data_nascimento=s.data_nascimento,nome_mae=s.nome_mae,fontes_distintas=s.fontes,
      estado_concordancia=CASE WHEN s.divergente=1 THEN 'DIVERGENTE' WHEN s.fontes>1 THEN 'CORROBORADO' ELSE 'BASELINE_FONTE_UNICA' END,atualizado_em=SYSDATETIMEOFFSET()
 WHEN NOT MATCHED THEN INSERT(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
      VALUES(s.pessoa_uuid,s.cpf,CASE WHEN s.cpf IS NOT NULL THEN 'PRESENTE' WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO' ELSE 'SEM_CPF' END,
             s.nome_completo,s.data_nascimento,s.nome_mae,s.fontes,CASE WHEN s.divergente=1 THEN 'DIVERGENTE' WHEN s.fontes>1 THEN 'CORROBORADO' ELSE 'BASELINE_FONTE_UNICA' END,SYSDATETIMEOFFSET());
 IF NOT EXISTS(SELECT 1 FROM obs) DELETE FROM gold.pessoa WHERE pessoa_uuid=@pessoa_uuid;
END;
GO

-- Aplica caso governado geral (fusão histórica, linkage incorreto ou outro motivo),
-- sem depender de CPF. O ato informa explicitamente o agrupamento; a Jornada não infere.
CREATE OR ALTER PROCEDURE identidade.sp_aplicar_caso_conflito_identidade
 @gestor_codigo NVARCHAR(30),@caso_id UNIQUEIDENTIFIER,@grupos_json NVARCHAR(MAX),@correlation_id UNIQUEIDENTIFIER=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISJSON(@grupos_json)<>1 THROW 51110,'Grupos do caso devem ser JSON válido.',1;
 DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor_codigo AND ativo=1);
 IF @gestor IS NULL THROW 51111,'Gestor responsável inexistente/inativo.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.caso_conflito_identidade WITH(UPDLOCK,HOLDLOCK) WHERE caso_id=@caso_id AND gestor_responsavel_id=@gestor AND status='ABERTO')
   THROW 51112,'Caso inexistente, não pertence ao Gestor ou não está ABERTO.',1;
 DECLARE @g TABLE(grupo NVARCHAR(80) PRIMARY KEY,dest UNIQUEIDENTIFIER NOT NULL);
 INSERT @g(grupo,dest)
 SELECT grupoCodigo,COALESCE(TRY_CONVERT(uniqueidentifier,pessoaUuidDestino),NEWID())
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaUuidDestino NVARCHAR(40) '$.pessoaUuidDestino');
 IF NOT EXISTS(SELECT 1 FROM @g) OR EXISTS(SELECT 1 FROM @g WHERE NULLIF(LTRIM(RTRIM(grupo)),'') IS NULL) THROW 51113,'Grupo inválido.',1;
 DECLARE @o TABLE(obs BIGINT PRIMARY KEY,grupo NVARCHAR(80) NOT NULL,dest UNIQUEIDENTIFIER NOT NULL,uuid_ant UNIQUEIDENTIFIER NULL);
 INSERT @o(obs,grupo,dest,uuid_ant)
 SELECT x.pessoaObservacaoId,g.grupo,g.dest,ci.pessoa_uuid_anterior
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) j
 JOIN @g g ON g.grupo=j.grupoCodigo
 CROSS APPLY OPENJSON(j.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') x
 JOIN identidade.caso_conflito_identidade_item ci ON ci.caso_id=@caso_id AND ci.pessoa_observacao_id=x.pessoaObservacaoId;
 IF EXISTS(SELECT pessoaObservacaoId FROM OPENJSON(@grupos_json) WITH(pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) j CROSS APPLY OPENJSON(j.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') x GROUP BY pessoaObservacaoId HAVING COUNT(*)>1)
   THROW 51114,'Uma observação não pode pertencer a mais de um grupo.',1;
 IF (SELECT COUNT(*) FROM @o)<>(SELECT COUNT(*) FROM identidade.caso_conflito_identidade_item WHERE caso_id=@caso_id)
   THROW 51115,'A correção deve classificar todas as observações do caso.',1;
 IF EXISTS(SELECT 1 FROM @g g WHERE NOT EXISTS(SELECT 1 FROM @o o WHERE o.grupo=g.grupo)) THROW 51116,'Grupo sem observações.',1;
 IF EXISTS(SELECT 1 FROM @g g WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=g.dest))
   INSERT identidade.pessoa(pessoa_uuid,status) SELECT g.dest,'ATIVO' FROM @g g WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=g.dest);
 UPDATE p SET status='ATIVO' FROM identidade.pessoa p JOIN @g g ON g.dest=p.pessoa_uuid;
 UPDATE vf SET ativo=0 FROM identidade.vinculo_fonte vf JOIN @o o ON o.obs=vf.pessoa_observacao_id WHERE vf.ativo=1;
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 SELECT obs,dest,'CORRECAO_GOVERNADA',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CASO_GOVERNADO_APLICADO' FROM @o;
 EXEC sys.sp_set_session_context @key=N'jornada_identity_case',@value=@caso_id;
 UPDATE b SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE s SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=SYSDATETIMEOFFSET()
 FROM gold.servico_prestado s JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE ri SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=SYSDATETIMEOFFSET()
 FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 DECLARE @afetados TABLE(uuid UNIQUEIDENTIFIER PRIMARY KEY);
 INSERT @afetados SELECT DISTINCT dest FROM @o UNION SELECT DISTINCT uuid_ant FROM @o WHERE uuid_ant IS NOT NULL;
 UPDATE p SET status='SEPARADO' FROM identidade.pessoa p JOIN @afetados a ON a.uuid=p.pessoa_uuid
 WHERE NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente v WHERE v.pessoa_uuid=p.pessoa_uuid AND v.status='RESOLVIDO')
   AND NOT EXISTS(SELECT 1 FROM identidade.identity_map m WHERE m.pessoa_uuid=p.pessoa_uuid AND m.vigencia_fim IS NULL AND m.estado='ATIVO');
 DELETE gp FROM gold.pessoa gp JOIN identidade.pessoa p ON p.pessoa_uuid=gp.pessoa_uuid WHERE p.status<>'ATIVO';
 DECLARE @u UNIQUEIDENTIFIER; DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT uuid FROM @afetados;
 OPEN c; FETCH NEXT FROM c INTO @u; WHILE @@FETCH_STATUS=0 BEGIN EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@u; FETCH NEXT FROM c INTO @u; END CLOSE c; DEALLOCATE c;
 DELETE ap FROM qualidade.avaliacao_possibilidade ap JOIN @afetados a ON a.uuid=ap.pessoa_uuid;
 UPDATE d SET status='RESOLVIDA',desfecho='CORRECAO_GOVERNADA',observacao_desfecho='Resolvida por caso governado de identidade.',encerrada_em=SYSDATETIMEOFFSET(),correlation_id=COALESCE(d.correlation_id,@correlation_id)
 FROM qualidade.divergencia_gestor d JOIN @o o ON o.obs=d.pessoa_observacao_id WHERE d.status='ABERTA' AND d.tipo='DIVERGENCIA_IDENTIDADE';
 UPDATE identidade.caso_conflito_identidade SET status='APLICADO',aplicado_em=SYSDATETIMEOFFSET(),correlation_id=COALESCE(correlation_id,@correlation_id) WHERE caso_id=@caso_id;
END;
GO

CREATE OR ALTER VIEW qualidade.v_divergencia_gestor_aberta AS
SELECT d.divergencia_id,d.gestor_id,g.codigo gestor,d.tipo,d.motivo,d.pessoa_observacao_id,d.registro_observacao_id,d.codigo_pessoa_origem,d.correlation_id,d.aberta_em
FROM qualidade.divergencia_gestor d JOIN ref.gestor g ON g.gestor_id=d.gestor_id WHERE d.status='ABERTA';
GO
CREATE OR ALTER PROCEDURE qualidade.sp_registrar_desfecho_divergencia
 @gestor_codigo NVARCHAR(30),@divergencia_id BIGINT,@status NVARCHAR(20),@desfecho NVARCHAR(80),@observacao NVARCHAR(2000)=NULL,@correlation_id UNIQUEIDENTIFIER=NULL
AS
BEGIN
 SET NOCOUNT ON;
 IF @status NOT IN('RESOLVIDA','DESCARTADA') THROW 51120,'Status de desfecho inválido.',1;
 DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor_codigo AND ativo=1);
 UPDATE qualidade.divergencia_gestor SET status=@status,desfecho=@desfecho,observacao_desfecho=@observacao,encerrada_em=SYSDATETIMEOFFSET(),correlation_id=COALESCE(correlation_id,@correlation_id)
 WHERE divergencia_id=@divergencia_id AND gestor_id=@gestor AND status='ABERTA';
 IF @@ROWCOUNT<>1 THROW 51121,'Divergência inexistente, encerrada ou de outro Gestor.',1;
END;
GO

CREATE OR ALTER PROCEDURE identidade.sp_aplicar_correcao_identidade
 @gestor_codigo NVARCHAR(30), @cpf CHAR(11), @grupo_titular NVARCHAR(80), @grupos_json NVARCHAR(MAX),
 @ato_referencia NVARCHAR(300), @justificativa NVARCHAR(2000), @correlation_id UNIQUEIDENTIFIER=NULL,
 @correcao_id UNIQUEIDENTIFIER OUTPUT, @pessoa_uuid_titular UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISJSON(@grupos_json)<>1 THROW 51076,'Grupos da correção devem ser JSON válido.',1;
 IF NULLIF(LTRIM(RTRIM(@ato_referencia)),'') IS NULL OR NULLIF(LTRIM(RTRIM(@justificativa)),'') IS NULL THROW 51077,'Ato e justificativa são obrigatórios.',1;
 DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor_codigo AND ativo=1);
 IF @gestor IS NULL THROW 51078,'Gestor responsável inexistente/inativo.',1;
 DECLARE @map BIGINT,@uuid_ant UNIQUEIDENTIFIER,@estado NVARCHAR(20);
 SELECT TOP(1) @map=identity_map_id,@uuid_ant=pessoa_uuid,@estado=estado FROM identidade.identity_map WITH(UPDLOCK,HOLDLOCK)
  WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;
 IF @map IS NULL OR @estado<>'EM_CONFLITO' THROW 51079,'CPF não possui conflito global ativo para correção.',1;

 DECLARE @g TABLE(grupo NVARCHAR(80) PRIMARY KEY,dest UNIQUEIDENTIFIER NULL);
 INSERT @g(grupo,dest)
 SELECT grupoCodigo,TRY_CONVERT(uniqueidentifier,pessoaUuidDestino)
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaUuidDestino NVARCHAR(40) '$.pessoaUuidDestino');
 IF NOT EXISTS(SELECT 1 FROM @g WHERE grupo=@grupo_titular) THROW 51080,'Grupo titular do CPF não foi informado.',1;
 IF EXISTS(SELECT 1 FROM @g WHERE NULLIF(LTRIM(RTRIM(grupo)),'') IS NULL) THROW 51081,'Código de grupo inválido.',1;
 IF EXISTS(SELECT 1 FROM @g g WHERE g.dest IS NOT NULL AND NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=g.dest)) THROW 51082,'UUID de destino informado não existe.',1;
 DECLARE @novos TABLE(grupo NVARCHAR(80) PRIMARY KEY,dest UNIQUEIDENTIFIER NOT NULL);
 INSERT @novos SELECT grupo,COALESCE(dest,NEWID()) FROM @g;
 INSERT identidade.pessoa(pessoa_uuid,status) SELECT n.dest,'ATIVO' FROM @novos n WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=n.dest);
 UPDATE p SET status='ATIVO' FROM identidade.pessoa p JOIN @novos n ON n.dest=p.pessoa_uuid;

 DECLARE @o TABLE(obs BIGINT PRIMARY KEY,grupo NVARCHAR(80) NOT NULL,dest UNIQUEIDENTIFIER NOT NULL,uuid_ant UNIQUEIDENTIFIER NULL,status_ant NVARCHAR(30) NULL,lote_id UNIQUEIDENTIFIER NOT NULL);
 INSERT @o(obs,grupo,dest,uuid_ant,status_ant,lote_id)
 SELECT j.pessoaObservacaoId,j.grupoCodigo,n.dest,vc.pessoa_uuid,vc.status,po.lote_id
 FROM OPENJSON(@grupos_json) WITH(grupoCodigo NVARCHAR(80) '$.grupoCodigo',pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g
 CROSS APPLY OPENJSON(g.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') j0
 CROSS APPLY (SELECT g.grupoCodigo,j0.pessoaObservacaoId) j
 JOIN @novos n ON n.grupo=j.grupoCodigo
 JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=j.pessoaObservacaoId AND po.cpf=@cpf
 LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id;
 IF EXISTS(SELECT pessoaObservacaoId FROM OPENJSON(@grupos_json) WITH(pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g CROSS APPLY OPENJSON(g.pessoaObservacaoIds) WITH(pessoaObservacaoId BIGINT '$') x GROUP BY pessoaObservacaoId HAVING COUNT(*)>1)
   THROW 51083,'Uma observação não pode pertencer a mais de um grupo.',1;
 DECLARE @solicitadas INT=(SELECT COUNT(*) FROM OPENJSON(@grupos_json) WITH(pessoaObservacaoIds NVARCHAR(MAX) '$.pessoaObservacaoIds' AS JSON) g CROSS APPLY OPENJSON(g.pessoaObservacaoIds));
 IF @solicitadas<>(SELECT COUNT(*) FROM @o) THROW 51084,'Há observação inexistente ou cujo CPF não corresponde ao conflito.',1;
 -- Para reativar o CPF é obrigatório classificar todas as observações que carregam esse CPF; não pode sobrar evidência ambígua fora do ato.
 IF EXISTS(SELECT 1 FROM silver.pessoa_observacao po WHERE po.cpf=@cpf AND NOT EXISTS(SELECT 1 FROM @o x WHERE x.obs=po.pessoa_observacao_id))
   THROW 51085,'A correção deve classificar todas as observações existentes com o CPF em conflito.',1;
 SELECT @pessoa_uuid_titular=dest FROM @novos WHERE grupo=@grupo_titular;
 SET @correcao_id=NEWID();
 INSERT identidade.correcao_identidade(correcao_id,gestor_responsavel_id,identity_map_origem_id,pessoa_uuid_titular,ato_referencia,justificativa,correlation_id)
 VALUES(@correcao_id,@gestor,@map,@pessoa_uuid_titular,@ato_referencia,@justificativa,@correlation_id);
 INSERT identidade.correcao_identidade_item(correcao_id,grupo_codigo,pessoa_observacao_id,pessoa_uuid_anterior,status_anterior,pessoa_uuid_destino)
 SELECT @correcao_id,grupo,obs,uuid_ant,status_ant,dest FROM @o;

 UPDATE vf SET ativo=0 FROM identidade.vinculo_fonte vf JOIN @o o ON o.obs=vf.pessoa_observacao_id WHERE vf.ativo=1;
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 SELECT obs,dest,'CORRECAO_GOVERNADA',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CORRECAO_IDENTIDADE_APLICADA' FROM @o;

 DECLARE @agora DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
 UPDATE identidade.identity_map SET vigencia_fim=@agora,estado='ENCERRADO',estado_motivo='CORRECAO_GOVERNADA',estado_em=@agora WHERE identity_map_id=@map;
 INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo,referencia_operacao)
 VALUES(@map,'EM_CONFLITO','ENCERRADO','CORRECAO_GOVERNADA',@correcao_id);
 INSERT identidade.identity_map(pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo,estado_em)
 VALUES(@pessoa_uuid_titular,'CPF',@cpf,@agora,@gestor,NULL,'CORRECAO_GOVERNADA','ATIVO','CORRECAO_GOVERNADA',@agora);
 DECLARE @novo_map BIGINT=SCOPE_IDENTITY();
 INSERT identidade.identity_map_estado_evento(identity_map_id,estado_anterior,estado_novo,motivo,referencia_operacao)
 VALUES(@novo_map,NULL,'ATIVO','CORRECAO_GOVERNADA',@correcao_id);

 -- Atualiza somente a atribuição canônica dos fatos já materializados; a ocorrência factual não depende de reprocessamento do lote.
 UPDATE b SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=@agora FROM gold.beneficio_concedido b JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE sp SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=@agora FROM gold.servico_prestado sp JOIN silver.registro_observacao ro ON ro.registro_observacao_id=sp.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;
 UPDATE ri SET pessoa_uuid=o.dest,estado_atribuicao_identidade='ATRIBUIDA',atualizado_em=@agora FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id JOIN @o o ON o.obs=ro.pessoa_observacao_id;

 -- Reconstrói a associação das linhas históricas de atributos; as versões correntes são fechadas e recriadas segundo a precedência vigente.
 DECLARE @afetados TABLE(uuid UNIQUEIDENTIFIER PRIMARY KEY);
 INSERT @afetados SELECT DISTINCT dest FROM @o UNION SELECT DISTINCT uuid_ant FROM @o WHERE uuid_ant IS NOT NULL;
 UPDATE ga SET vigencia_fim=COALESCE(ga.vigencia_fim,@agora),atualizado_em=@agora FROM gold.pessoa_atributo ga JOIN @afetados a ON a.uuid=ga.pessoa_uuid WHERE ga.vigencia_fim IS NULL;
 UPDATE ga SET pessoa_uuid=o.dest,atualizado_em=@agora FROM gold.pessoa_atributo ga JOIN silver.pessoa_atributo_observacao pao ON pao.pessoa_atributo_observacao_id=ga.pessoa_atributo_observacao_id JOIN @o o ON o.obs=pao.pessoa_observacao_id;
 ;WITH candidatos AS(
   SELECT vc.pessoa_uuid,pao.*,COALESCE(pao.referencia_evidencia,pao.verificado_em) precedencia,
          ROW_NUMBER() OVER(PARTITION BY vc.pessoa_uuid,pao.atributo_codigo ORDER BY COALESCE(pao.referencia_evidencia,pao.verificado_em) DESC,pao.verificado_em DESC,pao.pessoa_atributo_observacao_id DESC) rn
   FROM silver.pessoa_atributo_observacao pao JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=pao.pessoa_observacao_id AND vc.status='RESOLVIDO'
   JOIN @afetados a ON a.uuid=vc.pessoa_uuid WHERE pao.status_evidencia='COMPROVADO'
 )
 INSERT gold.pessoa_atributo(pessoa_uuid,atributo_codigo,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,vigencia_fim,atualizado_em)
 SELECT pessoa_uuid,atributo_codigo,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia,@agora,NULL,@agora FROM candidatos WHERE rn=1;

 -- Derived possibilities are invalid after reassociation and must be recomputed.
 DELETE ap FROM qualidade.avaliacao_possibilidade ap JOIN @afetados a ON a.uuid=ap.pessoa_uuid;
 -- v3.45: os fatos já existem na Gold; correção muda somente a atribuição canônica. Nenhum lote é reaberto.

 -- UUID sem vínculo corrente nem identificador ativo deixa de ser apresentado como ativo; histórico nunca é apagado.
 UPDATE p SET status='SEPARADO' FROM identidade.pessoa p JOIN @afetados a ON a.uuid=p.pessoa_uuid
 WHERE NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente v WHERE v.pessoa_uuid=p.pessoa_uuid AND v.status='RESOLVIDO')
   AND NOT EXISTS(SELECT 1 FROM identidade.identity_map m WHERE m.pessoa_uuid=p.pessoa_uuid AND m.vigencia_fim IS NULL AND m.estado='ATIVO');
 DELETE gp FROM gold.pessoa gp JOIN identidade.pessoa p ON p.pessoa_uuid=gp.pessoa_uuid WHERE p.status<>'ATIVO';
 DECLARE @rebuild UNIQUEIDENTIFIER;
 DECLARE rebuild_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT uuid FROM @afetados;
 OPEN rebuild_cursor; FETCH NEXT FROM rebuild_cursor INTO @rebuild;
 WHILE @@FETCH_STATUS=0
 BEGIN
   EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@rebuild;
   FETCH NEXT FROM rebuild_cursor INTO @rebuild;
 END
 CLOSE rebuild_cursor; DEALLOCATE rebuild_cursor;
 UPDATE d SET status='RESOLVIDA',desfecho='CORRECAO_GOVERNADA',observacao_desfecho='Resolvida por ato governado de identidade.',encerrada_em=@agora,correlation_id=COALESCE(d.correlation_id,@correlation_id)
 FROM qualidade.divergencia_gestor d JOIN @o o ON o.obs=d.pessoa_observacao_id WHERE d.status='ABERTA' AND d.tipo='DIVERGENCIA_IDENTIDADE';
 UPDATE identidade.correcao_identidade SET status='APLICADA',aplicado_em=@agora WHERE correcao_id=@correcao_id;
END;
GO

-- ============================================================================
-- v3.46 FINAL - BI factual único + retenção coordenada da Bronze por Entrega
-- ============================================================================

IF COL_LENGTH('bronze.entrega_arquivo','estado_armazenamento') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD estado_armazenamento NVARCHAR(20) NOT NULL
        CONSTRAINT df_bronze_entrega_estado_armazenamento DEFAULT('DISPONIVEL') WITH VALUES;
IF COL_LENGTH('bronze.entrega_arquivo','expurgo_iniciado_em') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD expurgo_iniciado_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('bronze.entrega_arquivo','expurgado_em') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD expurgado_em DATETIMEOFFSET(7) NULL;
IF COL_LENGTH('bronze.entrega_arquivo','retencao_motivo') IS NULL
    ALTER TABLE bronze.entrega_arquivo ADD retencao_motivo NVARCHAR(80) NULL;
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='ck_bronze_estado_armazenamento')
    ALTER TABLE bronze.entrega_arquivo DROP CONSTRAINT ck_bronze_estado_armazenamento;
ALTER TABLE bronze.entrega_arquivo WITH CHECK ADD CONSTRAINT ck_bronze_estado_armazenamento CHECK(
    (estado_armazenamento='DISPONIVEL' AND expurgado_em IS NULL)
 OR (estado_armazenamento='EXPURGO_PENDENTE' AND expurgo_iniciado_em IS NOT NULL AND expurgado_em IS NULL)
 OR (estado_armazenamento='EXPURGADO' AND expurgo_iniciado_em IS NOT NULL AND expurgado_em IS NOT NULL));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='IX_bronze_entrega_retencao')
    CREATE INDEX IX_bronze_entrega_retencao
      ON bronze.entrega_arquivo(estado_armazenamento,recebido_em,entrega_id)
      INCLUDE(objeto_chave,payload_sha256,tamanho_bytes,expurgo_iniciado_em,expurgado_em);
GO

IF OBJECT_ID('controle.entrega_retencao_ciclo','U') IS NULL CREATE TABLE controle.entrega_retencao_ciclo(
    ciclo_id BIGINT IDENTITY PRIMARY KEY,
    iniciado_em DATETIMEOFFSET(7) NOT NULL,
    finalizado_em DATETIMEOFFSET(7) NOT NULL,
    candidatos INT NOT NULL,
    referencias_expurgadas INT NOT NULL,
    objetos_fisicos_removidos INT NOT NULL,
    objetos_compartilhados_preservados INT NOT NULL,
    locks_nao_adquiridos INT NOT NULL,
    falhas_storage INT NOT NULL,
    CONSTRAINT ck_entrega_retencao_metricas CHECK(
        candidatos>=0 AND referencias_expurgadas>=0 AND objetos_fisicos_removidos>=0
        AND objetos_compartilhados_preservados>=0 AND locks_nao_adquiridos>=0 AND falhas_storage>=0));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('controle.entrega_retencao_ciclo') AND name='IX_entrega_retencao_ciclo_finalizado')
    CREATE INDEX IX_entrega_retencao_ciclo_finalizado ON controle.entrega_retencao_ciclo(finalizado_em DESC);
GO
CREATE OR ALTER VIEW serving.v_bi_retencao_bronze AS
SELECT ciclo_id,iniciado_em,finalizado_em,DATEDIFF(SECOND,iniciado_em,finalizado_em) duracao_segundos,
       candidatos,referencias_expurgadas,objetos_fisicos_removidos,objetos_compartilhados_preservados,
       locks_nao_adquiridos,falhas_storage
FROM controle.entrega_retencao_ciclo;
GO

IF OBJECT_ID('serving.v_bi_fatos_identidade','V') IS NOT NULL DROP VIEW serving.v_bi_fatos_identidade;
GO

CREATE OR ALTER VIEW serving.v_bi_beneficios_concedidos AS
SELECT ri.registro_observacao_id beneficio_observacao_id,ri.registro_origem_id,ri.codigo_registro_origem,ri.versao_interna,ri.operacao,ri.status_analitico,
       g.codigo gestor,tr.codigo beneficio,tr.nome nome_beneficio,trv.tipo_medida,
       ri.estado_atribuicao_identidade,
       ri.data_inicio_concessao,ri.data_fim_concessao,ri.data_evento_concessao,ri.valor_concedido,ri.quantidade,ri.unidade unidade_medida,ri.source_as_of,
       ri.vigencia_versao_inicio,ri.vigencia_versao_fim,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       ri.natureza_referencia_territorial,ri.referencia_territorial_observacao_id,
       ri.entrega_completa,ri.qc_resultado,COALESCE(ri.qc_especifico_implementado,0) qc_especifico_implementado
FROM serving.registro_integrado ri
JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ri.tipo_registro_versao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE';
GO

CREATE OR ALTER VIEW serving.v_bi_servicos_prestados AS
SELECT ri.registro_observacao_id servico_observacao_id,ri.registro_origem_id,ri.codigo_registro_origem,ri.versao_interna,ri.operacao,ri.status_analitico,
       g.codigo gestor,tr.codigo servico,tr.nome nome_servico,
       ri.estado_atribuicao_identidade,
       ri.data_hora_servico,ri.unidade_servico,ri.situacao,ri.source_as_of,ri.vigencia_versao_inicio,ri.vigencia_versao_fim,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       ri.natureza_referencia_territorial,ri.referencia_territorial_observacao_id,ri.entrega_completa,
       ri.qc_resultado,COALESCE(ri.qc_especifico_implementado,0) qc_especifico_implementado
FROM serving.registro_integrado ri
JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE';
GO

CREATE OR ALTER VIEW serving.v_bi_registros AS
SELECT CONCAT(CASE WHEN ri.natureza='BENEFICIO' THEN 'B:' ELSE 'S:' END,ri.registro_observacao_id) registro_id,
       ri.natureza,tr.codigo codigo,tr.nome nome,g.codigo gestor,
       ri.estado_atribuicao_identidade,
       ri.source_as_of data_referencia,
       COALESCE(ri.data_hora_servico,CAST(ri.data_evento_concessao AS DATETIMEOFFSET),CAST(ri.data_inicio_concessao AS DATETIMEOFFSET),ri.source_as_of) ocorrido_em,
       ri.situacao,ri.registro_observacao_id source_observacao_id,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
       COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       ri.natureza_referencia_territorial,
       ri.entrega_completa,ri.qc_resultado
FROM serving.registro_integrado ri
JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=ri.subprefeitura_referencia_id
LEFT JOIN ref.distrito d ON d.distrito_id=ri.distrito_referencia_id
WHERE ri.status_analitico='VIGENTE';
GO

CREATE OR ALTER VIEW serving.v_bi_beneficios_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(30)) gestor,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
UNION ALL
SELECT 'GESTOR',g.codigo,COUNT_BIG(DISTINCT ri.pessoa_uuid)
FROM serving.registro_integrado ri JOIN ref.gestor g ON g.gestor_id=ri.gestor_id
WHERE ri.natureza='BENEFICIO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
GROUP BY g.codigo;
GO
CREATE OR ALTER VIEW serving.v_bi_servicos_pessoas AS
SELECT CAST('TOTAL' AS NVARCHAR(20)) escopo,CAST(NULL AS NVARCHAR(200)) nome_servico,COUNT_BIG(DISTINCT ri.pessoa_uuid) pessoas
FROM serving.registro_integrado ri
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
UNION ALL
SELECT 'SERVICO',tr.nome,COUNT_BIG(DISTINCT ri.pessoa_uuid)
FROM serving.registro_integrado ri JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ri.tipo_registro_id
WHERE ri.natureza='SERVICO' AND ri.status_analitico='VIGENTE' AND ri.estado_atribuicao_identidade='ATRIBUIDA'
GROUP BY tr.nome;
GO
CREATE OR ALTER VIEW serving.v_bi_registros_pessoas AS
SELECT COUNT_BIG(DISTINCT pessoa_uuid) pessoas
FROM serving.registro_integrado
WHERE status_analitico='VIGENTE' AND estado_atribuicao_identidade='ATRIBUIDA';
GO

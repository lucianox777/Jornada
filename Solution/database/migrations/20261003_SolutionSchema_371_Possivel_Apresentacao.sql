SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  SolutionSchema 3.71 - apresentação governada de candidatos (#408)
  -----------------------------------------------------------------
  Persistência append-only da apresentação efetivamente exibida no balcão.
  Não define limiar POSSIVEL, não ativa fluxo em HML/PRD e não transforma
  ausência de ato em decisão. Candidatos referenciam UUIDs; PII não é copiada.
*/

IF OBJECT_ID(N'identidade.linkage_apresentacao',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_apresentacao(
   apresentacao_id UNIQUEIDENTIFIER NOT NULL
     CONSTRAINT pk_linkage_apresentacao PRIMARY KEY,
   pessoa_observacao_id BIGINT NOT NULL,
   linkage_resultado_id BIGINT NULL,
   modelo_id UNIQUEIDENTIFIER NOT NULL,
   modelo_versao INT NOT NULL,
   ruleset_versao NVARCHAR(120) NOT NULL,
   evidencia_fingerprint_sha256 CHAR(64) NOT NULL,
   candidatos_fingerprint_sha256 CHAR(64) NOT NULL,
   quantidade_candidatos TINYINT NOT NULL,
   exibido_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT df_linkage_apresentacao_exibido DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),
   correlation_id UNIQUEIDENTIFIER NULL,
   CONSTRAINT fk_linkage_apresentacao_observacao FOREIGN KEY(pessoa_observacao_id)
     REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
   CONSTRAINT fk_linkage_apresentacao_resultado FOREIGN KEY(linkage_resultado_id)
     REFERENCES identidade.linkage_resultado(linkage_resultado_id),
   CONSTRAINT ck_linkage_apresentacao_quantidade CHECK(quantidade_candidatos BETWEEN 1 AND 5),
   CONSTRAINT ck_linkage_apresentacao_evidencia_hash CHECK(
     LEN(evidencia_fingerprint_sha256)=64 AND evidencia_fingerprint_sha256 NOT LIKE '%[^0-9a-f]%'),
   CONSTRAINT ck_linkage_apresentacao_candidatos_hash CHECK(
     LEN(candidatos_fingerprint_sha256)=64 AND candidatos_fingerprint_sha256 NOT LIKE '%[^0-9a-f]%')
 );
END;
GO

IF OBJECT_ID(N'identidade.linkage_apresentacao_candidato',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_apresentacao_candidato(
   apresentacao_id UNIQUEIDENTIFIER NOT NULL,
   ordem TINYINT NOT NULL,
   candidato_uuid UNIQUEIDENTIFIER NOT NULL,
   CONSTRAINT pk_linkage_apresentacao_candidato PRIMARY KEY(apresentacao_id,ordem),
   CONSTRAINT fk_linkage_apresentacao_candidato_apresentacao FOREIGN KEY(apresentacao_id)
     REFERENCES identidade.linkage_apresentacao(apresentacao_id),
   CONSTRAINT fk_linkage_apresentacao_candidato_pessoa FOREIGN KEY(candidato_uuid)
     REFERENCES identidade.pessoa(pessoa_uuid),
   CONSTRAINT ck_linkage_apresentacao_candidato_ordem CHECK(ordem BETWEEN 1 AND 5),
   CONSTRAINT ux_linkage_apresentacao_candidato UNIQUE(apresentacao_id,candidato_uuid)
 );
END;
GO

IF OBJECT_ID(N'identidade.linkage_apresentacao_desfecho',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_apresentacao_desfecho(
   desfecho_id BIGINT IDENTITY(1,1) NOT NULL
     CONSTRAINT pk_linkage_apresentacao_desfecho PRIMARY KEY,
   apresentacao_id UNIQUEIDENTIFIER NOT NULL,
   candidato_uuid UNIQUEIDENTIFIER NULL,
   ato NVARCHAR(40) NOT NULL,
   autoria_tipo NVARCHAR(40) NOT NULL,
   autoria_referencia NVARCHAR(255) NOT NULL,
   evidencia_referencia NVARCHAR(500) NULL,
   ocorrido_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT df_linkage_apresentacao_desfecho_ocorrido DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),
   correlation_id UNIQUEIDENTIFIER NULL,
   CONSTRAINT fk_linkage_apresentacao_desfecho_apresentacao FOREIGN KEY(apresentacao_id)
     REFERENCES identidade.linkage_apresentacao(apresentacao_id),
   CONSTRAINT ck_linkage_apresentacao_desfecho_ato CHECK(
     ato IN(N'CONFIRMADO_DOCUMENTO',N'CONFIRMADO_INSTITUCIONAL',N'RECUSADO',N'NENHUM_DESTES')),
   CONSTRAINT ck_linkage_apresentacao_desfecho_alvo CHECK(
     (ato=N'NENHUM_DESTES' AND candidato_uuid IS NULL)
     OR (ato<>N'NENHUM_DESTES' AND candidato_uuid IS NOT NULL))
 );
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_apresentacao_append_only
ON identidade.linkage_apresentacao
INSTEAD OF UPDATE, DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51870,'Apresentação exibida é evidência histórica imutável.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_apresentacao_candidato_append_only
ON identidade.linkage_apresentacao_candidato
INSTEAD OF UPDATE, DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51871,'Conjunto efetivamente exibido é histórico imutável.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_apresentacao_desfecho_append_only
ON identidade.linkage_apresentacao_desfecho
INSTEAD OF UPDATE, DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51872,'Desfecho do balcão é ledger append-only.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_apresentacao_desfecho_candidato_exibido
ON identidade.linkage_apresentacao_desfecho
AFTER INSERT
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i
   WHERE i.candidato_uuid IS NOT NULL
     AND NOT EXISTS(
       SELECT 1 FROM identidade.linkage_apresentacao_candidato c
       WHERE c.apresentacao_id=i.apresentacao_id AND c.candidato_uuid=i.candidato_uuid))
   THROW 51873,'Desfecho por candidato exige candidato efetivamente exibido.',1;
END;
GO

CREATE OR ALTER VIEW identidade.v_linkage_apresentacao_auditoria AS
SELECT a.apresentacao_id,a.pessoa_observacao_id,a.linkage_resultado_id,
       a.modelo_id,a.modelo_versao,a.ruleset_versao,
       a.evidencia_fingerprint_sha256,a.candidatos_fingerprint_sha256,
       a.quantidade_candidatos,a.exibido_em,a.correlation_id,
       c.ordem,c.candidato_uuid
FROM identidade.linkage_apresentacao a
JOIN identidade.linkage_apresentacao_candidato c ON c.apresentacao_id=a.apresentacao_id;
GO

CREATE OR ALTER PROCEDURE identidade.sp_selar_linkage_apresentacao_v1
 @apresentacao_id UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 DECLARE @declarados TINYINT,@observados INT,@declarado CHAR(64),@calculado CHAR(64);
 SELECT @declarados=quantidade_candidatos,@declarado=candidatos_fingerprint_sha256
 FROM identidade.linkage_apresentacao WITH(UPDLOCK,HOLDLOCK)
 WHERE apresentacao_id=@apresentacao_id;
 IF @declarados IS NULL THROW 51874,'Apresentação inexistente.',1;

 SELECT @observados=COUNT(*),
        @calculado=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',
          STRING_AGG(LOWER(CONVERT(NVARCHAR(36),candidato_uuid)),N'|')
            WITHIN GROUP(ORDER BY ordem)),2))
 FROM identidade.linkage_apresentacao_candidato WITH(HOLDLOCK)
 WHERE apresentacao_id=@apresentacao_id;

 IF @observados<>@declarados
   THROW 51875,'Quantidade de candidatos persistidos diverge da apresentação.',1;
 IF @calculado<>@declarado
   THROW 51876,'Fingerprint do conjunto exibido diverge dos candidatos ordenados.',1;
END;
GO

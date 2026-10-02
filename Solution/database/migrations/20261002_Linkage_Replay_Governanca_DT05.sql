SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
/* DT-05 Marco B: identidade create-once do estado candidato/governança observado antes do score.
   Esta migration captura identidade auditável; o scorer ainda não consome uma cópia histórica congelada. */
IF OBJECT_ID(N'identidade.linkage_replay_estado_governanca',N'U') IS NULL
CREATE TABLE identidade.linkage_replay_estado_governanca(
 linkage_run_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
   REFERENCES identidade.linkage_run(linkage_run_id),
 candidatos_referencia BIGINT NOT NULL,
 candidatos_sha256 CHAR(64) NOT NULL,
 governanca_evento_high_watermark BIGINT NOT NULL,
 registrado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT CK_linkage_replay_estado_governanca_sha CHECK(
   LEN(candidatos_sha256)=64 AND candidatos_sha256 NOT LIKE '%[^0-9a-f]%')
);
GO
CREATE OR ALTER TRIGGER identidade.tr_linkage_replay_estado_governanca_imutavel
ON identidade.linkage_replay_estado_governanca
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51980,N'DT-05: identidade de candidatos/governança do run é imutável.',1;
END;
GO
CREATE OR ALTER PROCEDURE identidade.sp_capturar_estado_governanca_linkage
 @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF XACT_STATE()=0 THROW 51981,N'DT-05: captura de governança exige transação explícita.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run WITH(UPDLOCK,HOLDLOCK)
               WHERE linkage_run_id=@linkage_run_id AND status=N'EXECUTANDO')
   THROW 51982,N'DT-05: run deve estar EXECUTANDO para capturar governança.',1;
 IF EXISTS(SELECT 1 FROM identidade.linkage_replay_estado_governanca WITH(UPDLOCK,HOLDLOCK)
           WHERE linkage_run_id=@linkage_run_id)
   THROW 51983,N'DT-05: estado de governança do run já foi capturado.',1;

 DECLARE @canonical NVARCHAR(MAX);
 SELECT @canonical=STRING_AGG(CONVERT(NVARCHAR(MAX),LOWER(CONVERT(CHAR(36),pessoa_uuid)))+N'|'+estado_identidade,NCHAR(10))
                   WITHIN GROUP(ORDER BY pessoa_uuid)
 FROM gold.pessoa WITH(HOLDLOCK)
 WHERE estado_identidade=N'REFERENCIA';

 DECLARE @count BIGINT=(SELECT COUNT_BIG(*) FROM gold.pessoa WITH(HOLDLOCK) WHERE estado_identidade=N'REFERENCIA');
 DECLARE @sha CHAR(64)=LOWER(CONVERT(CHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),COALESCE(@canonical,N''))),2));
 DECLARE @gov BIGINT=COALESCE((SELECT MAX(decisao_identidade_evento_id) FROM auditoria.decisao_identidade_evento WITH(HOLDLOCK)),0);

 INSERT identidade.linkage_replay_estado_governanca(
   linkage_run_id,candidatos_referencia,candidatos_sha256,governanca_evento_high_watermark)
 VALUES(@linkage_run_id,@count,@sha,@gov);
END;
GO

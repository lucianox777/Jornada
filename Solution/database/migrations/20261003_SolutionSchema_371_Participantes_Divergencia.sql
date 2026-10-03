SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
/*
 SolutionSchema 3.71 closeout — #414 + #401.
 Additive only. Final schema promotion remains in the dedicated 3.71 consolidation migration.
*/

/* #414: a person attached to the fact remains the BENEFICIARIO.
   Other people participate through explicit roles; no role implies identity equivalence. */
IF OBJECT_ID(N'gold.registro_participante',N'U') IS NULL
BEGIN
 CREATE TABLE gold.registro_participante(
   registro_participante_id BIGINT IDENTITY PRIMARY KEY,
   registro_observacao_id BIGINT NOT NULL REFERENCES silver.registro_observacao(registro_observacao_id),
   pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
   papel NVARCHAR(40) NOT NULL,
   status_evidencia NVARCHAR(20) NOT NULL,
   source_as_of DATETIMEOFFSET(7) NOT NULL,
   criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
   CONSTRAINT uq_registro_participante UNIQUE(registro_observacao_id,pessoa_observacao_id,papel),
   CONSTRAINT ck_registro_participante_papel CHECK(papel IN(N'BENEFICIARIO',N'RESPONSAVEL_RECEBIMENTO')),
   CONSTRAINT ck_registro_participante_evidencia CHECK(status_evidencia IN(N'DECLARADO',N'COMPROVADO'))
 );
END;
GO
CREATE OR ALTER VIEW gold.v_registro_participante_corrente AS
SELECT rp.registro_participante_id,rp.registro_observacao_id,rp.pessoa_observacao_id,
       rp.papel,rp.status_evidencia,rp.source_as_of
FROM gold.registro_participante rp
JOIN silver.registro_observacao ro ON ro.registro_observacao_id=rp.registro_observacao_id
WHERE ro.operacao<>N'EXCLUSAO';
GO

/* #401: fingerprint causal versioned. It contains only causal evidence:
   observation content hash + canonical candidate set + categorical reason + method version.
   Scores and modelo_id are intentionally excluded. */
IF COL_LENGTH(N'qualidade.divergencia_gestor',N'fingerprint_causal') IS NULL
 ALTER TABLE qualidade.divergencia_gestor ADD fingerprint_causal CHAR(64) NULL;
IF COL_LENGTH(N'qualidade.divergencia_gestor',N'fingerprint_metodo_versao') IS NULL
 ALTER TABLE qualidade.divergencia_gestor ADD fingerprint_metodo_versao NVARCHAR(40) NULL;
IF COL_LENGTH(N'qualidade.divergencia_gestor',N'evidencia_causal_json') IS NULL
 ALTER TABLE qualidade.divergencia_gestor ADD evidencia_causal_json NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'qualidade.divergencia_gestor',N'reaberta_de_divergencia_id') IS NULL
 ALTER TABLE qualidade.divergencia_gestor ADD reaberta_de_divergencia_id BIGINT NULL;
GO
IF OBJECT_ID(N'qualidade.ck_divergencia_fingerprint_par',N'C') IS NULL
 ALTER TABLE qualidade.divergencia_gestor WITH CHECK ADD CONSTRAINT ck_divergencia_fingerprint_par CHECK(
   (fingerprint_causal IS NULL AND fingerprint_metodo_versao IS NULL AND evidencia_causal_json IS NULL)
   OR
   (fingerprint_causal IS NOT NULL AND fingerprint_metodo_versao IS NOT NULL AND evidencia_causal_json IS NOT NULL AND ISJSON(evidencia_causal_json)=1));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'qualidade.divergencia_gestor') AND name=N'IX_divergencia_fingerprint')
 CREATE INDEX IX_divergencia_fingerprint ON qualidade.divergencia_gestor(gestor_id,tipo,fingerprint_causal,divergencia_id DESC)
 WHERE fingerprint_causal IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE qualidade.sp_registrar_divergencia_causal_v1
 @gestor_id BIGINT,
 @tipo NVARCHAR(50),
 @motivo NVARCHAR(120),
 @pessoa_observacao_id BIGINT,
 @registro_observacao_id BIGINT=NULL,
 @codigo_pessoa_origem NVARCHAR(255)=NULL,
 @candidatos_json NVARCHAR(MAX)=N'[]',
 @correlation_id UNIQUEIDENTIFIER=NULL,
 @divergencia_id BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF ISJSON(@candidatos_json)<>1 THROW 52101,N'candidatos_json inválido.',1;
 DECLARE @conteudo_hash CHAR(64)=(SELECT conteudo_hash FROM silver.pessoa_observacao WHERE pessoa_observacao_id=@pessoa_observacao_id);
 IF @conteudo_hash IS NULL THROW 52102,N'Observação inexistente ou sem conteudo_hash.',1;

 DECLARE @candidatos NVARCHAR(MAX)=(
   SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),x.valor),N',') WITHIN GROUP(ORDER BY x.valor)
   FROM (SELECT DISTINCT CONVERT(NVARCHAR(36),TRY_CONVERT(UNIQUEIDENTIFIER,[value])) valor
         FROM OPENJSON(@candidatos_json) WHERE TRY_CONVERT(UNIQUEIDENTIFIER,[value]) IS NOT NULL) x);
 SET @candidatos=COALESCE(@candidatos,N'');
 DECLARE @metodo NVARCHAR(40)=N'DIVERGENCIA_CAUSAL_V1';
 DECLARE @evidencia NVARCHAR(MAX)=(
   SELECT @conteudo_hash conteudo_hash,@candidatos candidatos_canonicos,@motivo motivo,@metodo metodo_versao
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 DECLARE @fp CHAR(64)=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',
   CONCAT(@conteudo_hash,N'|',@candidatos,N'|',@motivo,N'|',@metodo)),2));

 DECLARE @anterior BIGINT,@status NVARCHAR(20);
 SELECT TOP(1) @anterior=divergencia_id,@status=status
 FROM qualidade.divergencia_gestor WITH (UPDLOCK,HOLDLOCK)
 WHERE gestor_id=@gestor_id AND tipo=@tipo AND fingerprint_causal=@fp
 ORDER BY divergencia_id DESC;

 IF @anterior IS NOT NULL
 BEGIN
   SET @divergencia_id=@anterior; -- same causal state never reopens after governed closure
   RETURN;
 END;

 DECLARE @ultima BIGINT=(
   SELECT TOP(1) divergencia_id FROM qualidade.divergencia_gestor WITH (UPDLOCK,HOLDLOCK)
   WHERE gestor_id=@gestor_id AND tipo=@tipo AND pessoa_observacao_id=@pessoa_observacao_id
   ORDER BY divergencia_id DESC);

 INSERT qualidade.divergencia_gestor(
   gestor_id,tipo,motivo,pessoa_observacao_id,registro_observacao_id,codigo_pessoa_origem,
   status,correlation_id,fingerprint_causal,fingerprint_metodo_versao,evidencia_causal_json,
   reaberta_de_divergencia_id)
 VALUES(@gestor_id,@tipo,@motivo,@pessoa_observacao_id,@registro_observacao_id,@codigo_pessoa_origem,
   N'ABERTA',@correlation_id,@fp,@metodo,@evidencia,@ultima);
 SET @divergencia_id=SCOPE_IDENTITY();
END;
GO

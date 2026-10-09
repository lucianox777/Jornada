SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Recebe o JSON já validado por hash SHA-256 do manifesto no importador.
-- Operação idempotente: nunca sobrescreve versão publicada.
CREATE OR ALTER PROCEDURE ref.sp_carregar_distribuicao_nascimento_json
 @codigo NVARCHAR(120),
 @fonte NVARCHAR(160),
 @geografia NVARCHAR(40),
 @data_referencia DATE,
 @metodo NVARCHAR(160),
 @fonte_arquivo_sha256 CHAR(64),
 @linhas_esperadas BIGINT,
 @peso_total_esperado BIGINT,
 @json NVARCHAR(MAX),
 @distribuicao_versao_id BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF ISJSON(@json)<>1 OR JSON_VALUE(@json,'$.schema_version')<>N'JORNADA_SYNTH_BIRTH_DAILY_V1'
   THROW 52240,'Schema JSON de distribuição demográfica inválido.',1;
 IF @linhas_esperadas<=0 OR @peso_total_esperado<=0
   THROW 52241,'Manifesto de distribuição demográfica inválido.',1;
 IF JSON_QUERY(@json,'$.rows') IS NULL\n   THROW 52248,'Array rows obrigatório.',1;\n DECLARE @rows TABLE(data_nascimento DATE PRIMARY KEY,peso BIGINT NOT NULL);
 INSERT @rows(data_nascimento,peso)
 SELECT TRY_CONVERT(DATE,JSON_VALUE(value,'$.date'),23),
        TRY_CONVERT(BIGINT,JSON_VALUE(value,'$.births'))
 FROM OPENJSON(@json,'$.rows');
 IF EXISTS(SELECT 1 FROM @rows WHERE data_nascimento IS NULL OR peso IS NULL OR peso<=0)
   THROW 52242,'Peso diário inválido.',1;
 IF NOT EXISTS(SELECT 1 FROM OPENJSON(@json,'$.rows'))
   THROW 52246,'Distribuição sem linhas.',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@json,'$.rows')
   WHERE TRY_CONVERT(DATE,JSON_VALUE(value,'$.date'),23) IS NULL
     OR TRY_CONVERT(BIGINT,JSON_VALUE(value,'$.births')) IS NULL)
   THROW 52247,'Linha demográfica com data ou peso inválido.',1;
 IF (SELECT COUNT_BIG(*) FROM @rows)<>@linhas_esperadas
   OR (SELECT SUM(peso) FROM @rows)<>@peso_total_esperado
   THROW 52243,'Distribuição divergente do manifesto.',1;

 BEGIN TRANSACTION;
 BEGIN TRY
   DECLARE @status NVARCHAR(20),@sha CHAR(64);
   SELECT @distribuicao_versao_id=distribuicao_versao_id,
          @status=status,@sha=fonte_arquivo_sha256
   FROM ref.distribuicao_nascimento_versao WITH(UPDLOCK,HOLDLOCK)
   WHERE codigo=@codigo;
   IF @distribuicao_versao_id IS NULL
   BEGIN
     INSERT ref.distribuicao_nascimento_versao(
       codigo,fonte,geografia,data_referencia,metodo,fonte_arquivo_sha256)
     VALUES(@codigo,@fonte,@geografia,@data_referencia,@metodo,@fonte_arquivo_sha256);
     SET @distribuicao_versao_id=CONVERT(BIGINT,SCOPE_IDENTITY());
   END
   ELSE
   BEGIN
     IF NOT EXISTS(
       SELECT 1 FROM ref.distribuicao_nascimento_versao
       WHERE distribuicao_versao_id=@distribuicao_versao_id
         AND fonte=@fonte AND geografia=@geografia
         AND data_referencia=@data_referencia AND metodo=@metodo
         AND fonte_arquivo_sha256=@fonte_arquivo_sha256)
       THROW 52244,'Código já registrado com proveniência diferente.',1;
     IF @status=N'PUBLICADA'
     BEGIN
       IF (SELECT COUNT_BIG(*) FROM ref.distribuicao_nascimento_dia
           WHERE distribuicao_versao_id=@distribuicao_versao_id)<>@linhas_esperadas
         OR (SELECT SUM(peso_populacional) FROM ref.distribuicao_nascimento_dia
           WHERE distribuicao_versao_id=@distribuicao_versao_id)<>@peso_total_esperado
         THROW 52245,'Distribuição publicada diverge do manifesto.',1;
       COMMIT TRANSACTION;
       RETURN;
     END;
     DELETE ref.distribuicao_nascimento_dia
       WHERE distribuicao_versao_id=@distribuicao_versao_id;
   END;
   INSERT ref.distribuicao_nascimento_dia(
     distribuicao_versao_id,data_nascimento,peso_populacional)
   SELECT @distribuicao_versao_id,data_nascimento,peso FROM @rows;
   COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH
END;
GO

-- Plano determinístico de recomposição V1.
-- PLANEJADA exige composição APLICADA, mas não significa PUBLICADA e não autoriza writers Gold/Serving.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @installation_lock INT;
 EXEC @installation_lock=sys.sp_getapplock
   @Resource=N'JORNADA:COMPOSICAO:RECOMPOSICAO:PLANO:INSTALL:V1',
   @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @installation_lock<0 THROW 51520,'Não foi possível reservar a instalação do plano de recomposição.',1;
 IF OBJECT_ID('identidade.composicao_aplicacao','U') IS NULL
   THROW 51521,'Aplicação governada de composição deve estar instalada antes do plano de recomposição.',1;

 IF OBJECT_ID('identidade.composicao_recomposicao_plano','U') IS NULL
 BEGIN
  CREATE TABLE identidade.composicao_recomposicao_plano(
   decision_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_composicao_recomposicao_plano PRIMARY KEY,
   composition_request_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   recomposition_version VARCHAR(80) NOT NULL,
   plan_json NVARCHAR(MAX) NOT NULL,
   plan_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL,
   estado VARCHAR(20) NOT NULL CONSTRAINT df_composicao_recomposicao_plano_estado DEFAULT('PLANEJADA'),
   CONSTRAINT fk_composicao_recomposicao_plano_aplicacao FOREIGN KEY(decision_id)
      REFERENCES identidade.composicao_aplicacao(decision_id),
   CONSTRAINT ck_composicao_recomposicao_plano_estado CHECK(estado='PLANEJADA'),
   CONSTRAINT ck_composicao_recomposicao_plano_versao CHECK(LEN(LTRIM(RTRIM(recomposition_version)))>0),
   CONSTRAINT ck_composicao_recomposicao_plano_json CHECK(ISJSON(plan_json)=1 AND LEFT(LTRIM(plan_json),1)='{'),
   CONSTRAINT ck_composicao_recomposicao_plano_hash CHECK(
      LEN(composition_request_hash)=64 AND composition_request_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND
      LEN(plan_hash)=64 AND plan_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)
  );
 END;

 IF COL_LENGTH('identidade.composicao_recomposicao_plano','plan_hash') IS NULL OR
    COL_LENGTH('identidade.composicao_recomposicao_plano','recomposition_version') IS NULL
   THROW 51522,'Schema existente de plano de recomposição é incompatível.',1;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO
CREATE OR ALTER TRIGGER identidade.tr_composicao_recomposicao_plano_append_only
ON identidade.composicao_recomposicao_plano INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51523,'Plano de recomposição é append-only.',1;
END;
GO

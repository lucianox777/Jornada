-- Aplicação governada da composição V1.
-- APLICADA é derivado da existência do recibo append-only abaixo; composicao_plano permanece PREPARADA e imutável.
-- Esta migração não cria endpoint, worker, ativação probabilística, nem publica/recompõe Gold/Serving.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @installation_lock INT;
 EXEC @installation_lock=sys.sp_getapplock
   @Resource=N'JORNADA:COMPOSICAO:APLICACAO:INSTALL:V1',
   @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @installation_lock<0 THROW 51500,'Não foi possível reservar a instalação da aplicação de composição.',1;
 IF OBJECT_ID('identidade.composicao_plano','U') IS NULL OR
    OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL OR
    OBJECT_ID('identidade.pessoa_origem_progressiva_evento','U') IS NULL
   THROW 51501,'Ledger e persistência progressiva devem estar instalados.',1;

 IF OBJECT_ID('identidade.composicao_aplicacao','U') IS NULL
 BEGIN
  CREATE TABLE identidade.composicao_aplicacao(
   decision_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_composicao_aplicacao PRIMARY KEY,
   request_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   plan_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   reservas_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   aplicador_referencia NVARCHAR(120) NOT NULL,
   aplicado_em DATETIMEOFFSET(7) NOT NULL,
   alteracoes_aplicadas INT NOT NULL,
   historicos_registrados INT NOT NULL,
   estado VARCHAR(20) NOT NULL CONSTRAINT df_composicao_aplicacao_estado DEFAULT('APLICADA'),
   CONSTRAINT fk_composicao_aplicacao_plano FOREIGN KEY(decision_id) REFERENCES identidade.composicao_plano(decision_id),
   CONSTRAINT ck_composicao_aplicacao_estado CHECK(estado='APLICADA'),
   CONSTRAINT ck_composicao_aplicacao_contagens CHECK(alteracoes_aplicadas>0 AND historicos_registrados>=0),
   CONSTRAINT ck_composicao_aplicacao_aplicador CHECK(LEN(LTRIM(RTRIM(aplicador_referencia)))>0),
   CONSTRAINT ck_composicao_aplicacao_hash CHECK(
    LEN(request_hash)=64 AND request_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND
    LEN(plan_hash)=64 AND plan_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND
    LEN(reservas_hash)=64 AND reservas_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)
  );
 END;

 IF OBJECT_ID('identidade.composicao_historico_aplicado','U') IS NULL
 BEGIN
  CREATE TABLE identidade.composicao_historico_aplicado(
   decision_id UNIQUEIDENTIFIER NOT NULL,
   reference_uuid UNIQUEIDENTIFIER NOT NULL,
   members_json NVARCHAR(MAX) NOT NULL,
   members_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL,
   CONSTRAINT pk_composicao_historico_aplicado PRIMARY KEY(decision_id,reference_uuid),
   CONSTRAINT fk_composicao_historico_aplicado_plano FOREIGN KEY(decision_id) REFERENCES identidade.composicao_plano(decision_id),
   CONSTRAINT fk_composicao_historico_aplicado_pessoa FOREIGN KEY(reference_uuid) REFERENCES identidade.pessoa(pessoa_uuid),
   CONSTRAINT ck_composicao_historico_aplicado_json CHECK(ISJSON(members_json)=1 AND LEFT(LTRIM(members_json),1)='['),
   CONSTRAINT ck_composicao_historico_aplicado_hash CHECK(
    LEN(members_hash)=64 AND members_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)
  );
  CREATE INDEX ix_composicao_historico_aplicado_referencia
    ON identidade.composicao_historico_aplicado(reference_uuid,registrado_em);
 END;

 IF COL_LENGTH('identidade.composicao_aplicacao','plan_hash') IS NULL OR
    COL_LENGTH('identidade.composicao_historico_aplicado','members_hash') IS NULL
   THROW 51502,'Schema de aplicação de composição existente incompatível.',1;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO
CREATE OR ALTER TRIGGER identidade.tr_composicao_aplicacao_append_only
ON identidade.composicao_aplicacao INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51503,'Recibo de aplicação é append-only.',1;
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_composicao_historico_aplicado_append_only
ON identidade.composicao_historico_aplicado INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51504,'Histórico de composição aplicado é append-only.',1;
END;
GO

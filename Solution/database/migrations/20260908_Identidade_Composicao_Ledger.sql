-- Ledger de preparação de composição V1. Não aplica identidade, vínculos ou Gold.
-- Requer a base normativa e a persistência progressiva. Instalação aditiva e repetível.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @installation_lock INT;
 EXEC @installation_lock=sys.sp_getapplock @Resource=N'JORNADA:COMPOSICAO:LEDGER:INSTALL:V1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @installation_lock<0 THROW 51400,'Não foi possível reservar a instalação do ledger.',1;
 IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL OR OBJECT_ID('identidade.pessoa','U') IS NULL
  THROW 51401,'A persistência progressiva deve estar instalada.',1;
 IF OBJECT_ID('identidade.composicao_uuid_reserva','U') IS NULL
 BEGIN
  CREATE TABLE identidade.composicao_uuid_reserva(
   reserva_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_composicao_uuid_reserva PRIMARY KEY,
   decision_id UNIQUEIDENTIFIER NOT NULL,
   pessoa_uuid UNIQUEIDENTIFIER NOT NULL CONSTRAINT uq_composicao_uuid_reserva_uuid UNIQUE,
   criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_composicao_uuid_reserva_criado DEFAULT(TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')),
   CONSTRAINT fk_composicao_uuid_reserva_pessoa FOREIGN KEY(pessoa_uuid) REFERENCES identidade.pessoa(pessoa_uuid),
   CONSTRAINT ck_composicao_uuid_reserva_ids CHECK(reserva_id<>'00000000-0000-0000-0000-000000000000' AND decision_id<>'00000000-0000-0000-0000-000000000000' AND pessoa_uuid<>'00000000-0000-0000-0000-000000000000')
  );
  CREATE INDEX ix_composicao_uuid_reserva_decisao ON identidade.composicao_uuid_reserva(decision_id);
 END;
 IF OBJECT_ID('identidade.composicao_plano','U') IS NULL
 BEGIN
  CREATE TABLE identidade.composicao_plano(
   decision_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_composicao_plano PRIMARY KEY,
   request_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   plan_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   reservas_hash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   request_json NVARCHAR(MAX) NOT NULL,
   plan_json NVARCHAR(MAX) NOT NULL,
   reservas_json NVARCHAR(MAX) NOT NULL,
   solicitante_referencia NVARCHAR(120) NOT NULL,
   correlation_id UNIQUEIDENTIFIER NULL,
   estado VARCHAR(20) NOT NULL CONSTRAINT df_composicao_plano_estado DEFAULT('PREPARADA'),
   registrado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_composicao_plano_registrado DEFAULT(TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')),
   CONSTRAINT ck_composicao_plano_estado CHECK(estado='PREPARADA'),
   CONSTRAINT ck_composicao_plano_json CHECK(ISJSON(request_json)=1 AND ISJSON(plan_json)=1 AND ISJSON(reservas_json)=1),
   CONSTRAINT ck_composicao_plano_solicitante CHECK(LEN(LTRIM(RTRIM(solicitante_referencia)))>0),
   CONSTRAINT ck_composicao_plano_hash CHECK(
    LEN(request_hash)=64 AND request_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND
    LEN(plan_hash)=64 AND plan_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND
    LEN(reservas_hash)=64 AND reservas_hash NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)
  );
 END;
 IF COL_LENGTH('identidade.composicao_uuid_reserva','pessoa_uuid') IS NULL OR
    COL_LENGTH('identidade.composicao_plano','reservas_hash') IS NULL OR
    COL_LENGTH('identidade.composicao_plano','estado') IS NULL
  THROW 51402,'Schema de ledger existente incompatível.',1;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO
CREATE OR ALTER TRIGGER identidade.tr_composicao_uuid_reserva_append_only
ON identidade.composicao_uuid_reserva INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51403,'Reserva de UUID é permanente e append-only.',1;
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_composicao_plano_append_only
ON identidade.composicao_plano INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51404,'Plano registrado é append-only.',1;
END;
GO
-- A reserva é feita pelo banco: nunca se aceita um UUID arbitrário do chamador.
-- Participa da transação externa, se houver. O mesmo reserva_id devolve o mesmo UUID.
CREATE OR ALTER PROCEDURE identidade.sp_reservar_uuid_composicao
 @decision_id UNIQUEIDENTIFIER,@reserva_id UNIQUEIDENTIFIER,@uuid_resultado UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @decision_id IS NULL OR @decision_id='00000000-0000-0000-0000-000000000000' OR
    @reserva_id IS NULL OR @reserva_id='00000000-0000-0000-0000-000000000000'
  THROW 51405,'Identificadores de reserva inválidos.',1;
 DECLARE @own BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 IF @own=1 BEGIN TRANSACTION;
 BEGIN TRY
  DECLARE @lock_result INT,@owner UNIQUEIDENTIFIER;
  EXEC @lock_result=sys.sp_getapplock @Resource=N'JORNADA:COMPOSICAO:DECISAO:'+CONVERT(NVARCHAR(36),@decision_id),@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
  IF @lock_result<0 THROW 51406,'Não foi possível serializar a decisão.',1;
  SET @uuid_resultado=NULL;
  SELECT @uuid_resultado=pessoa_uuid,@owner=decision_id FROM identidade.composicao_uuid_reserva WITH(UPDLOCK,HOLDLOCK) WHERE reserva_id=@reserva_id;
  IF @uuid_resultado IS NOT NULL
  BEGIN
   IF @owner<>@decision_id THROW 51407,'Reserva pertence a outra decisão.',1;
  END
  ELSE
  BEGIN
   IF EXISTS(SELECT 1 FROM identidade.composicao_plano WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision_id)
    THROW 51408,'Não é permitido acrescentar reservas após registrar o plano.',1;
   SET @uuid_resultado=NEWID();
   INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid_resultado,'ATIVO');
   INSERT identidade.composicao_uuid_reserva(reserva_id,decision_id,pessoa_uuid) VALUES(@reserva_id,@decision_id,@uuid_resultado);
  END;
  IF @own=1 COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF @own=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO
-- Hashes calculados sobre UTF-8 exato: o request_json é a serialização canônica
-- do IdentityCompositionPlanner; plan_json e reservas_json preservam o payload exato.
-- O registro não valida a evidência nem afirma que o plano foi aplicado.
CREATE OR ALTER PROCEDURE identidade.sp_registrar_plano_composicao
 @decision_id UNIQUEIDENTIFIER,@request_json NVARCHAR(MAX),@plan_json NVARCHAR(MAX),
 @reservas_json NVARCHAR(MAX),@solicitante_referencia NVARCHAR(120),@correlation_id UNIQUEIDENTIFIER=NULL,
 @request_hash CHAR(64) OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @decision_id IS NULL OR @decision_id='00000000-0000-0000-0000-000000000000' OR
    @request_json IS NULL OR @plan_json IS NULL OR @reservas_json IS NULL OR
    ISJSON(@request_json)<>1 OR ISJSON(@plan_json)<>1 OR ISJSON(@reservas_json)<>1 OR
    @solicitante_referencia IS NULL OR LTRIM(RTRIM(@solicitante_referencia))=''
  THROW 51409,'Registro de plano incompleto.',1;
 IF LEFT(LTRIM(@request_json),1)<>'{' OR LEFT(LTRIM(@plan_json),1)<>'{' OR LEFT(LTRIM(@reservas_json),1)<>'[' OR
    TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(@request_json,'$.DecisionId')) IS NULL OR
    TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(@request_json,'$.DecisionId'))<>@decision_id OR
    TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(@plan_json,'$.DecisionId')) IS NULL OR
    TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(@plan_json,'$.DecisionId'))<>@decision_id OR
    JSON_VALUE(@request_json,'$.Operation') IS NULL OR JSON_VALUE(@request_json,'$.Operation') NOT IN('0','1','2') OR
    NULLIF(LTRIM(RTRIM(JSON_VALUE(@request_json,'$.EvidenceReference'))),'') IS NULL OR
    NULLIF(LTRIM(RTRIM(JSON_VALUE(@request_json,'$.PolicyVersion'))),'') IS NULL OR
    JSON_VALUE(@request_json,'$.DecidedAt') IS NULL OR
    JSON_QUERY(@request_json,'$.Assignments') IS NULL OR
    JSON_QUERY(@plan_json,'$.Changes') IS NULL OR JSON_QUERY(@plan_json,'$.HistoryToAppend') IS NULL
  THROW 51410,'Payload de composição incompatível.',1;
 DECLARE @hash CHAR(64)=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),CONVERT(VARCHAR(MAX),@request_json COLLATE Latin1_General_100_BIN2_UTF8))),2));
 DECLARE @plan_hash CHAR(64)=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),CONVERT(VARCHAR(MAX),@plan_json COLLATE Latin1_General_100_BIN2_UTF8))),2));
 DECLARE @reservas_hash CHAR(64)=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),CONVERT(VARCHAR(MAX),@reservas_json COLLATE Latin1_General_100_BIN2_UTF8))),2));
 IF JSON_VALUE(@plan_json,'$.RequestHash') IS NULL OR
    JSON_VALUE(@plan_json,'$.RequestHash') COLLATE Latin1_General_100_BIN2<>@hash OR
    DATALENGTH(JSON_VALUE(@plan_json,'$.RequestHash'))<>128
  THROW 51411,'Hash do plano não corresponde à decisão.',1;
 DECLARE @own BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 IF @own=1 BEGIN TRANSACTION;
 BEGIN TRY
  DECLARE @lock_result INT,@old_hash CHAR(64),@old_plan CHAR(64),@old_reservas CHAR(64);
  EXEC @lock_result=sys.sp_getapplock @Resource=N'JORNADA:COMPOSICAO:DECISAO:'+CONVERT(NVARCHAR(36),@decision_id),@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
  IF @lock_result<0 THROW 51406,'Não foi possível serializar a decisão.',1;
  SELECT @old_hash=request_hash,@old_plan=plan_hash,@old_reservas=reservas_hash
   FROM identidade.composicao_plano WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision_id;
  IF @old_hash IS NOT NULL
  BEGIN
   IF @old_hash<>@hash OR @old_plan<>@plan_hash OR @old_reservas<>@reservas_hash
    THROW 51412,'decision_id reutilizado com conteúdo diferente.',1;
  END
  ELSE
  BEGIN
   DECLARE @reservas TABLE(pessoa_uuid UNIQUEIDENTIFIER NULL);
   INSERT @reservas(pessoa_uuid) SELECT TRY_CONVERT(UNIQUEIDENTIFIER,[value]) FROM OPENJSON(@reservas_json);
   IF EXISTS(SELECT 1 FROM @reservas WHERE pessoa_uuid IS NULL OR pessoa_uuid='00000000-0000-0000-0000-000000000000') OR
      EXISTS(SELECT pessoa_uuid FROM @reservas GROUP BY pessoa_uuid HAVING COUNT(*)>1) OR
      EXISTS(SELECT 1 FROM @reservas r LEFT JOIN identidade.composicao_uuid_reserva x WITH(UPDLOCK,HOLDLOCK)
             ON x.pessoa_uuid=r.pessoa_uuid AND x.decision_id=@decision_id WHERE x.reserva_id IS NULL) OR
      EXISTS(SELECT 1 FROM identidade.composicao_uuid_reserva x WITH(UPDLOCK,HOLDLOCK)
             WHERE x.decision_id=@decision_id AND NOT EXISTS(SELECT 1 FROM @reservas r WHERE r.pessoa_uuid=x.pessoa_uuid))
    THROW 51413,'Conjunto de reservas não corresponde à decisão.',1;
   INSERT identidade.composicao_plano(decision_id,request_hash,plan_hash,reservas_hash,request_json,plan_json,reservas_json,solicitante_referencia,correlation_id)
   VALUES(@decision_id,@hash,@plan_hash,@reservas_hash,@request_json,@plan_json,@reservas_json,@solicitante_referencia,@correlation_id);
  END;
  SET @request_hash=@hash;
  IF @own=1 COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF @own=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO
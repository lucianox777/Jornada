/* DT-05: pins Bronze por execução de replay. A gravação de cada pin
   deve ocorrer enquanto a entrega está DISPONIVEL, sob o mesmo applock
   Jornada.Bronze.Object.<sha256> usado por ingestão e GC. */
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'identidade.linkage_bronze_pin',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_bronze_pin(
   linkage_run_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.linkage_run(linkage_run_id),
   objeto_chave NVARCHAR(1024) NOT NULL,
   payload_sha256 CHAR(64) NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
   CONSTRAINT PK_linkage_bronze_pin PRIMARY KEY(linkage_run_id,objeto_chave),
   CONSTRAINT CK_linkage_bronze_pin_sha CHECK(
     LEN(payload_sha256)=64 AND payload_sha256 NOT LIKE '%[^0-9a-fA-F]%')
 );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'identidade.linkage_bronze_pin') AND name=N'IX_linkage_bronze_pin_objeto')
 CREATE INDEX IX_linkage_bronze_pin_objeto ON identidade.linkage_bronze_pin(payload_sha256,objeto_chave);
GO
CREATE OR ALTER PROCEDURE identidade.sp_fixar_bronze_para_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @objeto_chave NVARCHAR(1024),
 @payload_sha256 CHAR(64)
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @objeto_chave IS NULL OR @payload_sha256 IS NULL OR LEN(@payload_sha256)<>64
    OR @payload_sha256 LIKE '%[^0-9a-fA-F]%'
    THROW 51950,N'Objeto Bronze ou SHA inválido.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run WHERE linkage_run_id=@linkage_run_id AND status IN(N'PREPARANDO',N'EXECUTANDO'))
    THROW 51951,N'Somente run ativo pode fixar referências Bronze.',1;
 DECLARE @resource NVARCHAR(255)=N'Jornada.Bronze.Object.'+LOWER(@payload_sha256);
 DECLARE @lock INT;
 BEGIN TRAN;
 EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=60000;
 IF @lock<0 BEGIN ROLLBACK; THROW 51952,N'Falha ao adquirir lock Bronze.',1; END;
 IF NOT EXISTS(
   SELECT 1 FROM bronze.entrega_arquivo WITH(HOLDLOCK)
   WHERE objeto_chave=@objeto_chave AND payload_sha256=@payload_sha256
     AND estado_armazenamento=N'DISPONIVEL')
 BEGIN ROLLBACK; THROW 51953,N'Objeto Bronze não disponível para pin.',1; END;
 IF EXISTS(SELECT 1 FROM identidade.linkage_bronze_pin WITH(UPDLOCK,HOLDLOCK)
           WHERE linkage_run_id=@linkage_run_id AND objeto_chave=@objeto_chave AND payload_sha256<>@payload_sha256)
 BEGIN ROLLBACK; THROW 51954,N'Pin conflitante.',1; END;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_bronze_pin WITH(UPDLOCK,HOLDLOCK)
               WHERE linkage_run_id=@linkage_run_id AND objeto_chave=@objeto_chave)
   INSERT identidade.linkage_bronze_pin(linkage_run_id,objeto_chave,payload_sha256)
   VALUES(@linkage_run_id,@objeto_chave,LOWER(@payload_sha256));
 COMMIT;
END;
GO

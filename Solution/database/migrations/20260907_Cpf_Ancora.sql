-- Âncora CPF imutável: migração opt-in, versão de armazenamento V1.
-- Aplica-se somente sobre a base normativa já instalada.
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
-- A função é referenciada pela CHECK constraint da âncora. SQL Server não permite
-- CREATE OR ALTER de uma função enquanto essa dependência existe; por isso V1 cria
-- somente quando ausente. Instalações repetidas preservam função e constraint.
IF OBJECT_ID('identidade.fn_cpf_ancora_valido','FN') IS NULL
 EXEC(N'CREATE FUNCTION identidade.fn_cpf_ancora_valido(@cpf CHAR(11)) RETURNS BIT
 AS
 BEGIN
  IF @cpf IS NULL OR LEN(@cpf)<>11 OR @cpf COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9]%'' RETURN 0;
  IF @cpf=REPLICATE(LEFT(@cpf,1),11) RETURN 0;
  DECLARE @i INT=1,@sum INT=0,@digit INT,@expected INT;
  WHILE @i<=9
  BEGIN
   SET @sum=@sum+CONVERT(INT,SUBSTRING(@cpf,@i,1))*(11-@i);
   SET @i=@i+1;
  END;
  SET @digit=@sum%11;
  SET @expected=CASE WHEN @digit<2 THEN 0 ELSE 11-@digit END;
  IF @expected<>CONVERT(INT,SUBSTRING(@cpf,10,1)) RETURN 0;
  SET @i=1; SET @sum=0;
  WHILE @i<=10
  BEGIN
   SET @sum=@sum+CONVERT(INT,SUBSTRING(@cpf,@i,1))*(12-@i);
   SET @i=@i+1;
  END;
  SET @digit=@sum%11;
  SET @expected=CASE WHEN @digit<2 THEN 0 ELSE 11-@digit END;
  IF @expected<>CONVERT(INT,SUBSTRING(@cpf,11,1)) RETURN 0;
  RETURN 1;
 END;');
GO
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock_result INT;
 EXEC @lock_result=sys.sp_getapplock @Resource=N'JORNADA:CPF_ANCORA:V1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lock_result<0 THROW 51340,'Não foi possível reservar a migração da âncora CPF.',1;
 IF OBJECT_ID('identidade.pessoa','U') IS NULL OR OBJECT_ID('identidade.identity_map','U') IS NULL
  THROW 51341,'Instale a base normativa antes da âncora CPF.',1;
 DECLARE @locked BIGINT;
 SELECT @locked=COUNT_BIG(*) FROM identidade.identity_map WITH(TABLOCKX,HOLDLOCK);
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' AND
    (identificador IS NULL OR identidade.fn_cpf_ancora_valido(CONVERT(CHAR(11),identificador))=0 OR
     LEN(identificador)<>11 OR pessoa_uuid IS NULL OR pessoa_uuid='00000000-0000-0000-0000-000000000000'))
  THROW 51342,'Histórico CPF inválido: reconciliação explícita necessária.',1;
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' GROUP BY identificador HAVING COUNT(DISTINCT pessoa_uuid)>1)
  THROW 51343,'Um CPF possui UUIDs históricos distintos; migração interrompida.',1;
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' GROUP BY pessoa_uuid HAVING COUNT(DISTINCT identificador)>1)
  THROW 51344,'Um UUID possui CPFs históricos distintos; migração interrompida.',1;
 IF OBJECT_ID('identidade.cpf_ancora','U') IS NULL
 BEGIN
  CREATE TABLE identidade.cpf_ancora(
   cpf CHAR(11) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT pk_cpf_ancora PRIMARY KEY,
   pessoa_uuid UNIQUEIDENTIFIER NOT NULL CONSTRAINT uq_cpf_ancora_uuid UNIQUE,
   criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_cpf_ancora_criado DEFAULT(SYSDATETIMEOFFSET()),
   CONSTRAINT fk_cpf_ancora_pessoa FOREIGN KEY(pessoa_uuid) REFERENCES identidade.pessoa(pessoa_uuid),
   CONSTRAINT ck_cpf_ancora_valido CHECK(identidade.fn_cpf_ancora_valido(cpf)=1),
   CONSTRAINT ck_cpf_ancora_uuid CHECK(pessoa_uuid<>'00000000-0000-0000-0000-000000000000'));
 END;
 -- O mapa legado pode herdar a collation da base. Toda comparação com a âncora
 -- normaliza explicitamente para a collation binária da chave permanente.
 IF EXISTS(SELECT 1 FROM identidade.cpf_ancora a JOIN identidade.identity_map m ON m.tipo='CPF' AND m.identificador COLLATE Latin1_General_100_BIN2=a.cpf WHERE m.pessoa_uuid<>a.pessoa_uuid)
  THROW 51345,'Âncora existente diverge do histórico; não será sobrescrita.',1;
 IF EXISTS(SELECT 1 FROM identidade.cpf_ancora a JOIN identidade.identity_map m ON m.tipo='CPF' AND m.pessoa_uuid=a.pessoa_uuid WHERE m.identificador COLLATE Latin1_General_100_BIN2<>a.cpf)
  THROW 51346,'UUID reservado possui outro CPF histórico.',1;
 INSERT identidade.cpf_ancora(cpf,pessoa_uuid)
 SELECT DISTINCT CONVERT(CHAR(11),m.identificador) COLLATE Latin1_General_100_BIN2,m.pessoa_uuid
 FROM identidade.identity_map m
 WHERE m.tipo='CPF' AND NOT EXISTS(
   SELECT 1 FROM identidade.cpf_ancora a WITH(UPDLOCK,HOLDLOCK)
   WHERE a.cpf=m.identificador COLLATE Latin1_General_100_BIN2);
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO
CREATE OR ALTER TRIGGER identidade.tr_cpf_ancora_imutavel ON identidade.cpf_ancora
AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51347,'Âncora CPF imutável: correções devem reatribuir registros, não transferir a âncora.',1;
END;
GO
-- A correção governada pode separar/reassociar observações e fatos, mas o grupo
-- declarado como titular do CPF deve continuar apontando para a âncora permanente.
-- A trava fica no cabeçalho auditável da correção para proteger todos os chamadores
-- sem introduzir trigger no identity_map, que possui writers com OUTPUT INSERTED.
IF OBJECT_ID('identidade.correcao_identidade','U') IS NOT NULL
 EXEC(N'CREATE OR ALTER TRIGGER identidade.tr_correcao_identidade_cpf_ancora
 ON identidade.correcao_identidade
 AFTER INSERT,UPDATE
 AS
 BEGIN
  SET NOCOUNT ON;
  IF EXISTS(
   SELECT 1
   FROM inserted i
   JOIN identidade.identity_map m ON m.identity_map_id=i.identity_map_origem_id AND m.tipo=''CPF''
   LEFT JOIN identidade.cpf_ancora a
     ON a.cpf=CONVERT(CHAR(11),m.identificador) COLLATE Latin1_General_100_BIN2
   WHERE a.pessoa_uuid IS NULL OR a.pessoa_uuid<>i.pessoa_uuid_titular
  )
   THROW 51360,''Correção governada não pode transferir a âncora CPF permanente.'',1;
 END;');
GO
CREATE OR ALTER PROCEDURE identidade.sp_obter_cpf_ancora @cpf NVARCHAR(64)
AS
BEGIN
 SET NOCOUNT ON;
 IF @cpf IS NULL OR LEN(@cpf)<>11 OR DATALENGTH(@cpf)<>22 OR identidade.fn_cpf_ancora_valido(CONVERT(CHAR(11),@cpf))=0 THROW 51348,'CPF inválido.',1;
 SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=CONVERT(CHAR(11),@cpf) COLLATE Latin1_General_100_BIN2;
END;
GO
-- Reserva explícita; o chamador cria Pessoa e vínculo na mesma transação.
-- Não escolhe UUID por score e nunca altera uma âncora existente.
CREATE OR ALTER PROCEDURE identidade.sp_reservar_cpf_ancora
 @cpf NVARCHAR(64), @pessoa_uuid UNIQUEIDENTIFIER, @uuid_resultado UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @cpf IS NULL OR LEN(@cpf)<>11 OR DATALENGTH(@cpf)<>22 OR identidade.fn_cpf_ancora_valido(CONVERT(CHAR(11),@cpf))=0
  THROW 51348,'CPF inválido.',1;
 IF @pessoa_uuid IS NULL OR @pessoa_uuid='00000000-0000-0000-0000-000000000000'
  THROW 51355,'UUID de reserva inválido.',1;
 DECLARE @own BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 IF @own=1 BEGIN TRANSACTION;
 BEGIN TRY
  SET @uuid_resultado=NULL;
  SELECT @uuid_resultado=pessoa_uuid FROM identidade.cpf_ancora WITH(UPDLOCK,HOLDLOCK)
   WHERE cpf=CONVERT(CHAR(11),@cpf) COLLATE Latin1_General_100_BIN2;
  IF @uuid_resultado IS NOT NULL AND @uuid_resultado<>@pessoa_uuid
   THROW 51353,'CPF já possui outro UUID permanente.',1;
  IF @uuid_resultado IS NULL
  BEGIN
   IF EXISTS(SELECT 1 FROM identidade.cpf_ancora WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_uuid=@pessoa_uuid)
    THROW 51354,'UUID já possui outro CPF permanente.',1;
   INSERT identidade.cpf_ancora(cpf,pessoa_uuid) VALUES(CONVERT(CHAR(11),@cpf) COLLATE Latin1_General_100_BIN2,@pessoa_uuid);
   SET @uuid_resultado=@pessoa_uuid;
  END;
  IF @own=1 COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF @own=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO

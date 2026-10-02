/* DT-05 Marco B: vínculo imutável entre linkage_run e manifesto NAS publicado.
   Este contrato registra metadados/hashes; a publicação física do manifesto continua
   responsabilidade do orquestrador/Runner e deve ocorrer antes desta procedure. */
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'identidade.linkage_replay_manifest',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_replay_manifest(
   linkage_run_id UNIQUEIDENTIFIER NOT NULL
     CONSTRAINT PK_linkage_replay_manifest PRIMARY KEY
     REFERENCES identidade.linkage_run(linkage_run_id),
   schema_version INT NOT NULL,
   manifest_path NVARCHAR(1024) NOT NULL,
   manifest_sha256 CHAR(64) NOT NULL,
   bronze_set_sha256 CHAR(64) NOT NULL,
   parent_manifest_sha256 CHAR(64) NULL,
   versions_json NVARCHAR(MAX) NOT NULL,
   objetos_bronze BIGINT NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT DF_linkage_replay_manifest_registrado DEFAULT SYSUTCDATETIME(),
   CONSTRAINT UQ_linkage_replay_manifest_path UNIQUE(manifest_path),
   CONSTRAINT UQ_linkage_replay_manifest_sha UNIQUE(manifest_sha256),
   CONSTRAINT CK_linkage_replay_manifest_schema CHECK(schema_version=1),
   CONSTRAINT CK_linkage_replay_manifest_objetos CHECK(objetos_bronze>=0),
   CONSTRAINT CK_linkage_replay_manifest_sha CHECK(
     LEN(manifest_sha256)=64 AND manifest_sha256 NOT LIKE '%[^0-9a-fA-F]%'
     AND LEN(bronze_set_sha256)=64 AND bronze_set_sha256 NOT LIKE '%[^0-9a-fA-F]%'
     AND (parent_manifest_sha256 IS NULL OR
          (LEN(parent_manifest_sha256)=64 AND parent_manifest_sha256 NOT LIKE '%[^0-9a-fA-F]%'))),
   CONSTRAINT CK_linkage_replay_manifest_versions CHECK(ISJSON(versions_json)=1),
   CONSTRAINT CK_linkage_replay_manifest_path CHECK(
     manifest_path LIKE N'linkage-snapshots/v1/%'
     AND manifest_path NOT LIKE N'%..%'
     AND manifest_path NOT LIKE N'%\\%')
 );
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_replay_manifest_append_only
ON identidade.linkage_replay_manifest
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51969,N'DT-05: vínculo de manifesto de replay é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE identidade.sp_registrar_manifesto_replay_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @schema_version INT,
 @manifest_path NVARCHAR(1024),
 @manifest_sha256 CHAR(64),
 @bronze_set_sha256 CHAR(64),
 @parent_manifest_sha256 CHAR(64)=NULL,
 @versions_json NVARCHAR(MAX),
 @objetos_bronze BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF @schema_version<>1
   THROW 51963,N'DT-05: schema_version de manifesto não suportado.',1;
 IF NULLIF(LTRIM(RTRIM(@manifest_path)),N'') IS NULL
    OR @manifest_path NOT LIKE N'linkage-snapshots/v1/%'
    OR @manifest_path LIKE N'%..%' OR @manifest_path LIKE N'%\%'
   THROW 51964,N'DT-05: caminho lógico de manifesto inválido.',1;
 IF @manifest_sha256 IS NULL OR LEN(@manifest_sha256)<>64 OR @manifest_sha256 LIKE '%[^0-9a-fA-F]%'
    OR @bronze_set_sha256 IS NULL OR LEN(@bronze_set_sha256)<>64 OR @bronze_set_sha256 LIKE '%[^0-9a-fA-F]%'
    OR (@parent_manifest_sha256 IS NOT NULL AND
        (LEN(@parent_manifest_sha256)<>64 OR @parent_manifest_sha256 LIKE '%[^0-9a-fA-F]%'))
   THROW 51965,N'DT-05: SHA-256 do manifesto/corpus inválido.',1;
 IF ISJSON(@versions_json)<>1
    OR JSON_VALUE(@versions_json,'$.scorer_version') IS NULL
    OR JSON_VALUE(@versions_json,'$.ruleset_version') IS NULL
    OR JSON_VALUE(@versions_json,'$.model_version') IS NULL
    OR JSON_VALUE(@versions_json,'$.input_snapshot_id') IS NULL
   THROW 51966,N'DT-05: versões históricas obrigatórias ausentes.',1;
 IF @objetos_bronze<0
   THROW 51967,N'DT-05: contagem de objetos Bronze inválida.',1;

 DECLARE @status NVARCHAR(30),@capturados BIGINT,@pins BIGINT;
 SELECT @status=status FROM identidade.linkage_run WITH(HOLDLOCK)
 WHERE linkage_run_id=@linkage_run_id;
 IF @status<>N'EXECUTANDO'
   THROW 51968,N'DT-05: manifesto só pode ser vinculado a run EXECUTANDO.',1;

 SELECT @capturados=objetos_bronze
 FROM identidade.linkage_bronze_captura WITH(HOLDLOCK)
 WHERE linkage_run_id=@linkage_run_id;
 IF @capturados IS NULL OR @capturados<>@objetos_bronze
   THROW 51967,N'DT-05: manifesto diverge da captura Bronze do run.',1;

 SELECT @pins=COUNT_BIG(*) FROM identidade.linkage_bronze_pin WITH(HOLDLOCK)
 WHERE linkage_run_id=@linkage_run_id;
 IF @pins<>@objetos_bronze
   THROW 51967,N'DT-05: manifesto diverge dos pins Bronze do run.',1;

 IF EXISTS(SELECT 1 FROM identidade.linkage_replay_manifest WITH(UPDLOCK,HOLDLOCK)
           WHERE linkage_run_id=@linkage_run_id)
   THROW 51969,N'DT-05: run já possui manifesto imutável vinculado.',1;

 INSERT identidade.linkage_replay_manifest(
   linkage_run_id,schema_version,manifest_path,manifest_sha256,bronze_set_sha256,
   parent_manifest_sha256,versions_json,objetos_bronze)
 VALUES(
   @linkage_run_id,@schema_version,@manifest_path,LOWER(@manifest_sha256),LOWER(@bronze_set_sha256),
   LOWER(@parent_manifest_sha256),@versions_json,@objetos_bronze);
END;
GO

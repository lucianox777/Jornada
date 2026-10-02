/* DT-05 Marco B: bind an already-published immutable NAS manifest to one linkage run.
   The caller must verify/publish the manifest bytes first, then register this SQL reference.
   This migration does not enable historical replay automatically. */
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'identidade.linkage_replay_manifesto',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_replay_manifesto(
   linkage_run_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
     REFERENCES identidade.linkage_run(linkage_run_id),
   schema_version INT NOT NULL,
   caminho_logico NVARCHAR(1024) NOT NULL,
   manifesto_sha256 CHAR(64) NOT NULL,
   bronze_set_sha256 CHAR(64) NOT NULL,
   scorer_version NVARCHAR(120) NOT NULL,
   ruleset_version NVARCHAR(120) NOT NULL,
   model_version NVARCHAR(120) NOT NULL,
   input_snapshot_id NVARCHAR(200) NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT DF_linkage_replay_manifesto_registrado_em DEFAULT SYSUTCDATETIME(),
   CONSTRAINT UQ_linkage_replay_manifesto_caminho UNIQUE(caminho_logico),
   CONSTRAINT UQ_linkage_replay_manifesto_sha UNIQUE(manifesto_sha256),
   CONSTRAINT CK_linkage_replay_manifesto_schema CHECK(schema_version=1),
   CONSTRAINT CK_linkage_replay_manifesto_sha CHECK(
     LEN(manifesto_sha256)=64 AND manifesto_sha256 NOT LIKE '%[^0-9a-f]%'),
   CONSTRAINT CK_linkage_replay_bronze_set_sha CHECK(
     LEN(bronze_set_sha256)=64 AND bronze_set_sha256 NOT LIKE '%[^0-9a-f]%')
 );
END;
GO
CREATE OR ALTER PROCEDURE identidade.sp_registrar_manifesto_replay_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @schema_version INT,
 @caminho_logico NVARCHAR(1024),
 @manifesto_sha256 CHAR(64),
 @bronze_set_sha256 CHAR(64),
 @scorer_version NVARCHAR(120),
 @ruleset_version NVARCHAR(120),
 @model_version NVARCHAR(120),
 @input_snapshot_id NVARCHAR(200)
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF XACT_STATE()=0
   THROW 51970,N'DT-05: registro do manifesto exige transação explícita.',1;
 IF @schema_version<>1
   THROW 51971,N'DT-05: schema_version de manifesto não suportada.',1;
 IF @caminho_logico IS NULL
    OR @caminho_logico NOT LIKE N'linkage-snapshots/v1/manifests/%'
    OR @caminho_logico LIKE N'%..%'
    OR LEFT(@caminho_logico,1) IN (N'/',N'\')
   THROW 51972,N'DT-05: caminho lógico de manifesto inválido.',1;
 IF LEN(@manifesto_sha256)<>64 OR @manifesto_sha256 LIKE '%[^0-9a-f]%'
    OR LEN(@bronze_set_sha256)<>64 OR @bronze_set_sha256 LIKE '%[^0-9a-f]%'
   THROW 51973,N'DT-05: SHA-256 inválido.',1;
 IF NULLIF(LTRIM(RTRIM(@scorer_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@ruleset_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@model_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@input_snapshot_id)),N'') IS NULL
   THROW 51974,N'DT-05: versões históricas exatas são obrigatórias.',1;

 IF NOT EXISTS(
   SELECT 1 FROM identidade.linkage_run
   WHERE linkage_run_id=@linkage_run_id)
   THROW 51975,N'DT-05: linkage_run inexistente.',1;
 IF NOT EXISTS(
   SELECT 1 FROM identidade.linkage_bronze_captura
   WHERE linkage_run_id=@linkage_run_id)
   THROW 51976,N'DT-05: captura Bronze deve existir antes do manifesto.',1;
 IF EXISTS(
   SELECT 1 FROM identidade.linkage_replay_manifesto
   WHERE linkage_run_id=@linkage_run_id)
   THROW 51977,N'DT-05: manifesto do run é imutável e já foi registrado.',1;

 DECLARE @capturados BIGINT=(SELECT objetos_bronze FROM identidade.linkage_bronze_captura
                             WHERE linkage_run_id=@linkage_run_id);
 DECLARE @pins BIGINT=(SELECT COUNT_BIG(*) FROM identidade.linkage_bronze_pin
                       WHERE linkage_run_id=@linkage_run_id);
 IF @capturados<>@pins
   THROW 51978,N'DT-05: conjunto de pins Bronze incompleto para o run.',1;

 INSERT identidade.linkage_replay_manifesto(
   linkage_run_id,schema_version,caminho_logico,manifesto_sha256,bronze_set_sha256,
   scorer_version,ruleset_version,model_version,input_snapshot_id)
 VALUES(
   @linkage_run_id,@schema_version,@caminho_logico,@manifesto_sha256,@bronze_set_sha256,
   @scorer_version,@ruleset_version,@model_version,@input_snapshot_id);
END;
GO

/* DT-05 Marco B: bind immutable candidate/governance identity into replay manifest v3. */
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'identidade.linkage_replay_manifesto',N'candidatos_referencia') IS NULL
BEGIN
 ALTER TABLE identidade.linkage_replay_manifesto ADD
   candidatos_referencia BIGINT NULL,
   candidatos_sha256 CHAR(64) NULL,
   governanca_evento_high_watermark BIGINT NULL;
END;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_linkage_replay_manifesto_schema')
 ALTER TABLE identidade.linkage_replay_manifesto DROP CONSTRAINT CK_linkage_replay_manifesto_schema;
GO
ALTER TABLE identidade.linkage_replay_manifesto ADD CONSTRAINT CK_linkage_replay_manifesto_schema CHECK(schema_version IN(1,2,3));
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
 @input_snapshot_id NVARCHAR(200),
 @normalization_version NVARCHAR(120)=NULL,
 @resolution_catalog_version NVARCHAR(120)=NULL,
 @projection_schema_version NVARCHAR(120)=NULL,
 @projection_fingerprint_sha256 CHAR(64)=NULL,
 @candidatos_referencia BIGINT=NULL,
 @candidatos_sha256 CHAR(64)=NULL,
 @governanca_evento_high_watermark BIGINT=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF XACT_STATE()=0 THROW 51970,N'DT-05: registro do manifesto exige transação explícita.',1;
 IF @schema_version NOT IN(1,2,3) THROW 51971,N'DT-05: schema_version de manifesto não suportada.',1;
 IF @caminho_logico IS NULL OR @caminho_logico NOT LIKE N'linkage-snapshots/v1/manifests/%'
    OR @caminho_logico LIKE N'%..%' OR LEFT(@caminho_logico,1) IN (N'/',N'\')
   THROW 51972,N'DT-05: caminho lógico de manifesto inválido.',1;
 IF LEN(@manifesto_sha256)<>64 OR @manifesto_sha256 LIKE '%[^0-9a-f]%'
    OR LEN(@bronze_set_sha256)<>64 OR @bronze_set_sha256 LIKE '%[^0-9a-f]%'
   THROW 51973,N'DT-05: SHA-256 inválido.',1;
 IF NULLIF(LTRIM(RTRIM(@scorer_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@ruleset_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@model_version)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@input_snapshot_id)),N'') IS NULL
   THROW 51974,N'DT-05: versões históricas exatas são obrigatórias.',1;
 IF @schema_version>=2 AND (
    NULLIF(LTRIM(RTRIM(@normalization_version)),N'') IS NULL OR
    NULLIF(LTRIM(RTRIM(@resolution_catalog_version)),N'') IS NULL OR
    NULLIF(LTRIM(RTRIM(@projection_schema_version)),N'') IS NULL OR
    LEN(@projection_fingerprint_sha256)<>64 OR @projection_fingerprint_sha256 LIKE '%[^0-9a-f]%')
   THROW 51979,N'DT-05: schema v2+ exige contratos executáveis exatos.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run WHERE linkage_run_id=@linkage_run_id)
   THROW 51975,N'DT-05: linkage_run inexistente.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_bronze_captura WHERE linkage_run_id=@linkage_run_id)
   THROW 51976,N'DT-05: captura Bronze deve existir antes do manifesto.',1;
 IF EXISTS(SELECT 1 FROM identidade.linkage_replay_manifesto WHERE linkage_run_id=@linkage_run_id)
   THROW 51977,N'DT-05: manifesto do run é imutável e já foi registrado.',1;
 DECLARE @capturados BIGINT=(SELECT objetos_bronze FROM identidade.linkage_bronze_captura WHERE linkage_run_id=@linkage_run_id);
 DECLARE @pins BIGINT=(SELECT COUNT_BIG(*) FROM identidade.linkage_bronze_pin WHERE linkage_run_id=@linkage_run_id);
 IF @capturados<>@pins THROW 51978,N'DT-05: conjunto de pins Bronze incompleto para o run.',1;
 IF @schema_version=3
 BEGIN
   IF @candidatos_referencia IS NULL OR @candidatos_referencia<0
      OR LEN(@candidatos_sha256)<>64 OR @candidatos_sha256 LIKE '%[^0-9a-f]%'
      OR @governanca_evento_high_watermark IS NULL OR @governanca_evento_high_watermark<0
     THROW 51984,N'DT-05: schema v3 exige identidade de candidatos/governança.',1;
   IF NOT EXISTS(
      SELECT 1 FROM identidade.linkage_replay_estado_governanca
       WHERE linkage_run_id=@linkage_run_id
         AND candidatos_referencia=@candidatos_referencia
         AND candidatos_sha256=@candidatos_sha256
         AND governanca_evento_high_watermark=@governanca_evento_high_watermark)
     THROW 51985,N'DT-05: identidade de candidatos/governança diverge da captura create-once.',1;
 END;
 INSERT identidade.linkage_replay_manifesto(
   linkage_run_id,schema_version,caminho_logico,manifesto_sha256,bronze_set_sha256,
   scorer_version,ruleset_version,model_version,input_snapshot_id,
   normalization_version,resolution_catalog_version,projection_schema_version,projection_fingerprint_sha256,
   candidatos_referencia,candidatos_sha256,governanca_evento_high_watermark)
 VALUES(@linkage_run_id,@schema_version,@caminho_logico,@manifesto_sha256,@bronze_set_sha256,
   @scorer_version,@ruleset_version,@model_version,@input_snapshot_id,
   @normalization_version,@resolution_catalog_version,@projection_schema_version,@projection_fingerprint_sha256,
   @candidatos_referencia,@candidatos_sha256,@governanca_evento_high_watermark);
END;
GO

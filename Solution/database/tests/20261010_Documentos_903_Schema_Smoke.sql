SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
-- #903: executar SOMENTE em SQL Server descartavel apos as duas migracoes.
-- O rollback protege o fixture; nao aplicar em HML/PRD.
BEGIN TRANSACTION;
BEGIN TRY
 IF OBJECT_ID(N'ref.tipo_documento',N'U') IS NULL
    OR OBJECT_ID(N'ref.modelo_documento',N'U') IS NULL
    OR OBJECT_ID(N'ref.modelo_documento_atributo',N'U') IS NULL
    OR OBJECT_ID(N'silver.documento_evidencia_observacao',N'U') IS NULL
    OR OBJECT_ID(N'silver.documento_evidencia_valor',N'U') IS NULL
    THROW 52310,'Estruturas documentais ausentes.',1;

 IF NOT EXISTS(SELECT 1 FROM ref.tipo_documento WHERE tipo_documento_codigo=N'RG')
    OR NOT EXISTS(SELECT 1 FROM ref.tipo_documento WHERE tipo_documento_codigo=N'CIN')
    THROW 52311,'Bootstrap documental RG/CIN ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys
   WHERE name=N'fk_documento_evidencia_valor_cobertura'
     AND parent_object_id=OBJECT_ID(N'silver.documento_evidencia_valor'))
    THROW 52312,'FK de cobertura documental ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys
   WHERE name=N'fk_documento_evidencia_valor_instancia'
     AND parent_object_id=OBJECT_ID(N'silver.documento_evidencia_valor'))
    THROW 52313,'FK da instancia documental ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
   WHERE name=N'ck_modelo_documento_publicacao'
     AND parent_object_id=OBJECT_ID(N'ref.modelo_documento'))
    THROW 52314,'Restricao de publicacao documental ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.triggers
   WHERE name=N'tr_modelo_documento_publicado_imutavel'
     AND parent_id=OBJECT_ID(N'ref.modelo_documento'))
    THROW 52315,'Imutabilidade de modelo publicado ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.triggers
   WHERE name=N'tr_modelo_documento_atributo_publicado_imutavel'
     AND parent_id=OBJECT_ID(N'ref.modelo_documento_atributo'))
    THROW 52316,'Imutabilidade de atributos publicados ausente.',1;

 IF EXISTS(SELECT 1 FROM sys.columns
   WHERE object_id=OBJECT_ID(N'silver.documento_evidencia_valor')
     AND name IN(N'data_evidencia',N'data_atendimento'))
    THROW 52317,'Data documental nao pode ser duplicada por atributo.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.triggers
   WHERE name=N'tr_documento_evidencia_valor_admissivel'
     AND parent_id=OBJECT_ID(N'silver.documento_evidencia_valor'))
    THROW 52319,'Trigger de admissibilidade documental ausente.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'ck_documento_evidencia_classe' AND parent_object_id=OBJECT_ID(N'silver.documento_evidencia_observacao')) THROW 52320,'Classe de evidencia sem dominio validado.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'ck_documento_evidencia_status' AND parent_object_id=OBJECT_ID(N'silver.documento_evidencia_observacao')) THROW 52321,'Status documental sem dominio validado.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name=N'uq_documento_evidencia_origem' AND parent_object_id=OBJECT_ID(N'silver.documento_evidencia_observacao')) THROW 52322,'Idempotencia de origem documental ausente.',1;
 IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.documento_evidencia_observacao') AND name=N'data_evidencia' AND is_nullable=1) THROW 52323,'Data propria opcional nao preservada.',1;

 IF NOT EXISTS(SELECT 1 FROM sys.triggers WHERE name=N'tr_documento_evidencia_modelo_publicado' AND parent_id=OBJECT_ID(N'silver.documento_evidencia_observacao')) THROW 52325,'Gate de modelo documental publicado ausente.',1;

 -- Gate de compilacao e estrutura das views RG 1:N instaladas. Este smoke
 -- NAO substitui fixture comportamental com registros reais de CI descartavel.
 IF OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold',N'V') IS NULL
    OR OBJECT_ID(N'gold.vw_documento_evidencia_candidato_uuid',N'V') IS NULL
    OR OBJECT_ID(N'gold.vw_rg_documento_distinto',N'V') IS NULL
    OR OBJECT_ID(N'gold.vw_rg_documento_proveniencia',N'V') IS NULL
    THROW 52326,'Views Silver/Gold documentais nao instaladas na CI SQL.',1;
 IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
    WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_distinto')
      AND definition LIKE N'%COUNT_BIG(DISTINCT documento_evidencia_id)%'
      AND definition LIKE N'%GROUP BY pessoa_uuid, numero_rg, emissor_codigo, uf_emissor%')
    THROW 52327,'Colecao RG nao preserva chave 1:N e deduplicacao de ocorrencias.',1;
 IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
    WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_proveniencia')
      AND UPPER(definition) LIKE N'%SELECT DISTINCT D.PESSOA_UUID%'
      AND definition LIKE N'%c.documento_evidencia_id%'
      AND definition LIKE N'%c.ocorrencia_origem_codigo%')
    THROW 52328,'Proveniencia RG nao preserva ocorrencias distintas.',1;

 PRINT N'PASSOU: estruturas, RG/CIN, FKs, views Gold 1:N e proveniencia (smoke estrutural, nao E2E).';
 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO

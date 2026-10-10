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

 -- Comportamento: rejeitar evidencias associadas a modelos em rascunho.
 DECLARE @modelo_rascunho BIGINT;
 INSERT ref.modelo_documento(tipo_documento_codigo,codigo,versao,descricao)
 VALUES(N'RG',N'SMOKE_903_RASCUNHO',1,N'Fixture transacional');
 SET @modelo_rascunho=SCOPE_IDENTITY();
 DECLARE @pessoa_smoke BIGINT=(SELECT MIN(pessoa_observacao_id) FROM silver.pessoa_observacao);
 IF @pessoa_smoke IS NOT NULL
 BEGIN
   DECLARE @rejeitado BIT=0;
   BEGIN TRY
     INSERT silver.documento_evidencia_observacao
       (pessoa_observacao_id,modelo_documento_id,base_origem_codigo,ocorrencia_origem_codigo,classe_evidencia)
     VALUES(@pessoa_smoke,@modelo_rascunho,N'SMOKE_903',N'RASCUNHO',N'DOCUMENTO');
   END TRY
   BEGIN CATCH
     IF ERROR_NUMBER()=52324 SET @rejeitado=1;
     ELSE THROW;
   END CATCH;
   IF @rejeitado=0 THROW 52326,'Modelo em rascunho foi admitido como evidencia.',1;
 END
 ELSE PRINT N'AVISO: fixture sem pessoa Silver; rejeicao DML nao exercitada.';

 PRINT N'PASSOU: estruturas, RG/CIN, FKs, imutabilidade e data na instancia.';
 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
GO

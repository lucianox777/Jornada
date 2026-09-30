SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Vincula cada modelo ao bundle exato Base + Blocking + FS que o gerou.
  O fingerprint entra no snapshot governado; mudar a configuração invalida
  qualquer conferência calculada para outro bundle.
*/
IF COL_LENGTH('identidade.modelo_linkage','model_config_bundle_version') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD model_config_bundle_version NVARCHAR(120) NULL;
GO
IF COL_LENGTH('identidade.modelo_linkage','model_config_bundle_fingerprint_sha256') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD model_config_bundle_fingerprint_sha256 CHAR(64) NULL;
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage')
      AND name='ck_modelo_linkage_model_config_bundle')
    ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT ck_modelo_linkage_model_config_bundle
    CHECK (
      (model_config_bundle_version IS NULL AND model_config_bundle_fingerprint_sha256 IS NULL)
      OR
      (LEN(LTRIM(RTRIM(model_config_bundle_version)))>0
       AND LEN(model_config_bundle_fingerprint_sha256)=64
       AND model_config_bundle_fingerprint_sha256 NOT LIKE '%[^0-9A-F]%')
    );
GO

CREATE OR ALTER PROCEDURE auditoria.sp_calcular_fingerprint_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @fingerprint BINARY(32) OUTPUT
AS
BEGIN
 SET NOCOUNT ON;

 DECLARE @modelo NVARCHAR(MAX),@parametros NVARCHAR(MAX),@estatisticas NVARCHAR(MAX),
         @ruleset NVARCHAR(MAX),@passes NVARCHAR(MAX),@campos NVARCHAR(MAX),
         @frequencias NVARCHAR(MAX);

 SELECT @modelo=(
   SELECT
     CONVERT(VARCHAR(36),m.modelo_id) AS modelo_id,
     m.versao,m.algoritmo_versao,m.normalizacao_versao,m.deduplicacao_metodo,
     m.base_referencia,m.snapshot_referencia,m.registros_lidos,m.pessoas_unicas,
     CONVERT(VARCHAR(33),m.gerado_em,127) AS gerado_em,
     CONVERT(VARCHAR(33),m.snapshot_capturado_em,126) AS snapshot_capturado_em,
     m.amostra_metodo,m.amostra_pool_tamanho,m.amostra_m_tamanho,m.amostra_u_tamanho,
     CONVERT(VARCHAR(36),m.frequencia_nome_versao_id) AS frequencia_nome_versao_id,\n     m.model_config_bundle_version,m.model_config_bundle_fingerprint_sha256
   FROM identidade.modelo_linkage m
   WHERE m.modelo_id=@modelo_id
   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);

 IF @modelo IS NULL
    THROW 51988,'Modelo não encontrado para cálculo do fingerprint da conferência.',1;

 SELECT @parametros=(
   SELECT p.nome,CONVERT(VARCHAR(80),p.valor) AS valor
   FROM identidade.parametro_linkage p
   WHERE p.modelo_id=@modelo_id
   ORDER BY p.nome
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SELECT @estatisticas=(
   SELECT e.nome,CONVERT(VARCHAR(80),e.valor) AS valor,e.metodo
   FROM identidade.estatistica_linkage e
   WHERE e.modelo_id=@modelo_id
   ORDER BY e.nome
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SELECT @ruleset=(
   SELECT CONVERT(VARCHAR(36),r.ruleset_id) AS ruleset_id,
          r.ruleset_versao,r.algoritmo_versao,r.fingerprint_sha256,
          r.ibge_source_versao,r.ibge_fingerprint_sha256
   FROM identidade.linkage_ruleset r
   WHERE r.modelo_id=@modelo_id
   ORDER BY r.ruleset_id
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SELECT @passes=(
   SELECT CONVERT(VARCHAR(36),p.ruleset_id) AS ruleset_id,p.passe_ordem,p.passe_id
   FROM identidade.linkage_ruleset_passe p
   JOIN identidade.linkage_ruleset r ON r.ruleset_id=p.ruleset_id
   WHERE r.modelo_id=@modelo_id
   ORDER BY p.ruleset_id,p.passe_ordem
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SELECT @campos=(
   SELECT CONVERT(VARCHAR(36),c.ruleset_id) AS ruleset_id,
          c.passe_ordem,c.campo_ordem,c.atributo
   FROM identidade.linkage_ruleset_passe_campo c
   JOIN identidade.linkage_ruleset r ON r.ruleset_id=c.ruleset_id
   WHERE r.modelo_id=@modelo_id
   ORDER BY c.ruleset_id,c.passe_ordem,c.campo_ordem
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SELECT @frequencias=(
   SELECT f.atributo,f.valor_normalizado,f.ocorrencias,f.populacao_referencia,
          CONVERT(VARCHAR(80),f.frequencia) AS frequencia
   FROM identidade.frequencia_linkage f
   WHERE f.modelo_id=@modelo_id
   ORDER BY f.atributo,f.valor_normalizado
   FOR JSON PATH,INCLUDE_NULL_VALUES);

 SET @fingerprint=HASHBYTES(
   'SHA2_256',
   CONVERT(VARBINARY(MAX),CONCAT(
     N'MODEL=',COALESCE(@modelo,N'null'),
     N'|PARAMETERS=',COALESCE(@parametros,N'[]'),
     N'|STATISTICS=',COALESCE(@estatisticas,N'[]'),
     N'|RULESET=',COALESCE(@ruleset,N'[]'),
     N'|PASSES=',COALESCE(@passes,N'[]'),
     N'|FIELDS=',COALESCE(@campos,N'[]'),
     N'|TERM_FREQUENCY=',COALESCE(@frequencias,N'[]'))));
END;
GO

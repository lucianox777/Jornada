-- Fase A: censo privado de versões físicas; somente leitura do banco DEV.
-- Versão set-based de v9-e2e-1k-identity-census.sql. Os pares físicos 1k
-- conferem a igualdade exata do JSON com a consulta original.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @attempt IS NULL OR @run IS NULL
   OR @attempt='00000000-0000-0000-0000-000000000000'
   OR @run='00000000-0000-0000-0000-000000000000'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 <>
      N'JornadaV9Dev_'+REPLACE(LOWER(CONVERT(nvarchar(36),@attempt)),N'-',N'')
   OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
       AND name=N'Jornada.EnvironmentProfile'
       AND CONVERT(nvarchar(200),value) COLLATE Latin1_General_100_BIN2=N'Development')
   OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
       AND name=N'Jornada.V9.DevAttempt'
       AND CONVERT(nvarchar(200),value) COLLATE Latin1_General_100_BIN2=LOWER(CONVERT(nvarchar(36),@attempt)))
    THROW 52980,'V9_E2E_CENSUS_DISPOSABLE_SQL_REQUIRED',1;

-- Prepare empty, session-local tables before starting SNAPSHOT. TOP (0)
-- preserves the source column types without reading any census rows.
SELECT TOP (0) pessoa_observacao_id,pessoa_uuid_publicado AS pessoa_uuid,status_publicacao AS status
INTO #census_prob_current FROM identidade.linkage_resultado;
CREATE UNIQUE CLUSTERED INDEX IX_census_prob_current ON #census_prob_current(pessoa_observacao_id);
SELECT TOP (0) pessoa_observacao_id,pessoa_uuid,status,metodo_resolucao
INTO #census_current FROM identidade.v_vinculo_corrente;
CREATE INDEX IX_census_current ON #census_current(pessoa_observacao_id);

SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
BEGIN TRANSACTION;
IF (SELECT COUNT_BIG(*) FROM identidade.linkage_run)<>1
   OR NOT EXISTS(SELECT 1 FROM identidade.linkage_run lr
       JOIN identidade.modelo_linkage ml ON ml.modelo_id=lr.modelo_id
       WHERE lr.linkage_run_id=@run AND lr.modelo_versao=9
       AND lr.tipo_run=N'FULL' AND lr.status=N'PUBLICADO'
       AND lr.registros_elegiveis=lr.avaliados AND lr.avaliados>0
       AND ml.snapshot_referencia=N'6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee')
    THROW 52981,'V9_E2E_CENSUS_SINGLE_PINNED_FULL_REQUIRED',1;

-- The whole SQL universe is intentional: never filter away unexpected
-- non-CPF rows or synthetic versions omitted from the external truth.
IF EXISTS(SELECT 1 FROM silver.pessoa_observacao po
          WHERE po.pessoa_origem_id IS NULL OR po.codigo_pessoa_origem IS NULL
          OR po.codigo_pessoa_origem NOT LIKE N'SYNTH-%')
    THROW 52982,'V9_E2E_CENSUS_UNEXPECTED_SOURCE',1;
IF (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE cpf IS NULL)
    <> (SELECT registros_elegiveis FROM identidade.linkage_run WHERE linkage_run_id=@run)
    THROW 52983,'V9_E2E_CENSUS_ELIGIBLE_MISMATCH',1;

-- Preserve the view's ranking and precedence. A statement boundary keeps
-- the anti-join from running the ranking again for each active base link.
;WITH ranked AS (
    SELECT r.pessoa_observacao_id,r.pessoa_uuid_publicado AS pessoa_uuid,
           r.status_publicacao AS status,
           ROW_NUMBER() OVER(
               PARTITION BY r.pessoa_observacao_id
               ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) AS rn
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
    WHERE lr.status=N'PUBLICADO'
      AND r.resultado_publicacao IS NOT NULL
      AND r.status_publicacao IS NOT NULL
      AND r.publicado_em IS NOT NULL
)
INSERT #census_prob_current(pessoa_observacao_id,pessoa_uuid,status)
SELECT pessoa_observacao_id,pessoa_uuid,status FROM ranked WHERE rn=1;

INSERT #census_current(pessoa_observacao_id,pessoa_uuid,status,metodo_resolucao)
SELECT b.pessoa_observacao_id,b.pessoa_uuid,b.status,b.metodo_resolucao
FROM identidade.vinculo_fonte b
WHERE b.ativo=1
  AND (b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
       OR NOT EXISTS (SELECT 1 FROM #census_prob_current p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id))
UNION ALL
SELECT p.pessoa_observacao_id,p.pessoa_uuid,p.status,N'LINKAGE_PROBABILISTICO'
FROM #census_prob_current p
WHERE NOT EXISTS (
    SELECT 1 FROM identidade.vinculo_fonte b
    WHERE b.ativo=1 AND b.pessoa_observacao_id=p.pessoa_observacao_id
      AND b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'));

SELECT po.pessoa_observacao_id AS observationId,po.versao_interna AS version,
       g.codigo AS manager,b.nome_arquivo AS package,
       po.codigo_pessoa_origem AS opaque,po.pessoa_origem_id AS originId,
       CONVERT(bit,CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END) AS hasCpf,
       LOWER(CONVERT(nvarchar(36),vc.pessoa_uuid)) AS currentUuid,
       vc.status AS currentStatus,vc.metodo_resolucao AS currentMethod,
       gp.estado_identidade AS goldState,
       LOWER(CONVERT(nvarchar(36),lr.linkage_run_id)) AS resultRunId,
       lr.resultado_publicacao AS runResult,
       LOWER(CONVERT(nvarchar(36),lr.pessoa_uuid_publicado)) AS publishedUuid,
       lr.motivo_publicacao AS publicationReason
FROM silver.pessoa_observacao po
LEFT JOIN ref.gestor g ON g.gestor_id=po.gestor_id
LEFT JOIN ingestao.lote l ON l.lote_id=po.lote_id
LEFT JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
LEFT JOIN #census_current vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=vc.pessoa_uuid
LEFT JOIN identidade.linkage_resultado lr ON lr.pessoa_observacao_id=po.pessoa_observacao_id
    AND lr.linkage_run_id=@run
ORDER BY po.pessoa_observacao_id
FOR JSON PATH, INCLUDE_NULL_VALUES;
COMMIT TRANSACTION;

-- Fase 3, plano PO v6: censo privado de versões físicas; somente leitura.
-- Caller: conexão autenticada do banco DEV efêmero, depois do FULL único.
-- Execute with typed SqlCommand parameters @attempt and @run (uniqueidentifier).
-- No interpolation of shell or SQLCMD variables.
-- Do not send this per-version JSON to logs or upload it as an artifact.
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

SELECT g.codigo AS manager,b.nome_arquivo AS package,
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
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=vc.pessoa_uuid
LEFT JOIN identidade.linkage_resultado lr ON lr.pessoa_observacao_id=po.pessoa_observacao_id
    AND lr.linkage_run_id=@run
ORDER BY po.pessoa_observacao_id
FOR JSON PATH, INCLUDE_NULL_VALUES;
COMMIT TRANSACTION;

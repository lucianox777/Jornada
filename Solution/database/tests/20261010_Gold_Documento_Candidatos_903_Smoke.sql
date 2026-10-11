SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
-- #903 fase 3: smoke sem fixture e sem mutacao de dados.
IF DB_NAME() NOT IN(N'JornadaE2E',N'JornadaSyntheticDev')
 THROW 52329,'Somente SQL descartavel JornadaE2E/JornadaSyntheticDev.',1;
IF OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold',N'V') IS NULL
 THROW 52330,'View de candidatos documentais Gold ausente.',1;
-- Evidencias documentais nao elegem vencedor por classe ou data.
IF EXISTS (SELECT 1 FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
 AND name IN(N'ordem_candidata',N'situacao_candidata',N'quantidade_mesma_classe_data',N'data_atendimento',N'capturado_em'))
 THROW 52331,'VIEW nao deve eleger documento vencedor nem usar data de atendimento.',1;
IF (SELECT COUNT(*) FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
 AND name IN(N'pessoa_observacao_id',N'tipo_documento_codigo',N'documento_evidencia_id',
 N'modelo_documento_id',N'emissor_codigo',N'uf_emissor',N'classe_evidencia',
 N'data_evidencia',N'base_origem_codigo',N'ocorrencia_origem_codigo',
 N'atributo_codigo',N'valor_original'))<>12
 THROW 52332,'VIEW perdeu atributos, qualificadores ou proveniencia.',1;
IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
 AND definition LIKE N'%status_validacao=N''VALIDO''%'
 AND definition LIKE N'%estado=N''PUBLICADO''%'
 AND definition LIKE N'%comprovacao_admissivel=1%')
 THROW 52333,'VIEW nao filtra somente evidencias validas, publicadas e admissiveis.',1;

-- Regressao estrutural: nao escolher um documento unico por ranking temporal.
-- A view deve preservar todas as ocorrencias admissiveis para consolidacao 1:N.
IF EXISTS (SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND (UPPER(definition) LIKE N'%ROW_NUMBER(%'
     OR UPPER(definition) LIKE N'%ROW_NUMBER (%'
     OR UPPER(definition) LIKE N'%TOP (1)%'
     OR UPPER(definition) LIKE N'%TOP 1 %'
     OR UPPER(definition) LIKE N'%ORDER BY%'))
 THROW 52334,'VIEW nao pode eleger documento por ranking, TOP ou ordenacao.',1;

-- Regressao estrutural: a selecao nunca deve atualizar Gold automaticamente.
IF EXISTS (
 SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND (definition LIKE N'%UPDATE%gold.%' OR definition LIKE N'%MERGE%gold.%')
)
 THROW 52336,'A view de candidatos nao pode escrever em Gold.',1;
IF OBJECT_ID(N'gold.vw_documento_evidencia_candidato_uuid',N'V') IS NULL
 THROW 52337,'View documental por UUID nao instalada.',1;
IF COL_LENGTH(N'gold.vw_documento_evidencia_candidato_uuid',N'pessoa_uuid') IS NULL
 THROW 52338,'View documental nao expoe UUID.',1;
-- A projecao UUID deve preservar os qualificadores e a proveniencia.
IF (SELECT COUNT(*) FROM sys.columns
 WHERE object_id=OBJECT_ID(N'gold.vw_documento_evidencia_candidato_uuid')
   AND name IN(N'pessoa_uuid',N'pessoa_observacao_id',N'tipo_documento_codigo',
       N'documento_evidencia_id',N'modelo_documento_id',N'emissor_codigo',
       N'uf_emissor',N'classe_evidencia',N'data_evidencia',
       N'base_origem_codigo',N'ocorrencia_origem_codigo',
       N'atributo_codigo',N'valor_original'))<>13
 THROW 52339,'Projecao UUID perdeu atributos ou proveniencia documental.',1;
IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'gold.vw_documento_evidencia_candidato_uuid')
   AND definition LIKE N'%mapeamentos_sem_ambiguidade%'
   AND definition LIKE N'%outro.pessoa_uuid<>m.pessoa_uuid%')
 THROW 52340,'Projecao UUID deve abster-se diante de vinculos ambiguos.',1;

-- Colecao RG distinta e relacao de proveniencia sao partes obrigatorias
-- do incremento; uma CI verde nao pode ignorar views ausentes ou perder a
-- identidade (UUID, numero, emissor, UF) ao consolidar ocorrencias.
IF OBJECT_ID(N'gold.vw_rg_documento_distinto',N'V') IS NULL
 THROW 52341,'View da colecao RG 1:N ausente.',1;
IF OBJECT_ID(N'gold.vw_rg_documento_proveniencia',N'V') IS NULL
 THROW 52342,'View de proveniencia dos RGs ausente.',1;
IF (SELECT COUNT(*) FROM sys.columns
 WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_distinto')
   AND name IN(N'pessoa_uuid',N'tipo_documento_codigo',N'numero_rg',
               N'emissor_codigo',N'uf_emissor',N'quantidade_ocorrencias'))<>6
 THROW 52343,'Colecao RG perdeu chave documental ou contagem.',1;
IF (SELECT COUNT(*) FROM sys.columns
 WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_proveniencia')
   AND name IN(N'pessoa_uuid',N'numero_rg',N'emissor_codigo',N'uf_emissor',
               N'documento_evidencia_id',N'pessoa_observacao_id',
               N'modelo_documento_id',N'data_evidencia',
               N'base_origem_codigo',N'ocorrencia_origem_codigo'))<>10
 THROW 52344,'Proveniencia RG perdeu identificadores ou origem.',1;
IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_distinto')
   AND definition LIKE N'%COUNT_BIG(DISTINCT documento_evidencia_id)%'
   AND definition LIKE N'%GROUP BY pessoa_uuid, numero_rg, emissor_codigo, uf_emissor%')
 THROW 52345,'RG distinto deve deduplicar ocorrencias por chave completa.',1;
IF NOT EXISTS (SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'gold.vw_rg_documento_proveniencia')
   AND definition LIKE N'%c.documento_evidencia_id%'
   AND definition LIKE N'%c.ocorrencia_origem_codigo%')
 THROW 52346,'Relacao RG deve preservar todas as ocorrencias e suas origens.',1;

-- Verifica por comportamento, sem tocar tabelas permanentes, a chave documental
-- (UUID, numero, emissor, UF), repeticoes e qualificadores distintos.
DECLARE @rg TABLE (
 pessoa_uuid UNIQUEIDENTIFIER NOT NULL,
 numero_rg NVARCHAR(50) NOT NULL,
 emissor_codigo NVARCHAR(50) NOT NULL,
 uf_emissor NVARCHAR(8) NOT NULL,
 documento_evidencia_id BIGINT NOT NULL
);
DECLARE @uuid UNIQUEIDENTIFIER='11111111-1111-1111-1111-111111111111';
INSERT @rg VALUES
 (@uuid,N' 00123x ',N' ssp ',N' sp ',1),
 (@uuid,N'00123X',N'SSP',N'SP',2),
 (@uuid,N'00123X',N'OUTRO',N'SP',3),
 (@uuid,N'00123X',N'SSP',N'RJ',4),
 (@uuid,N'99999',N'SSP',N'SP',5);
;WITH normalized AS (
 SELECT pessoa_uuid, UPPER(LTRIM(RTRIM(numero_rg))) numero_rg,
        UPPER(LTRIM(RTRIM(emissor_codigo))) emissor_codigo,
        UPPER(LTRIM(RTRIM(uf_emissor))) uf_emissor,
        documento_evidencia_id
 FROM @rg
), distinct_rg AS (
 SELECT pessoa_uuid,numero_rg,emissor_codigo,uf_emissor,
        COUNT_BIG(DISTINCT documento_evidencia_id) quantidade_ocorrencias
 FROM normalized
 GROUP BY pessoa_uuid,numero_rg,emissor_codigo,uf_emissor
)
SELECT pessoa_uuid,numero_rg,emissor_codigo,uf_emissor,quantidade_ocorrencias
INTO #rg_result
FROM distinct_rg;
IF (SELECT COUNT(*) FROM #rg_result)<>4
 OR (SELECT quantidade_ocorrencias FROM #rg_result
     WHERE numero_rg=N'00123X' AND emissor_codigo=N'SSP' AND uf_emissor=N'SP')<>2
 THROW 52347,'Regressao de chave RG: repeticoes devem unir e qualificadores distintos permanecer separados.',1;
DROP TABLE #rg_result;
-- Isolamento 1:N entre pessoas: mesmo numero e qualificadores em UUIDs
-- diferentes nao podem ser fundidos em um documento global.
DECLARE @uuid_outro UNIQUEIDENTIFIER='22222222-2222-2222-2222-222222222222';
INSERT @rg VALUES (@uuid_outro,N'00123X',N'SSP',N'SP',6);
;WITH normalized AS (
 SELECT pessoa_uuid, UPPER(LTRIM(RTRIM(numero_rg))) AS numero_rg,
        UPPER(LTRIM(RTRIM(emissor_codigo))) AS emissor_codigo,
        UPPER(LTRIM(RTRIM(uf_emissor))) AS uf_emissor,
        documento_evidencia_id
 FROM @rg
), distinct_rg AS (
 SELECT pessoa_uuid, numero_rg, emissor_codigo, uf_emissor,
        COUNT_BIG(DISTINCT documento_evidencia_id) AS quantidade_ocorrencias
 FROM normalized
 GROUP BY pessoa_uuid, numero_rg, emissor_codigo, uf_emissor
)
SELECT pessoa_uuid, numero_rg, emissor_codigo, uf_emissor, quantidade_ocorrencias
INTO #rg_multi_uuid
FROM distinct_rg;
IF (SELECT COUNT(*) FROM #rg_multi_uuid)<>5
 OR (SELECT COUNT(*) FROM #rg_multi_uuid WHERE numero_rg=N'00123X'
     AND emissor_codigo=N'SSP' AND uf_emissor=N'SP')<>2
 OR (SELECT quantidade_ocorrencias FROM #rg_multi_uuid
     WHERE pessoa_uuid=@uuid_outro AND numero_rg=N'00123X'
       AND emissor_codigo=N'SSP' AND uf_emissor=N'SP')<>1
 THROW 52348,'Regressao RG: mesma chave documental em UUIDs distintos nao pode ser fundida.',1;
DROP TABLE #rg_multi_uuid;


-- Repeticao exata da mesma evidencia nao aumenta o numero de ocorrencias
-- documentais; evidencia distinta do mesmo RG deve continuar contabilizada.
INSERT @rg VALUES (@uuid,N'00123X',N'SSP',N'SP',1);
;WITH normalized AS (
 SELECT pessoa_uuid, UPPER(LTRIM(RTRIM(numero_rg))) AS numero_rg,
        UPPER(LTRIM(RTRIM(emissor_codigo))) AS emissor_codigo,
        UPPER(LTRIM(RTRIM(uf_emissor))) AS uf_emissor,
        documento_evidencia_id
 FROM @rg
)
SELECT pessoa_uuid,numero_rg,emissor_codigo,uf_emissor,
       COUNT_BIG(DISTINCT documento_evidencia_id) AS quantidade_ocorrencias
INTO #rg_repeat
FROM normalized
GROUP BY pessoa_uuid,numero_rg,emissor_codigo,uf_emissor;
IF (SELECT quantidade_ocorrencias FROM #rg_repeat
    WHERE pessoa_uuid=@uuid AND numero_rg=N'00123X'
      AND emissor_codigo=N'SSP' AND uf_emissor=N'SP')<>2
 THROW 52349,'Repeticao da mesma evidencia RG inflou a contagem de ocorrencias.',1;
DROP TABLE #rg_repeat;

PRINT N'GOLD DOCUMENTARY CANDIDATE VIEW SCHEMA: OK';
GO

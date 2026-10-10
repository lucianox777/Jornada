SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
-- #903 fase 3: smoke sem fixture e sem mutacao de dados.
IF OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold',N'V') IS NULL
 THROW 52330,'View de candidatos documentais Gold ausente.',1;
-- Evidencias documentais nao elegem vencedor por classe ou data.
IF DB_NAME() NOT IN(N'JornadaE2E',N'JornadaSyntheticDev')
 THROW 52329,'Somente SQL descartavel JornadaE2E/JornadaSyntheticDev.',1;
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

-- Regressao estrutural: a selecao nunca deve atualizar Gold automaticamente.
IF EXISTS (
 SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND (definition LIKE N'%UPDATE%gold.%' OR definition LIKE N'%MERGE%gold.%')
)
 THROW 52336,'A view de candidatos nao pode escrever em Gold.',1;
PRINT N'GOLD DOCUMENTARY CANDIDATE VIEW SCHEMA: OK';
GO

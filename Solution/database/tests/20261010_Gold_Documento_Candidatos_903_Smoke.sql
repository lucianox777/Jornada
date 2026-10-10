SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
-- #903 fase 3: smoke sem fixture e sem mutacao de dados.
IF OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold',N'V') IS NULL
 THROW 52330,'View de candidatos documentais Gold ausente.',1;
IF NOT EXISTS(
 SELECT 1 FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND name=N'situacao_candidata'
)
 THROW 52331,'Classificacao de abstencao documental ausente.',1;
IF NOT EXISTS(
 SELECT 1 FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND name=N'data_evidencia'
)
 THROW 52332,'Data propria da evidencia nao exposta.',1;
IF EXISTS(
 SELECT 1 FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND name IN(N'data_atendimento',N'capturado_em')
)
 THROW 52333,'Metadados de atendimento/captura nao devem ordenar Gold.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.columns
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND name=N'quantidade_mesma_classe_data'
)
 THROW 52334,'Contagem de empates documentais ausente.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.sql_modules
 WHERE object_id=OBJECT_ID(N'silver.vw_documento_evidencia_candidato_gold')
   AND definition LIKE N'%classe_possui_data_ausente%'
   AND definition LIKE N'%ABSTER_SEM_DATA%'
   AND definition LIKE N'%ABSTER_EMPATE%'
)
 THROW 52335,'Abstencao por data ausente ou empate nao declarada na view.',1;
PRINT N'GOLD DOCUMENTARY CANDIDATE VIEW SCHEMA: OK';
GO

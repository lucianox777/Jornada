SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  RF-052 / decisão 29/09/2026: o IBGE é bootstrap inicial, carregado uma vez.
  GENERATE_DRAFT posterior fixa explicitamente a fonte do bootstrap persistido;
  não reativa nem exige status ATIVA. Nenhum modelo existente é alterado.
*/
CREATE OR ALTER VIEW ref.v_ibge_u_referencia_pronta AS
SELECT u.ibge_u_referencia_id,u.frequencia_nome_versao_id,
       v.codigo referencia_ibge,u.conteudo_origem_sha256,
       u.metodo_versao,u.construcao_versao,u.canal_versao,
       u.comparador_versao,u.recorte_prenome,u.seed,u.pares,
       u.vocabulario_prenomes,u.vocabulario_sobrenomes,
       u.ocorrencias_prenomes,u.ocorrencias_sobrenomes,
       u.colisoes_prenome,u.colisoes_sobrenome,u.colisoes_nome_completo,
       u.resultado_sha256,u.publicado_em
FROM ref.ibge_u_referencia u
JOIN ref.frequencia_nome_versao v
  ON v.frequencia_nome_versao_id=u.frequencia_nome_versao_id
WHERE u.status=N'PRONTA'
  AND u.conteudo_origem_sha256=v.conteudo_sha256;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_fixa_frequencia_nome_versao
ON identidade.modelo_linkage
AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF TRIGGER_NESTLEVEL()>1 RETURN;

 IF EXISTS(
   SELECT 1
   FROM inserted i
   LEFT JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE d.modelo_id IS NULL AND i.status=N'GERANDO'
     AND i.algoritmo_versao=N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8'
     AND i.frequencia_nome_versao_id IS NULL)
   THROW 51639,'Geração de modelo exige referência explícita do bootstrap IBGE inicial persistido.',1;

 IF EXISTS(
   SELECT 1
   FROM inserted i
   LEFT JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE d.modelo_id IS NULL AND i.status=N'GERANDO'
     AND i.algoritmo_versao=N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8'
     AND NOT EXISTS(
       SELECT 1
       FROM ref.frequencia_nome_versao v
       WHERE v.frequencia_nome_versao_id=i.frequencia_nome_versao_id
         AND v.codigo=N'CENSO2022_NOMES_BRASIL_V1'
         AND DATALENGTH(v.conteudo_sha256)=32
         AND EXISTS(
           SELECT 1 FROM ref.ibge_u_referencia u
           WHERE u.frequencia_nome_versao_id=v.frequencia_nome_versao_id
             AND u.conteudo_origem_sha256=v.conteudo_sha256
             AND u.status=N'PRONTA'
             AND u.metodo_versao=N'IBGE_NOMINAL_U_BOOTSTRAP_V1'
             AND u.construcao_versao=N'INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1'
             AND u.canal_versao=N'CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1'
             AND u.comparador_versao=N'WholeNameJaroWinklerV1'
             AND u.recorte_prenome=N'TODOS')
         AND EXISTS(
           SELECT 1 FROM ref.ibge_u_referencia u
           WHERE u.frequencia_nome_versao_id=v.frequencia_nome_versao_id
             AND u.conteudo_origem_sha256=v.conteudo_sha256
             AND u.status=N'PRONTA'
             AND u.metodo_versao=N'IBGE_NOMINAL_U_BOOTSTRAP_V1'
             AND u.construcao_versao=N'INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1'
             AND u.canal_versao=N'CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1'
             AND u.comparador_versao=N'WholeNameJaroWinklerV1'
             AND u.recorte_prenome=N'FEMININO')
     ))
   THROW 51641,'Referência do modelo não comprova bootstrap IBGE inicial íntegro e persistido.',1;

 IF EXISTS(
   SELECT 1 FROM inserted i JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE ISNULL(i.frequencia_nome_versao_id,-1)<>ISNULL(d.frequencia_nome_versao_id,-1))
   THROW 51640,'A versão da referência de frequências de um modelo é imutável após sua criação.',1;
END;
GO

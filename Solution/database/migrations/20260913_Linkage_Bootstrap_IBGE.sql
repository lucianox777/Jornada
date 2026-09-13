SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Cold start do linkage
 ---------------------
 - m bootstrap vem de PRIOR_INSTITUCIONAL, nunca do IBGE;
 - u de concordância exata de nome pode usar colisão da referência IBGE versionada;
 - ausência/supressão no ranking não é u=0;
 - modelo bootstrap pode produzir score diagnóstico/provisório, mas não pode ser ATIVO;
 - quando houver pares CPF independentes suficientes, nova geração passa a ESTIMADO;
 - HOMOLOGADO continua sendo decisão explícita posterior à validação.
*/

IF COL_LENGTH('identidade.modelo_linkage','estagio_parametro') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD estagio_parametro NVARCHAR(30) NOT NULL
        CONSTRAINT DF_modelo_linkage_estagio_parametro DEFAULT('ESTIMADO') WITH VALUES;
GO
IF COL_LENGTH('identidade.modelo_linkage','m_origem') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD m_origem NVARCHAR(40) NOT NULL
        CONSTRAINT DF_modelo_linkage_m_origem DEFAULT('PARES_CPF_INDEPENDENTES') WITH VALUES;
GO
IF COL_LENGTH('identidade.modelo_linkage','u_nome_origem') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD u_nome_origem NVARCHAR(60) NOT NULL
        CONSTRAINT DF_modelo_linkage_u_nome_origem DEFAULT('AMOSTRA_U_EMPIRICA') WITH VALUES;
GO
IF COL_LENGTH('identidade.modelo_linkage','promocao_automatica_permitida') IS NULL
    ALTER TABLE identidade.modelo_linkage ADD promocao_automatica_permitida BIT NOT NULL
        CONSTRAINT DF_modelo_linkage_promocao_automatica DEFAULT(1) WITH VALUES;
GO

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='ck_modelo_linkage_estagio_parametro')
    ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT ck_modelo_linkage_estagio_parametro
        CHECK(estagio_parametro IN('PRIOR_BOOTSTRAP','ESTIMADO','HOMOLOGADO'));
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='ck_modelo_linkage_origem_m')
    ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT ck_modelo_linkage_origem_m
        CHECK(m_origem IN('PRIOR_INSTITUCIONAL','PARES_CPF_INDEPENDENTES','OUTRA_EVIDENCIA_HOMOLOGADA'));
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='ck_modelo_linkage_bootstrap_promocao')
    ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT ck_modelo_linkage_bootstrap_promocao
        CHECK(estagio_parametro<>'PRIOR_BOOTSTRAP' OR promocao_automatica_permitida=0);
GO

CREATE OR ALTER VIEW ref.v_frequencia_nome_ibge_colisao_ativa AS
WITH base AS (
    SELECT frequencia_nome_versao_id,frequencia
    FROM ref.v_frequencia_nome_ativa
    WHERE tipo='NOME'
      AND escopo_geografico='BRASIL'
      AND uf_codigo='00'
      AND municipio_codigo='0000000'
      AND sexo='TODOS'
      AND periodo_nascimento='TODOS'
), totais AS (
    SELECT frequencia_nome_versao_id,SUM(CONVERT(decimal(38,0),frequencia)) massa_publicada
    FROM base GROUP BY frequencia_nome_versao_id
)
SELECT b.frequencia_nome_versao_id,
       CONVERT(decimal(38,18),SUM(POWER(CONVERT(decimal(38,18),b.frequencia)/NULLIF(t.massa_publicada,0),2))) AS u_nome_exact_colisao,
       CONVERT(bigint,t.massa_publicada) AS massa_publicada
FROM base b JOIN totais t ON t.frequencia_nome_versao_id=b.frequencia_nome_versao_id
GROUP BY b.frequencia_nome_versao_id,t.massa_publicada;
GO

/* A massa publicada é o denominador intencional no bootstrap. Nomes suprimidos/ausentes
   não recebem frequência zero individual. A colisão resultante é tratada como referência
   conservadora da parte publicada e deve ser congelada pelo frequencia_nome_versao_id. */

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_bloqueia_bootstrap_ativo
ON identidade.modelo_linkage
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1 FROM inserted
        WHERE status='ATIVO'
          AND (estagio_parametro='PRIOR_BOOTSTRAP' OR promocao_automatica_permitida=0))
        THROW 51670,'Modelo PRIOR_BOOTSTRAP não pode ser ativado. Use-o apenas para score diagnóstico/provisório até m empírico suficiente.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_homologado_exige_estimado
ON identidade.modelo_linkage
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1 FROM inserted i
        JOIN deleted d ON d.modelo_id=i.modelo_id
        WHERE i.estagio_parametro='HOMOLOGADO'
          AND d.estagio_parametro='PRIOR_BOOTSTRAP')
        THROW 51671,'Modelo não pode saltar diretamente de PRIOR_BOOTSTRAP para HOMOLOGADO; gere parâmetros ESTIMADOS por pares CPF independentes e valide nova versão.',1;
END;
GO

CREATE OR ALTER VIEW identidade.v_modelo_linkage_origem_parametros AS
SELECT modelo_id,versao,status,estagio_parametro,m_origem,u_nome_origem,
       promocao_automatica_permitida,frequencia_nome_versao_id,
       amostra_m_tamanho,amostra_u_tamanho,gerado_em,ativado_em
FROM identidade.modelo_linkage;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Identificadores múltiplos por observação de Pessoa
 -------------------------------------------------
 Uma observação pode possuir zero, um ou vários identificadores. CPF, CNS, RG,
 código interno de uma base e UUID Jornada deixam de ser tratados como variações
 da mesma coluna e passam a ser evidências identificadoras tipadas.

 A prioridade de resolução é explícita e governável. CPF ocupa o nível mais alto.
 A prioridade orienta a âncora principal e a ordem de avaliação, mas NÃO permite
 descartar conflito entre identificadores determinísticos válidos.

 Esta migração é aditiva e NÃO ativa resolução determinística por CNS/RG. A
 elegibilidade determinística é governada por tipo/namespace e pelas políticas
 de identidade.
*/

IF OBJECT_ID('ref.tipo_identificador_pessoa','U') IS NULL
BEGIN
    CREATE TABLE ref.tipo_identificador_pessoa(
        tipo_identificador_codigo NVARCHAR(40) NOT NULL PRIMARY KEY,
        nome NVARCHAR(120) NOT NULL,
        exige_namespace BIT NOT NULL,
        formato_codigo NVARCHAR(80) NOT NULL,
        elegibilidade_deterministica NVARCHAR(30) NOT NULL,
        prioridade_resolucao SMALLINT NOT NULL,
        ativo BIT NOT NULL CONSTRAINT DF_tipo_identificador_pessoa_ativo DEFAULT(1),
        CONSTRAINT ck_tipo_identificador_pessoa_elegibilidade
            CHECK(elegibilidade_deterministica IN('NAO_AUTOMATICA','CONDICIONAL','DETERMINISTICA')),
        CONSTRAINT ck_tipo_identificador_pessoa_prioridade
            CHECK(prioridade_resolucao BETWEEN 1 AND 100)
    );
END;
GO

IF COL_LENGTH('ref.tipo_identificador_pessoa','prioridade_resolucao') IS NULL
    ALTER TABLE ref.tipo_identificador_pessoa
        ADD prioridade_resolucao SMALLINT NOT NULL
            CONSTRAINT DF_tipo_identificador_pessoa_prioridade DEFAULT(1) WITH VALUES;
GO

MERGE ref.tipo_identificador_pessoa AS t
USING (VALUES
    ('CPF','CPF','BR','CPF_BR_11_V1','DETERMINISTICA',100),
    ('UUID_JORNADA','UUID publicado pela Jornada','JORNADA','UUID_V1','DETERMINISTICA',90),
    ('CODIGO_BASE_ORIGEM','Código interno de cadastro de origem','BASE_PESSOA_ORIGEM','TEXTO_255_V1','CONDICIONAL',80),
    ('CNS','Cartão Nacional de Saúde','BR','CNS_BR_V1','CONDICIONAL',70),
    ('RG','Registro Geral / identidade estadual','SSP_UF','RG_QUALIFICADO_V1','CONDICIONAL',60),
    ('OUTRO','Outro identificador institucional','OBRIGATORIO','TEXTO_255_V1','NAO_AUTOMATICA',10)
) AS s(tipo_identificador_codigo,nome,namespace_padrao,formato_codigo,elegibilidade_deterministica,prioridade_resolucao)
ON t.tipo_identificador_codigo=s.tipo_identificador_codigo
WHEN NOT MATCHED THEN
    INSERT(tipo_identificador_codigo,nome,exige_namespace,formato_codigo,elegibilidade_deterministica,prioridade_resolucao)
    VALUES(s.tipo_identificador_codigo,s.nome,1,s.formato_codigo,s.elegibilidade_deterministica,s.prioridade_resolucao)
WHEN MATCHED THEN UPDATE SET
    nome=s.nome,
    formato_codigo=s.formato_codigo,
    elegibilidade_deterministica=s.elegibilidade_deterministica,
    prioridade_resolucao=s.prioridade_resolucao,
    ativo=1;
GO

/* Invariante institucional: CPF é a referência de maior prioridade.
   Nenhum outro tipo ativo pode empatar ou ultrapassá-lo. */
IF EXISTS(
    SELECT 1
    FROM ref.tipo_identificador_pessoa cpf
    JOIN ref.tipo_identificador_pessoa outro
      ON outro.tipo_identificador_codigo<>'CPF' AND outro.ativo=1
    WHERE cpf.tipo_identificador_codigo='CPF'
      AND cpf.ativo=1
      AND outro.prioridade_resolucao>=cpf.prioridade_resolucao)
    THROW 51281,'CPF deve permanecer como identificador de maior prioridade de resolução.',1;
GO

IF OBJECT_ID('silver.pessoa_identificador_observacao','U') IS NULL
BEGIN
    CREATE TABLE silver.pessoa_identificador_observacao(
        pessoa_identificador_observacao_id BIGINT IDENTITY PRIMARY KEY,
        pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
        tipo_identificador_codigo NVARCHAR(40) NOT NULL REFERENCES ref.tipo_identificador_pessoa(tipo_identificador_codigo),
        namespace_codigo NVARCHAR(120) NOT NULL,
        valor_original NVARCHAR(255) NOT NULL,
        valor_normalizado NVARCHAR(255) NOT NULL,
        base_pessoa_origem_id BIGINT NULL REFERENCES ref.base_pessoa_origem(base_pessoa_origem_id),
        emissor_codigo NVARCHAR(120) NULL,
        uf_emissor CHAR(2) NULL,
        status_validacao NVARCHAR(20) NOT NULL CONSTRAINT DF_pessoa_identificador_status DEFAULT('NAO_VALIDADO'),
        status_evidencia NVARCHAR(20) NOT NULL CONSTRAINT DF_pessoa_identificador_evidencia DEFAULT('DECLARADO'),
        evidencia_tipo NVARCHAR(80) NULL,
        verificado_em DATETIMEOFFSET(7) NULL,
        ingested_at DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_pessoa_identificador_ingested DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT uq_pessoa_identificador_observacao UNIQUE(pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,valor_normalizado),
        CONSTRAINT ck_pessoa_identificador_namespace CHECK(LEN(namespace_codigo) BETWEEN 1 AND 120),
        CONSTRAINT ck_pessoa_identificador_valores CHECK(LEN(valor_original) BETWEEN 1 AND 255 AND LEN(valor_normalizado) BETWEEN 1 AND 255),
        CONSTRAINT ck_pessoa_identificador_status CHECK(status_validacao IN('VALIDO','INVALIDO','NAO_VALIDADO','CONFLITANTE')),
        CONSTRAINT ck_pessoa_identificador_evidencia CHECK(status_evidencia IN('DECLARADO','COMPROVADO')),
        CONSTRAINT ck_pessoa_identificador_comprovado CHECK(status_evidencia<>'COMPROVADO' OR verificado_em IS NOT NULL),
        CONSTRAINT ck_pessoa_identificador_rg CHECK(tipo_identificador_codigo<>'RG' OR (emissor_codigo IS NOT NULL AND uf_emissor IS NOT NULL)),
        CONSTRAINT ck_pessoa_identificador_codigo_base CHECK(tipo_identificador_codigo<>'CODIGO_BASE_ORIGEM' OR base_pessoa_origem_id IS NOT NULL),
        CONSTRAINT ck_pessoa_identificador_uuid_jornada CHECK(tipo_identificador_codigo<>'UUID_JORNADA' OR namespace_codigo='JORNADA'),
        CONSTRAINT ck_pessoa_identificador_cpf CHECK(tipo_identificador_codigo<>'CPF' OR namespace_codigo='BR'),
        CONSTRAINT ck_pessoa_identificador_cns CHECK(tipo_identificador_codigo<>'CNS' OR namespace_codigo='BR')
    );
    CREATE INDEX ix_pessoa_identificador_busca
        ON silver.pessoa_identificador_observacao(tipo_identificador_codigo,namespace_codigo,valor_normalizado)
        INCLUDE(pessoa_observacao_id,status_validacao,status_evidencia,base_pessoa_origem_id);
END;
GO

/* Compatibilidade: materializa CPF e codigo_pessoa_origem já existentes como
   identificadores observados sem remover as colunas legadas. */
INSERT silver.pessoa_identificador_observacao(
    pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
    valor_original,valor_normalizado,status_validacao,status_evidencia)
SELECT po.pessoa_observacao_id,'CPF','BR',po.cpf,po.cpf,'VALIDO','DECLARADO'
FROM silver.pessoa_observacao po
WHERE po.cpf IS NOT NULL
  AND NOT EXISTS(
      SELECT 1 FROM silver.pessoa_identificador_observacao i
      WHERE i.pessoa_observacao_id=po.pessoa_observacao_id
        AND i.tipo_identificador_codigo='CPF' AND i.namespace_codigo='BR'
        AND i.valor_normalizado=po.cpf);
GO

INSERT silver.pessoa_identificador_observacao(
    pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
    valor_original,valor_normalizado,base_pessoa_origem_id,status_validacao,status_evidencia)
SELECT po.pessoa_observacao_id,'CODIGO_BASE_ORIGEM',b.codigo,
       po.codigo_pessoa_origem,po.codigo_pessoa_origem,porg.base_pessoa_origem_id,
       CASE WHEN b.confianca_identidade='HOMOLOGADA_DETERMINISTICA' THEN 'VALIDO' ELSE 'NAO_VALIDADO' END,
       'DECLARADO'
FROM silver.pessoa_observacao po
JOIN silver.pessoa_origem porg ON porg.pessoa_origem_id=po.pessoa_origem_id
JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=porg.base_pessoa_origem_id
WHERE po.codigo_pessoa_origem IS NOT NULL
  AND NOT EXISTS(
      SELECT 1 FROM silver.pessoa_identificador_observacao i
      WHERE i.pessoa_observacao_id=po.pessoa_observacao_id
        AND i.tipo_identificador_codigo='CODIGO_BASE_ORIGEM'
        AND i.namespace_codigo=b.codigo
        AND i.valor_normalizado=po.codigo_pessoa_origem);
GO

/* Semântica da hierarquia:
   1. a maior prioridade elegível é a âncora principal da tentativa de resolução;
   2. identificadores de menor prioridade continuam sendo verificados;
   3. se outro identificador determinístico válido apontar para UUID incompatível,
      a observação entra em CONFLITO_IDENTIDADE; prioridade nunca apaga divergência;
   4. identificadores condicionais só participam deterministicamente após homologação
      das respectivas regras de validação/namespace. */

/* Não existe constraint exigindo ao menos um identificador. Isso é intencional:
   observações sem ID são válidas e seguem para resolução por atributos/linkage. */

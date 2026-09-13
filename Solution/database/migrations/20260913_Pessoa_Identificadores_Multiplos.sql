SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Identificadores múltiplos por observação de Pessoa
 -------------------------------------------------
 Uma observação pode possuir zero, um ou vários identificadores. CPF, CNS, RG,
 código interno de uma base e UUID Jornada deixam de ser tratados como variações
 da mesma coluna e passam a ser evidências identificadoras tipadas.

 A hierarquia aplica-se somente a identificadores EXTERNOS. CPF ocupa o nível mais
 alto dessa hierarquia e preserva o invariante já adotado pela Jornada:
 o mesmo CPF válido/governado sempre resolve para o mesmo UUID âncora.
 UUID_JORNADA não concorre com documentos externos: ele é retorno da própria Jornada
 e fecha o ciclo de retroalimentação da identidade.

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
        papel_resolucao NVARCHAR(30) NOT NULL,
        prioridade_resolucao SMALLINT NULL,
        ativo BIT NOT NULL CONSTRAINT DF_tipo_identificador_pessoa_ativo DEFAULT(1),
        CONSTRAINT ck_tipo_identificador_pessoa_elegibilidade
            CHECK(elegibilidade_deterministica IN('NAO_AUTOMATICA','CONDICIONAL','DETERMINISTICA')),
        CONSTRAINT ck_tipo_identificador_pessoa_papel
            CHECK(papel_resolucao IN('EXTERNO_HIERARQUICO','RETROALIMENTACAO_INTERNA','NAO_HIERARQUICO')),
        CONSTRAINT ck_tipo_identificador_pessoa_prioridade
            CHECK((papel_resolucao='EXTERNO_HIERARQUICO' AND prioridade_resolucao BETWEEN 1 AND 100)
               OR (papel_resolucao<>'EXTERNO_HIERARQUICO' AND prioridade_resolucao IS NULL))
    );
END;
GO

IF COL_LENGTH('ref.tipo_identificador_pessoa','papel_resolucao') IS NULL
    ALTER TABLE ref.tipo_identificador_pessoa
        ADD papel_resolucao NVARCHAR(30) NULL;
GO

IF COL_LENGTH('ref.tipo_identificador_pessoa','prioridade_resolucao') IS NULL
    ALTER TABLE ref.tipo_identificador_pessoa
        ADD prioridade_resolucao SMALLINT NULL;
GO

MERGE ref.tipo_identificador_pessoa AS t
USING (VALUES
    ('CPF','CPF','BR','CPF_BR_11_V1','DETERMINISTICA','EXTERNO_HIERARQUICO',CAST(100 AS SMALLINT)),
    ('CODIGO_BASE_ORIGEM','Código interno de cadastro de origem','BASE_PESSOA_ORIGEM','TEXTO_255_V1','CONDICIONAL','EXTERNO_HIERARQUICO',CAST(80 AS SMALLINT)),
    ('CNS','Cartão Nacional de Saúde','BR','CNS_BR_V1','CONDICIONAL','EXTERNO_HIERARQUICO',CAST(70 AS SMALLINT)),
    ('RG','Registro Geral / identidade estadual','SSP_UF','RG_QUALIFICADO_V1','CONDICIONAL','EXTERNO_HIERARQUICO',CAST(60 AS SMALLINT)),
    ('UUID_JORNADA','UUID publicado pela Jornada','JORNADA','UUID_V1','DETERMINISTICA','RETROALIMENTACAO_INTERNA',CAST(NULL AS SMALLINT)),
    ('OUTRO','Outro identificador institucional','OBRIGATORIO','TEXTO_255_V1','NAO_AUTOMATICA','NAO_HIERARQUICO',CAST(NULL AS SMALLINT))
) AS s(tipo_identificador_codigo,nome,namespace_padrao,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
ON t.tipo_identificador_codigo=s.tipo_identificador_codigo
WHEN NOT MATCHED THEN
    INSERT(tipo_identificador_codigo,nome,exige_namespace,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
    VALUES(s.tipo_identificador_codigo,s.nome,1,s.formato_codigo,s.elegibilidade_deterministica,s.papel_resolucao,s.prioridade_resolucao)
WHEN MATCHED THEN UPDATE SET
    nome=s.nome,
    formato_codigo=s.formato_codigo,
    elegibilidade_deterministica=s.elegibilidade_deterministica,
    papel_resolucao=s.papel_resolucao,
    prioridade_resolucao=s.prioridade_resolucao,
    ativo=1;
GO

UPDATE ref.tipo_identificador_pessoa
SET papel_resolucao=CASE
        WHEN tipo_identificador_codigo='UUID_JORNADA' THEN 'RETROALIMENTACAO_INTERNA'
        WHEN tipo_identificador_codigo='OUTRO' THEN 'NAO_HIERARQUICO'
        ELSE 'EXTERNO_HIERARQUICO'
    END
WHERE papel_resolucao IS NULL;
GO

ALTER TABLE ref.tipo_identificador_pessoa ALTER COLUMN papel_resolucao NVARCHAR(30) NOT NULL;
GO

/* Invariante institucional: entre identificadores externos hierárquicos, o CPF é
   a referência máxima. UUID_JORNADA é deliberadamente excluído desta comparação. */
IF EXISTS(
    SELECT 1
    FROM ref.tipo_identificador_pessoa cpf
    JOIN ref.tipo_identificador_pessoa outro
      ON outro.tipo_identificador_codigo<>'CPF'
     AND outro.ativo=1
     AND outro.papel_resolucao='EXTERNO_HIERARQUICO'
    WHERE cpf.tipo_identificador_codigo='CPF'
      AND cpf.ativo=1
      AND cpf.papel_resolucao='EXTERNO_HIERARQUICO'
      AND outro.prioridade_resolucao>=cpf.prioridade_resolucao)
    THROW 51281,'CPF deve permanecer como identificador externo de maior prioridade de resolução.',1;
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

/* Semântica de resolução:
   1. UUID_JORNADA é consultado como continuidade interna da Jornada e deve seguir
      redirecionamentos/fusões; nunca cria Pessoa implicitamente;
   2. a hierarquia externa é avaliada separadamente, com CPF no topo;
   3. CPF válido e governado é suficiente e preserva o invariante permanente:
      mesmo CPF -> mesmo UUID âncora; nenhuma outra evidência recalcula essa relação;
   4. identificadores externos de menor prioridade complementam proveniência e
      permitem detectar inconsistências, mas não substituem CPF quando este existe;
   5. se UUID Jornada recebido divergir do UUID resolvido pelo CPF, a resolução do
      CPF permanece inalterada. Registrar INCONSISTENCIA_RETROALIMENTACAO e preservar
      o UUID recebido para diagnóstico/correção; isso não é disputa de âncoras;
   6. se o UUID recebido redireciona legitimamente ao UUID do CPF, a retroalimentação
      é consistente e não deve ser classificada como conflito;
   7. identificadores condicionais só participam deterministicamente após homologação
      das respectivas regras de validação/namespace. */

/* Não existe constraint exigindo ao menos um identificador. Isso é intencional:
   observações sem ID são válidas e seguem para resolução por atributos/linkage. */

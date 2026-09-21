SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Pessoa v5 — trem estrutural SolutionSchema 3.71 (parcial)
 ---------------------------------------------------------
 Esta migration NÃO promove Jornada.SolutionSchema: o rebind 3.71 ocorre somente
 no fechamento coordenado por #411, depois das demais migrations do trem.

 Entrega:
 - #392: taxonomia explícita de ausência de CPF sem reinterpretar SEM_CPF legado;
 - #402: RG parcial + CNH como identificadores secundários, nunca âncoras;
 - #393: estado tipado de referência territorial e bloqueio compartilhado de
         INSTITUCIONAL_PRISIONAL por padrão.

 Os schemas Pessoa v5 são versionados no repositório; a ativação contratual é
 etapa separada. v4 permanece legível com sua semântica histórica.
*/

IF OBJECT_ID(N'silver.pessoa_observacao',N'U') IS NULL
   OR OBJECT_ID(N'silver.pessoa_identificador_observacao',N'U') IS NULL
   OR OBJECT_ID(N'silver.referencia_territorial_observacao',N'U') IS NULL
   OR OBJECT_ID(N'ref.tipo_identificador_pessoa',N'U') IS NULL
    THROW 52010,'Pré-requisitos de Pessoa v5 não instalados.',1;
GO

/* #392 — o banco preserva SEM_CPF histórico. O Processor v5 é quem impede
   novos payloads v5 de usarem esse valor agregado. */
IF OBJECT_ID(N'silver.ck_pessoa_cpf_motivo',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao DROP CONSTRAINT ck_pessoa_cpf_motivo;
GO
ALTER TABLE silver.pessoa_observacao WITH CHECK
ADD CONSTRAINT ck_pessoa_cpf_motivo CHECK(
    (cpf IS NULL AND cpf_ausente_motivo IN(
        N'SEM_CPF',
        N'EM_REGULARIZACAO',
        N'NAO_INFORMADO_ORIGEM',
        N'SEM_DOCUMENTACAO_BASE_DECLARADA',
        N'COM_DOCUMENTACAO_SEM_CPF_CONHECIDO'))
    OR
    (cpf IS NOT NULL AND cpf_ausente_motivo IS NULL));
GO

/* #402 — RG parcial não exige emissor/UF. CNH é secundária e não hierárquica. */
IF OBJECT_ID(N'silver.ck_pessoa_identificador_rg',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_identificador_observacao DROP CONSTRAINT ck_pessoa_identificador_rg;
GO

MERGE ref.tipo_identificador_pessoa AS t
USING (VALUES
    (N'CNH',N'Carteira Nacional de Habilitação',N'CNH_TEXTO_CANONICO_V1',N'NAO_AUTOMATICA',N'NAO_HIERARQUICO',CAST(NULL AS SMALLINT))
) AS s(tipo_identificador_codigo,nome,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
ON t.tipo_identificador_codigo=s.tipo_identificador_codigo
WHEN NOT MATCHED THEN
    INSERT(tipo_identificador_codigo,nome,exige_namespace,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
    VALUES(s.tipo_identificador_codigo,s.nome,1,s.formato_codigo,s.elegibilidade_deterministica,s.papel_resolucao,s.prioridade_resolucao)
WHEN MATCHED THEN UPDATE SET
    nome=s.nome,
    exige_namespace=1,
    formato_codigo=s.formato_codigo,
    elegibilidade_deterministica=s.elegibilidade_deterministica,
    papel_resolucao=s.papel_resolucao,
    prioridade_resolucao=s.prioridade_resolucao,
    ativo=1;
GO

UPDATE ref.tipo_identificador_pessoa
SET elegibilidade_deterministica=N'NAO_AUTOMATICA',
    papel_resolucao=N'NAO_HIERARQUICO',
    prioridade_resolucao=NULL,
    ativo=1
WHERE tipo_identificador_codigo IN(N'NIS',N'RG',N'CNH');
GO

IF OBJECT_ID(N'silver.CK_pessoa_identificador_cnh_namespace',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_identificador_observacao DROP CONSTRAINT CK_pessoa_identificador_cnh_namespace;
GO
ALTER TABLE silver.pessoa_identificador_observacao WITH CHECK
ADD CONSTRAINT CK_pessoa_identificador_cnh_namespace CHECK(
    tipo_identificador_codigo<>N'CNH' OR namespace_codigo=N'BR');
GO

/* #393 — estado é separado de natureza. Linhas v4 existentes são INFORMADA. */
IF COL_LENGTH(N'silver.referencia_territorial_observacao',N'estado_referencia') IS NULL
    ALTER TABLE silver.referencia_territorial_observacao ADD estado_referencia NVARCHAR(40) NULL;
GO
UPDATE silver.referencia_territorial_observacao
SET estado_referencia=N'INFORMADA'
WHERE estado_referencia IS NULL;
GO

IF OBJECT_ID(N'silver.ck_referencia_territorial_natureza',N'C') IS NOT NULL
    ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_natureza;
IF OBJECT_ID(N'silver.ck_referencia_territorial_domiciliar',N'C') IS NOT NULL
    ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_domiciliar;
IF OBJECT_ID(N'silver.ck_referencia_territorial_geo_situacao',N'C') IS NOT NULL
    ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_geo_situacao;
IF OBJECT_ID(N'silver.ck_referencia_territorial_geo_coerencia',N'C') IS NOT NULL
    ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_geo_coerencia;
GO

ALTER TABLE silver.referencia_territorial_observacao ALTER COLUMN estado_referencia NVARCHAR(40) NOT NULL;
ALTER TABLE silver.referencia_territorial_observacao ALTER COLUMN natureza_referencia NVARCHAR(50) NULL;
ALTER TABLE silver.referencia_territorial_observacao ALTER COLUMN situacao_geografia NVARCHAR(40) NULL;
GO

IF OBJECT_ID(N'silver.ck_referencia_territorial_estado',N'C') IS NOT NULL
    ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ck_referencia_territorial_estado;
GO
ALTER TABLE silver.referencia_territorial_observacao WITH CHECK
ADD CONSTRAINT ck_referencia_territorial_estado CHECK(
    estado_referencia IN(N'INFORMADA',N'SEM_ENDERECO_FIXO_DECLARADO'));
GO

ALTER TABLE silver.referencia_territorial_observacao WITH CHECK
ADD CONSTRAINT ck_referencia_territorial_natureza CHECK(
    (estado_referencia=N'INFORMADA' AND natureza_referencia IN(
        N'DOMICILIAR',
        N'ACOLHIMENTO_INSTITUCIONAL',
        N'INSTITUCIONAL_PRISIONAL',
        N'SERVICO_REFERENCIA',
        N'PERNOITE',
        N'REFERENCIA_TERRITORIAL_DECLARADA'))
    OR
    (estado_referencia=N'SEM_ENDERECO_FIXO_DECLARADO' AND natureza_referencia IS NULL));
GO

ALTER TABLE silver.referencia_territorial_observacao WITH CHECK
ADD CONSTRAINT ck_referencia_territorial_domiciliar CHECK(
    fonte_semantica<>N'ENDERECO_RESIDENCIAL'
    OR (estado_referencia=N'INFORMADA' AND natureza_referencia=N'DOMICILIAR'));
GO

ALTER TABLE silver.referencia_territorial_observacao WITH CHECK
ADD CONSTRAINT ck_referencia_territorial_geo_situacao CHECK(
    situacao_geografia IS NULL
    OR situacao_geografia IN(N'RESOLVIDA',N'FORA_MUNICIPIO',N'SEM_ENDERECO_APTO',N'NAO_RESOLVIDA_ORIGEM'));
GO

ALTER TABLE silver.referencia_territorial_observacao WITH CHECK
ADD CONSTRAINT ck_referencia_territorial_geo_coerencia CHECK(
    (
      estado_referencia=N'SEM_ENDERECO_FIXO_DECLARADO'
      AND situacao_geografia IS NULL
      AND subprefeitura_id IS NULL
      AND distrito_id IS NULL
      AND origem_geografia IS NULL
      AND referencia_malha IS NULL
      AND resolvido_em IS NULL
    )
    OR
    (
      estado_referencia=N'INFORMADA'
      AND (
        (
          situacao_geografia=N'RESOLVIDA'
          AND subprefeitura_id IS NOT NULL
          AND distrito_id IS NOT NULL
          AND referencia_malha IS NOT NULL
          AND origem_geografia IN(N'ORIGEM',N'ENRIQUECIMENTO_PRODAM')
        )
        OR
        (
          situacao_geografia IN(N'FORA_MUNICIPIO',N'SEM_ENDERECO_APTO',N'NAO_RESOLVIDA_ORIGEM')
          AND subprefeitura_id IS NULL
          AND distrito_id IS NULL
          AND origem_geografia IS NULL
          AND referencia_malha IS NULL
          AND resolvido_em IS NULL
        )
      )
    ));
GO

/* A view compartilhada é fail-closed: estado sem endereço fixo é qualidade, não
   endereço; natureza prisional não é projetada para consumidores comuns. */
CREATE OR ALTER VIEW silver.v_pessoa_referencia_territorial AS
SELECT po.pessoa_observacao_id,x.referencia_territorial_observacao_id,x.pessoa_atributo_observacao_id,
       x.estado_referencia,x.natureza_referencia,x.fonte_semantica,x.subprefeitura_id,x.distrito_id,
       x.situacao_geografia,x.origem_geografia,x.referencia_malha,x.resolvido_em
FROM silver.pessoa_observacao po
OUTER APPLY(
 SELECT TOP(1)
        rt.referencia_territorial_observacao_id,
        pa.pessoa_atributo_observacao_id,
        rt.estado_referencia,
        rt.natureza_referencia,
        rt.fonte_semantica,
        rt.subprefeitura_id,
        rt.distrito_id,
        rt.situacao_geografia,
        rt.origem_geografia,
        rt.referencia_malha,
        rt.resolvido_em
 FROM silver.pessoa_atributo_observacao pa
 JOIN silver.referencia_territorial_observacao rt
   ON rt.pessoa_atributo_observacao_id=pa.pessoa_atributo_observacao_id
 WHERE pa.pessoa_observacao_id=po.pessoa_observacao_id
   AND rt.estado_referencia=N'INFORMADA'
   AND rt.natureza_referencia<>N'INSTITUCIONAL_PRISIONAL'
 ORDER BY CASE WHEN rt.fonte_semantica=N'REFERENCIA_TERRITORIAL' THEN 0 ELSE 1 END,
          CASE WHEN pa.status_evidencia=N'COMPROVADO' THEN 0 ELSE 1 END,
          COALESCE(pa.referencia_evidencia,pa.verificado_em,pa.atualizado_em_origem,pa.ingested_at) DESC,
          pa.pessoa_atributo_observacao_id DESC
) x;
GO

/* QC/BI sem PII: legado SEM_CPF nunca é recodificado como novo estado v5. */
CREATE OR ALTER VIEW serving.v_bi_cpf_ausencia_taxonomia AS
SELECT
    g.codigo gestor,
    so.codigo sistema_origem,
    gpv.versao pessoa_schema_versao,
    CONVERT(date,po.source_as_of) data_referencia,
    CASE
      WHEN po.cpf IS NOT NULL THEN N'COM_CPF'
      WHEN po.cpf_ausente_motivo=N'SEM_CPF' THEN N'LEGADO_SEM_CPF_NAO_DECOMPOSTO'
      ELSE po.cpf_ausente_motivo
    END cpf_estado,
    COUNT_BIG(*) observacoes
FROM silver.pessoa_observacao po
JOIN ingestao.lote l ON l.lote_id=po.lote_id
JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_pessoa_versao_id=e.gestor_pessoa_versao_id
GROUP BY
    g.codigo,so.codigo,gpv.versao,CONVERT(date,po.source_as_of),
    CASE
      WHEN po.cpf IS NOT NULL THEN N'COM_CPF'
      WHEN po.cpf_ausente_motivo=N'SEM_CPF' THEN N'LEGADO_SEM_CPF_NAO_DECOMPOSTO'
      ELSE po.cpf_ausente_motivo
    END;
GO

/* Identificadores secundários: apenas métricas agregadas, nunca valor civil. */
CREATE OR ALTER VIEW serving.v_bi_identificador_secundario_v5 AS
SELECT
    g.codigo gestor,
    i.tipo_identificador_codigo tipo_identificador,
    i.status_validacao,
    i.status_evidencia,
    CAST(CASE WHEN i.emissor_codigo IS NULL THEN 0 ELSE 1 END AS bit) emissor_informado,
    CAST(CASE WHEN i.uf_emissor IS NULL THEN 0 ELSE 1 END AS bit) uf_informada,
    COUNT_BIG(*) observacoes
FROM silver.pessoa_identificador_observacao i
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
WHERE i.tipo_identificador_codigo IN(N'NIS',N'RG',N'CNH')
GROUP BY
    g.codigo,i.tipo_identificador_codigo,i.status_validacao,i.status_evidencia,
    CAST(CASE WHEN i.emissor_codigo IS NULL THEN 0 ELSE 1 END AS bit),
    CAST(CASE WHEN i.uf_emissor IS NULL THEN 0 ELSE 1 END AS bit);
GO

/* A natureza prisional não é nomeada na superfície compartilhada de QC. */
CREATE OR ALTER VIEW serving.v_bi_referencia_territorial_v5 AS
SELECT
    g.codigo gestor,
    rt.estado_referencia,
    CASE
      WHEN rt.natureza_referencia=N'INSTITUCIONAL_PRISIONAL' THEN N'RESTRITA'
      ELSE COALESCE(rt.natureza_referencia,N'NAO_APLICAVEL')
    END natureza_publicavel,
    COUNT_BIG(*) observacoes
FROM silver.referencia_territorial_observacao rt
JOIN silver.pessoa_atributo_observacao pa
  ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=pa.pessoa_observacao_id
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
GROUP BY
    g.codigo,rt.estado_referencia,
    CASE
      WHEN rt.natureza_referencia=N'INSTITUCIONAL_PRISIONAL' THEN N'RESTRITA'
      ELSE COALESCE(rt.natureza_referencia,N'NAO_APLICAVEL')
    END;
GO

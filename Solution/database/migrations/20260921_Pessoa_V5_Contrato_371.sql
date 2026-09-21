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
   OR OBJECT_ID(N'ref.gestor_pessoa_versao',N'U') IS NULL
    THROW 52010,'Pré-requisitos de Pessoa v5 não instalados.',1;
GO

/* #392 — o banco preserva SEM_CPF histórico. O Processor v5 é quem impede
   novos payloads v5 de usarem esse valor agregado. */
IF OBJECT_ID(N'silver.ck_pessoa_cpf_motivo',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao DROP CONSTRAINT ck_pessoa_cpf_motivo;
GO
ALTER TABLE silver.pessoa_observacao ALTER COLUMN cpf_ausente_motivo NVARCHAR(50) NULL;
IF COL_LENGTH(N'gold.beneficio_concedido',N'cpf_ausente_motivo') IS NOT NULL
    ALTER TABLE gold.beneficio_concedido ALTER COLUMN cpf_ausente_motivo NVARCHAR(50) NULL;
IF COL_LENGTH(N'gold.servico_prestado',N'cpf_ausente_motivo') IS NOT NULL
    ALTER TABLE gold.servico_prestado ALTER COLUMN cpf_ausente_motivo NVARCHAR(50) NULL;
IF COL_LENGTH(N'serving.registro_integrado',N'cpf_ausente_motivo') IS NOT NULL
    ALTER TABLE serving.registro_integrado ALTER COLUMN cpf_ausente_motivo NVARCHAR(50) NULL;
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

/* Gold preserva a taxonomia explícita; não colapsa estados v5 em SEM_CPF. */
IF OBJECT_ID(N'gold.ck_gold_pessoa_status_cpf',N'C') IS NOT NULL
    ALTER TABLE gold.pessoa DROP CONSTRAINT ck_gold_pessoa_status_cpf;
GO
ALTER TABLE gold.pessoa ALTER COLUMN status_cpf NVARCHAR(50) NOT NULL;
ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_status_cpf CHECK(
    (cpf IS NOT NULL AND status_cpf=N'PRESENTE')
    OR
    (cpf IS NULL AND status_cpf IN(
        N'SEM_CPF',
        N'EM_REGULARIZACAO',
        N'NAO_INFORMADO_ORIGEM',
        N'SEM_DOCUMENTACAO_BASE_DECLARADA',
        N'COM_DOCUMENTACAO_SEM_CPF_CONHECIDO')));
GO

/* Recompõe Gold com a mesma taxonomia sem truncamento nem recodificação. */
CREATE OR ALTER PROCEDURE identidade.sp_recompor_gold_pessoa @pessoa_uuid UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 DECLARE @jornada_own_tran BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
 IF @jornada_own_tran=1 BEGIN TRANSACTION;
 BEGIN TRY
   IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@pessoa_uuid AND status='ATIVO')
   BEGIN
     DELETE FROM gold.pessoa WHERE pessoa_uuid=@pessoa_uuid;
     IF @jornada_own_tran=1 COMMIT TRANSACTION;
     RETURN;
   END;

   DECLARE @src TABLE(
     pessoa_uuid UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
     cpf CHAR(11) NULL,
     nome_completo NVARCHAR(500) NULL,
     data_nascimento DATE NULL,
     nome_mae NVARCHAR(500) NULL,
     fontes INT NOT NULL,
     divergente BIT NOT NULL,
     cpf_ausente_motivo NVARCHAR(50) NULL,
     estado_identidade NVARCHAR(20) NOT NULL,
     completude_nucleo NVARCHAR(20) NOT NULL
   );

   ;WITH obs_ids AS(
      -- vínculo corrente resolvido
      SELECT po.pessoa_observacao_id
      FROM silver.pessoa_observacao po
      JOIN identidade.v_vinculo_corrente vc
        ON vc.pessoa_observacao_id=po.pessoa_observacao_id
      WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO'

      UNION

      -- referência progressiva já publicada
      SELECT po.pessoa_observacao_id
      FROM silver.pessoa_observacao po
      JOIN identidade.pessoa_origem_progressiva p
        ON p.pessoa_origem_id=po.pessoa_origem_id
      WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@pessoa_uuid

      UNION

      -- casca progressiva própria; initial_uuid é linhagem, não evidência
      SELECT po.pessoa_observacao_id
      FROM silver.pessoa_observacao po
      JOIN identidade.pessoa_origem_progressiva p
        ON p.pessoa_origem_id=po.pessoa_origem_id
      WHERE p.initial_uuid=@pessoa_uuid
        AND p.estado IN('PROVISORIA','INDEFINIDA')
   ),
   obs AS(
      SELECT po.*
      FROM silver.pessoa_observacao po
      JOIN obs_ids i ON i.pessoa_observacao_id=po.pessoa_observacao_id
   ),
   stats AS(
      SELECT COUNT(DISTINCT gestor_id) fontes,
             CASE
               WHEN COUNT(DISTINCT nome_cmp)>1
                 OR COUNT(DISTINCT CONVERT(char(10),data_nascimento,23))>1
                 OR COUNT(DISTINCT nome_mae_cmp)>1
               THEN CAST(1 AS bit)
               ELSE CAST(0 AS bit)
             END divergente
      FROM obs
   )
   INSERT @src(
     pessoa_uuid,cpf,nome_completo,data_nascimento,nome_mae,fontes,divergente,
     cpf_ausente_motivo,estado_identidade,completude_nucleo)
   SELECT @pessoa_uuid,
          COALESCE(
            (SELECT TOP(1) identificador
             FROM identidade.identity_map
             WHERE pessoa_uuid=@pessoa_uuid
               AND tipo='CPF'
               AND vigencia_fim IS NULL
               AND estado='ATIVO'
             ORDER BY vigencia_inicio DESC,identity_map_id DESC),
            cpf_src.cpf),
          nome_src.nome_completo,
          nasc_src.data_nascimento,
          mae_src.nome_mae,
          st.fontes,
          st.divergente,
          cpf_ausencia.cpf_ausente_motivo,
          CASE
            WHEN EXISTS(
              SELECT 1
              FROM identidade.v_vinculo_corrente vc
              WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO')
              OR EXISTS(
                SELECT 1
                FROM identidade.pessoa_origem_progressiva p
                WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@pessoa_uuid)
              OR EXISTS(
                SELECT 1
                FROM identidade.cpf_ancora a
                WHERE a.pessoa_uuid=@pessoa_uuid)
              THEN 'REFERENCIA'
            WHEN EXISTS(
              SELECT 1
              FROM identidade.pessoa_origem_progressiva p
              WHERE p.initial_uuid=@pessoa_uuid AND p.estado='INDEFINIDA')
              THEN 'INDEFINIDA'
            ELSE 'PROVISORIA'
          END,
          CASE
            WHEN nome_src.nome_completo IS NOT NULL
             AND nasc_src.data_nascimento IS NOT NULL
             AND mae_src.nome_mae IS NOT NULL
              THEN 'COMPLETO'
            ELSE 'PARCIAL'
          END
   FROM stats st
   OUTER APPLY(
      SELECT TOP(1) o.cpf
      FROM obs o
      LEFT JOIN silver.pessoa_campo_verificacao_observacao v
        ON v.pessoa_observacao_id=o.pessoa_observacao_id
       AND v.campo_codigo='CPF'
      WHERE o.cpf IS NOT NULL
      ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
               v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
   ) cpf_src
   OUTER APPLY(
      SELECT TOP(1) o.cpf_ausente_motivo
      FROM obs o
      WHERE o.cpf IS NULL
        AND o.cpf_ausente_motivo IS NOT NULL
      ORDER BY o.source_as_of DESC,o.pessoa_observacao_id DESC
   ) cpf_ausencia
   OUTER APPLY(
      SELECT TOP(1) o.nome_completo
      FROM obs o
      LEFT JOIN silver.pessoa_campo_verificacao_observacao v
        ON v.pessoa_observacao_id=o.pessoa_observacao_id
       AND v.campo_codigo='NOME_COMPLETO'
      WHERE o.nome_completo IS NOT NULL
      ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
               v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
   ) nome_src
   OUTER APPLY(
      SELECT TOP(1) o.data_nascimento
      FROM obs o
      LEFT JOIN silver.pessoa_campo_verificacao_observacao v
        ON v.pessoa_observacao_id=o.pessoa_observacao_id
       AND v.campo_codigo='DATA_NASCIMENTO'
      WHERE o.data_nascimento IS NOT NULL
      ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
               v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
   ) nasc_src
   OUTER APPLY(
      SELECT TOP(1) o.nome_mae
      FROM obs o
      LEFT JOIN silver.pessoa_campo_verificacao_observacao v
        ON v.pessoa_observacao_id=o.pessoa_observacao_id
       AND v.campo_codigo='NOME_MAE'
      WHERE o.nome_mae IS NOT NULL
      ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
               v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
   ) mae_src
   WHERE EXISTS(SELECT 1 FROM obs);

   MERGE gold.pessoa WITH (HOLDLOCK) AS t
   USING @src s ON t.pessoa_uuid=s.pessoa_uuid
   WHEN MATCHED THEN UPDATE SET
        cpf=s.cpf,
        status_cpf=CASE
          WHEN s.cpf IS NOT NULL THEN N'PRESENTE'
          ELSE COALESCE(s.cpf_ausente_motivo,N'SEM_CPF')
        END,
        nome_completo=s.nome_completo,
        data_nascimento=s.data_nascimento,
        nome_mae=s.nome_mae,
        fontes_distintas=s.fontes,
        estado_concordancia=CASE
          WHEN s.divergente=1 THEN 'DIVERGENTE'
          WHEN s.fontes>1 THEN 'CORROBORADO'
          ELSE 'BASELINE_FONTE_UNICA'
        END,
        estado_identidade=s.estado_identidade,
        completude_nucleo=s.completude_nucleo,
        atualizado_em=SYSDATETIMEOFFSET()
   WHEN NOT MATCHED THEN INSERT(
        pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
        fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em)
        VALUES(
          s.pessoa_uuid,s.cpf,
          CASE
          WHEN s.cpf IS NOT NULL THEN N'PRESENTE'
          ELSE COALESCE(s.cpf_ausente_motivo,N'SEM_CPF')
        END,
          s.nome_completo,s.data_nascimento,s.nome_mae,s.fontes,
          CASE
            WHEN s.divergente=1 THEN 'DIVERGENTE'
            WHEN s.fontes>1 THEN 'CORROBORADO'
            ELSE 'BASELINE_FONTE_UNICA'
          END,
          s.estado_identidade,s.completude_nucleo,SYSDATETIMEOFFSET());

   -- initial_uuid associado a outra referência deixa a Gold corrente,
   -- mas permanece imutável no ledger/eventos.
   IF NOT EXISTS(SELECT 1 FROM @src)
     DELETE FROM gold.pessoa WHERE pessoa_uuid=@pessoa_uuid;

   IF @jornada_own_tran=1 COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF @jornada_own_tran=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH
END;
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

IF NOT EXISTS(
    SELECT 1
    FROM sys.default_constraints dc
    JOIN sys.columns c
      ON c.object_id=dc.parent_object_id
     AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID(N'silver.referencia_territorial_observacao')
      AND c.name=N'estado_referencia')
    ALTER TABLE silver.referencia_territorial_observacao
      ADD CONSTRAINT DF_referencia_territorial_estado DEFAULT(N'INFORMADA') FOR estado_referencia;
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

/* A natureza prisional é omitida integralmente da superfície compartilhada.
   Persistência Silver continua disponível somente a fluxos institucionais autorizados futuros. */
CREATE OR ALTER VIEW serving.v_bi_referencia_territorial_v5 AS
SELECT
    g.codigo gestor,
    rt.estado_referencia,
    COALESCE(rt.natureza_referencia,N'NAO_APLICAVEL') natureza_publicavel,
    COUNT_BIG(*) observacoes
FROM silver.referencia_territorial_observacao rt
JOIN silver.pessoa_atributo_observacao pa
  ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=pa.pessoa_observacao_id
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
WHERE rt.natureza_referencia IS NULL
   OR rt.natureza_referencia<>N'INSTITUCIONAL_PRISIONAL'
GROUP BY
    g.codigo,rt.estado_referencia,COALESCE(rt.natureza_referencia,N'NAO_APLICAVEL');
GO

/* Catálogo contratual: v5 entra como RASCUNHO; esta migration não troca a versão ativa. */
IF EXISTS(
    SELECT 1
    FROM ref.gestor_pessoa_versao v
    JOIN ref.gestor g ON g.gestor_id=v.gestor_id
    WHERE v.versao=5
      AND (
        v.pessoa_schema_ref<>CONCAT(N'config/contracts/gestores/',g.codigo,N'/pessoa/v5/pessoa.schema.json')
        OR v.pessoa_schema_sha256<>CASE g.codigo
            WHEN N'SEHAB' THEN 0xEBE9B8177CA35BEC6FEB8D746854448D697801FAEDCFE7DC8F112C42ADFBBA62
            WHEN N'SMADS' THEN 0x75407173BCD0F9D6DFB2727BF6044FD7B232AB7D5EDA3B34ADA9473F17C98FCE
            WHEN N'SMDET' THEN 0x37C34AED6257E700158D11D196A848AA795F10986DA5E19D8A4C422966D0361B
            WHEN N'SMS' THEN 0x48C10C2B74543E27F12121B9A9B958B5FFD2AEC2EA39641B2420103315D647F1
          END
      ))
    THROW 52011,'Pessoa v5 já cadastrada com referência/hash divergente.',1;
GO

INSERT ref.gestor_pessoa_versao(
    gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)
SELECT
    g.gestor_id,5,'2026-09-21',
    CONCAT(N'config/contracts/gestores/',g.codigo,N'/pessoa/v5/pessoa.schema.json'),
    CASE g.codigo
      WHEN N'SEHAB' THEN 0xEBE9B8177CA35BEC6FEB8D746854448D697801FAEDCFE7DC8F112C42ADFBBA62
      WHEN N'SMADS' THEN 0x75407173BCD0F9D6DFB2727BF6044FD7B232AB7D5EDA3B34ADA9473F17C98FCE
      WHEN N'SMDET' THEN 0x37C34AED6257E700158D11D196A848AA795F10986DA5E19D8A4C422966D0361B
      WHEN N'SMS' THEN 0x48C10C2B74543E27F12121B9A9B958B5FFD2AEC2EA39641B2420103315D647F1
    END,
    N'RASCUNHO',NULL
FROM ref.gestor g
WHERE g.codigo IN(N'SEHAB',N'SMADS',N'SMDET',N'SMS')
  AND NOT EXISTS(
      SELECT 1 FROM ref.gestor_pessoa_versao v
      WHERE v.gestor_id=g.gestor_id AND v.versao=5);
GO

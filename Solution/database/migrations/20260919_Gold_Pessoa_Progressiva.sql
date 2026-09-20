SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Identidade progressiva: completude cadastral deixa de ser condição de existência na Gold.
-- A obrigatoriedade de atributos pertence ao schema versionado da fonte.
IF COL_LENGTH('silver.pessoa_observacao','nome_completo') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_completo NVARCHAR(500) NULL;
IF COL_LENGTH('silver.pessoa_observacao','nome_cmp') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_cmp NVARCHAR(500) NULL;
IF COL_LENGTH('silver.pessoa_observacao','data_nascimento') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN data_nascimento DATE NULL;
IF COL_LENGTH('silver.pessoa_observacao','nome_mae') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae NVARCHAR(500) NULL;
IF COL_LENGTH('silver.pessoa_observacao','nome_mae_cmp') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae_cmp NVARCHAR(500) NULL;
GO

IF COL_LENGTH('gold.pessoa','nome_completo') IS NOT NULL
    ALTER TABLE gold.pessoa ALTER COLUMN nome_completo NVARCHAR(500) NULL;
IF COL_LENGTH('gold.pessoa','data_nascimento') IS NOT NULL
    ALTER TABLE gold.pessoa ALTER COLUMN data_nascimento DATE NULL;
IF COL_LENGTH('gold.pessoa','nome_mae') IS NOT NULL
    ALTER TABLE gold.pessoa ALTER COLUMN nome_mae NVARCHAR(500) NULL;
GO

IF COL_LENGTH('gold.pessoa','estado_identidade') IS NULL
    ALTER TABLE gold.pessoa ADD estado_identidade NVARCHAR(20) NULL;
IF COL_LENGTH('gold.pessoa','completude_nucleo') IS NULL
    ALTER TABLE gold.pessoa ADD completude_nucleo NVARCHAR(20) NULL;
GO

UPDATE gold.pessoa
SET estado_identidade=COALESCE(estado_identidade,'REFERENCIA'),
    completude_nucleo=CASE
        WHEN nome_completo IS NOT NULL AND data_nascimento IS NOT NULL AND nome_mae IS NOT NULL THEN 'COMPLETO'
        ELSE 'PARCIAL'
    END
WHERE estado_identidade IS NULL OR completude_nucleo IS NULL;
GO

ALTER TABLE gold.pessoa ALTER COLUMN estado_identidade NVARCHAR(20) NOT NULL;
ALTER TABLE gold.pessoa ALTER COLUMN completude_nucleo NVARCHAR(20) NOT NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='df_gold_pessoa_estado_identidade')
    ALTER TABLE gold.pessoa ADD CONSTRAINT df_gold_pessoa_estado_identidade DEFAULT('REFERENCIA') FOR estado_identidade;
IF NOT EXISTS(
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='df_gold_pessoa_completude_nucleo')
    ALTER TABLE gold.pessoa ADD CONSTRAINT df_gold_pessoa_completude_nucleo DEFAULT('COMPLETO') FOR completude_nucleo;
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='ck_gold_pessoa_status_cpf')
    ALTER TABLE gold.pessoa DROP CONSTRAINT ck_gold_pessoa_status_cpf;

ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_status_cpf CHECK(
    (cpf IS NOT NULL AND status_cpf='PRESENTE')
    OR (cpf IS NULL AND status_cpf IN('SEM_CPF','EM_REGULARIZACAO','NAO_INFORMADO_ORIGEM')));
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='ck_gold_pessoa_estado_identidade')
    ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_estado_identidade
      CHECK(estado_identidade IN('PROVISORIA','REFERENCIA','INDEFINIDA'));

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='ck_gold_pessoa_completude_nucleo')
    ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_completude_nucleo CHECK(
      (completude_nucleo='COMPLETO'
       AND nome_completo IS NOT NULL
       AND data_nascimento IS NOT NULL
       AND nome_mae IS NOT NULL)
      OR
      (completude_nucleo='PARCIAL'
       AND (nome_completo IS NULL OR data_nascimento IS NULL OR nome_mae IS NULL)));
GO

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
     cpf_ausente_motivo NVARCHAR(30) NULL,
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
          WHEN s.cpf IS NOT NULL THEN 'PRESENTE'
          WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO'
          WHEN s.cpf_ausente_motivo='NAO_INFORMADO_ORIGEM' THEN 'NAO_INFORMADO_ORIGEM'
          ELSE 'SEM_CPF'
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
            WHEN s.cpf IS NOT NULL THEN 'PRESENTE'
            WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO'
            WHEN s.cpf_ausente_motivo='NAO_INFORMADO_ORIGEM' THEN 'NAO_INFORMADO_ORIGEM'
            ELSE 'SEM_CPF'
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

CREATE OR ALTER VIEW serving.v_pessoa AS
SELECT pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
       fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em
FROM gold.pessoa;
GO

CREATE OR ALTER VIEW serving.v_bi_qualidade_pessoa AS
SELECT po.pessoa_observacao_id,g.codigo gestor,po.source_as_of,
 COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
 COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
 pg.natureza_referencia natureza_referencia_territorial,
 pg.situacao_geografia,pg.referencia_malha,
 CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,
 CASE WHEN LEN(LTRIM(RTRIM(po.nome_completo)))>0 THEN 1 ELSE 0 END nome_preenchido,
 CASE WHEN po.data_nascimento IS NULL THEN 0 ELSE 1 END nascimento_preenchido,
 CASE WHEN LEN(LTRIM(RTRIM(po.nome_mae)))>0 THEN 1 ELSE 0 END nome_mae_preenchido,
 CASE WHEN pg.subprefeitura_id IS NULL OR pg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_preenchida,
 po.cpf_ausente_motivo,gp.status_cpf,gp.estado_identidade,gp.completude_nucleo
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
LEFT JOIN identidade.v_vinculo_corrente vc
  ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN identidade.pessoa_origem_progressiva pr
  ON pr.pessoa_origem_id=po.pessoa_origem_id
LEFT JOIN gold.pessoa gp
  ON gp.pessoa_uuid=COALESCE(
      vc.pessoa_uuid,
      CASE
        WHEN pr.estado IN('PROVISORIA','INDEFINIDA') THEN pr.initial_uuid
        WHEN pr.estado='REFERENCIA' THEN pr.canonical_uuid
      END)
LEFT JOIN silver.v_pessoa_referencia_territorial pg
  ON pg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id;
GO

CREATE OR ALTER VIEW serving.v_bi_completude_pessoa AS
SELECT estado_identidade,completude_nucleo,status_cpf,
       COUNT_BIG(*) pessoas,
       SUM(CASE WHEN nome_completo IS NULL THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) nome_ausente,
       SUM(CASE WHEN data_nascimento IS NULL THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) nascimento_ausente,
       SUM(CASE WHEN nome_mae IS NULL THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) nome_mae_ausente
FROM gold.pessoa
GROUP BY estado_identidade,completude_nucleo,status_cpf;
GO

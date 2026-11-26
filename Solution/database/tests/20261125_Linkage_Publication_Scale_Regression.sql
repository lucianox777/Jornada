-- Only CI disposable synthetic DB, after a completed V9 FULL.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT OFF;
IF DB_NAME() NOT LIKE N'JornadaV9Dev[_]%' OR NOT EXISTS(
 SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.V9.DevAttempt')
 THROW 52995,'Disposable regression database required.',1;
DECLARE @run uniqueidentifier=(SELECT linkage_run_id FROM identidade.linkage_run WHERE modelo_versao=9 AND status=N'PUBLICADO');
IF @run IS NULL THROW 52996,'Completed V9 fixture required.',1;

-- Each negative executes against actual tables and their enabled triggers.
-- Transaction rollback must preserve all durable fixture rows.
PRINT 'V9_SQL_FIXTURE_STAGE=NEGATIVE_GUARDS';
DECLARE @case int=1,@expected int,@actual int,@id bigint,@source bigint,@uuid uniqueidentifier;
WHILE @case<=6
BEGIN
 SET @expected=CASE @case WHEN 1 THEN 51822 WHEN 2 THEN 51823 WHEN 3 THEN 51110 WHEN 4 THEN 51111 WHEN 5 THEN 51112 ELSE 51117 END;
 SET @actual=0;
 BEGIN TRY
  BEGIN TRANSACTION;
  IF @case=1 UPDATE TOP(1) identidade.linkage_resultado SET motivo=N'NEGATIVE_SCALE_GUARD' WHERE linkage_run_id=@run;
  IF @case=2 UPDATE TOP(1) identidade.linkage_resultado SET motivo_publicacao=motivo_publicacao WHERE linkage_run_id=@run;
  IF @case=3
  BEGIN
   INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
    SELECT TOP(1) sistema_origem_id,CONCAT(N'SYNTH-GUARD-',NEWID()),base_pessoa_origem_id FROM silver.pessoa_origem;
   SET @source=CONVERT(bigint,SCOPE_IDENTITY());
   SET @uuid=NEWID();
   INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,N'ATIVO');
   INSERT identidade.pessoa_origem_progressiva(pessoa_origem_id,initial_uuid,criado_em,atualizado_em)
    VALUES(@source,@uuid,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
   DELETE FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source;
  END;
  IF @case=4 UPDATE TOP(1) identidade.pessoa_origem_progressiva SET criado_em=DATEADD(second,-1,criado_em) WHERE versao>0;
  IF @case=5 UPDATE TOP(1) identidade.pessoa_origem_progressiva SET versao=versao+2 WHERE versao>0;
  IF @case=6 UPDATE TOP(1) identidade.pessoa_origem_progressiva SET versao=versao+1 WHERE versao>0;
  ROLLBACK TRANSACTION;
 END TRY
 BEGIN CATCH
  SET @actual=ERROR_NUMBER();
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 END CATCH;
 IF @actual<>@expected
 BEGIN
  DECLARE @message nvarchar(200)=CONCAT(N'Guard regression: expected ',@expected,N', actual ',@actual);
  THROW 52997,@message,1;
 END;
 SET @case+=1;
END;
PRINT 'PUBLICATION_GUARDS_SIX_CODES_PASS';

-- Compare the exact current-link multiset with the candidate physical
-- join plan. Never compare only counts or discard duplicates.
PRINT 'V9_SQL_FIXTURE_STAGE=CURRENT_MULTISET';
SELECT pessoa_uuid,pessoa_observacao_id,status,COUNT_BIG(*) AS multiplicidade
INTO #scale_current_original FROM identidade.v_vinculo_corrente
GROUP BY pessoa_uuid,pessoa_observacao_id,status;
-- Materialize the latest published probabilistic row per observation
-- once; the normative view remains the independent oracle above.
;WITH ranked AS (
 SELECT r.pessoa_observacao_id,r.pessoa_uuid_publicado AS pessoa_uuid,r.status_publicacao AS status,
 ROW_NUMBER() OVER(PARTITION BY r.pessoa_observacao_id
 ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) AS rn
 FROM identidade.linkage_resultado r
 JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
 WHERE lr.status=N'PUBLICADO' AND r.resultado_publicacao IS NOT NULL
 AND r.status_publicacao IS NOT NULL AND r.publicado_em IS NOT NULL
)
SELECT pessoa_observacao_id,pessoa_uuid,status
INTO #scale_prob_current FROM ranked WHERE rn=1;
CREATE UNIQUE CLUSTERED INDEX IX_scale_prob_current ON #scale_prob_current(pessoa_observacao_id);
SELECT b.pessoa_uuid,b.pessoa_observacao_id,b.status
INTO #scale_current_materialized
FROM identidade.vinculo_fonte b WHERE b.ativo=1
AND (b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
 OR NOT EXISTS(SELECT 1 FROM #scale_prob_current p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id))
UNION ALL
SELECT p.pessoa_uuid,p.pessoa_observacao_id,p.status FROM #scale_prob_current p
WHERE NOT EXISTS(SELECT 1 FROM identidade.vinculo_fonte b WHERE b.ativo=1
 AND b.pessoa_observacao_id=p.pessoa_observacao_id
 AND b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'));
CREATE INDEX IX_scale_current_materialized ON #scale_current_materialized(pessoa_uuid,status,pessoa_observacao_id);
SELECT pessoa_uuid,pessoa_observacao_id,status,COUNT_BIG(*) AS multiplicidade
INTO #scale_current_staged FROM #scale_current_materialized
GROUP BY pessoa_uuid,pessoa_observacao_id,status;
IF EXISTS(SELECT * FROM #scale_current_original EXCEPT SELECT * FROM #scale_current_staged)
 OR EXISTS(SELECT * FROM #scale_current_staged EXCEPT SELECT * FROM #scale_current_original)
 THROW 52998,'Current link multiset differs.',1;
DROP TABLE #scale_current_original,#scale_current_materialized,#scale_current_staged,#scale_prob_current;
PRINT 'PUBLICATION_CURRENT_LINK_MULTISET_PASS';

-- Independent original scalar name selection, all actual Gold persons.
-- Force drift through the enabled trigger, never insert Gold test persons.
PRINT 'V9_SQL_FIXTURE_STAGE=CANONICAL_NAME';
SELECT g.pessoa_uuid,
 LEFT(src.nome_cmp,CHARINDEX(N' ',src.nome_cmp+N' ')-1) AS chave
INTO #scale_name_expected
FROM gold.pessoa g
OUTER APPLY(
 SELECT TOP(1) po.nome_cmp
 FROM silver.pessoa_observacao po
 JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
 WHERE vc.pessoa_uuid=g.pessoa_uuid AND vc.status=N'RESOLVIDO'
   AND po.nome_completo=g.nome_completo
   AND NULLIF(LTRIM(RTRIM(po.nome_cmp)),N'') IS NOT NULL
 ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
) src;
IF NOT EXISTS(SELECT 1 FROM #scale_name_expected WHERE chave IS NOT NULL)
 THROW 52998,'Gold name positive control missing.',1;
BEGIN TRANSACTION;
UPDATE gold.pessoa SET nome_publicacao_normalizado=N'SYNTH_SCALE_DRIFT',
 nome_publicacao_metodo_versao=N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1',
 nome_publicacao_normalizacao_versao=N'IDENTITY_NORMALIZATION_V1';
IF EXISTS(SELECT 1 FROM gold.pessoa g FULL JOIN #scale_name_expected e ON e.pessoa_uuid=g.pessoa_uuid
 WHERE g.pessoa_uuid IS NULL OR e.pessoa_uuid IS NULL
 OR ISNULL(g.nome_publicacao_normalizado,N'')<>ISNULL(e.chave,N'')
 OR ISNULL(g.nome_publicacao_metodo_versao,N'')<>CASE WHEN e.chave IS NULL THEN N'' ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END
 OR ISNULL(g.nome_publicacao_normalizacao_versao,N'')<>CASE WHEN e.chave IS NULL THEN N'' ELSE N'IDENTITY_NORMALIZATION_V1' END)
BEGIN
 ROLLBACK TRANSACTION;
 THROW 52998,'Gold canonical name key differs.',1;
END;
ROLLBACK TRANSACTION;
DROP TABLE #scale_name_expected;
PRINT 'PUBLICATION_GOLD_CANONICAL_NAME_TRIGGER_PASS';

-- Use exactly the old and new SQL on the same persisted run and same initial
-- data; compare every semantic Gold column in both directions (ignore only
-- updated-at timestamp generated by the recomposition itself).
-- No scientific replay, model change or activation. A rollback leaves no DML.
GO
CREATE OR ALTER PROCEDURE identidade.sp_scale_regression_gold_original
 @run_id uniqueidentifier
AS
BEGIN
 IF @@TRANCOUNT=0 THROW 52992,'Recomposição exige transação explícita.',1;
 SET NOCOUNT ON;
 
 SELECT u.pessoa_uuid
 INTO #gold_progressiva
 FROM (
     SELECT r.pessoa_uuid_publicado AS pessoa_uuid
     FROM identidade.linkage_resultado r
     WHERE r.linkage_run_id=@run_id
       AND r.pessoa_uuid_publicado IS NOT NULL
     UNION
     SELECT p.initial_uuid
     FROM identidade.linkage_resultado r
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=r.pessoa_observacao_id
     JOIN identidade.pessoa_origem_progressiva p
       ON p.pessoa_origem_id=po.pessoa_origem_id
     WHERE r.linkage_run_id=@run_id
       AND p.initial_uuid IS NOT NULL
 ) u;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_progressiva_uuid
     ON #gold_progressiva(pessoa_uuid);
 
 SELECT DISTINCT x.pessoa_uuid,x.pessoa_observacao_id
 INTO #gold_obs_ids
 FROM (
     SELECT a.pessoa_uuid,vc.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN identidade.v_vinculo_corrente vc
       ON vc.pessoa_uuid=a.pessoa_uuid AND vc.status='RESOLVIDO'
 
     UNION
 
     SELECT a.pessoa_uuid,po.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN identidade.pessoa_origem_progressiva p
       ON p.estado='REFERENCIA' AND p.canonical_uuid=a.pessoa_uuid
     JOIN silver.pessoa_observacao po
       ON po.pessoa_origem_id=p.pessoa_origem_id
 
     UNION
 
     SELECT a.pessoa_uuid,po.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN identidade.pessoa_origem_progressiva p
       ON p.initial_uuid=a.pessoa_uuid
      AND p.estado IN('PROVISORIA','INDEFINIDA')
     JOIN silver.pessoa_observacao po
       ON po.pessoa_origem_id=p.pessoa_origem_id
 ) x;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_obs_ids_uuid_obs
     ON #gold_obs_ids(pessoa_uuid,pessoa_observacao_id);
 
 SELECT oi.pessoa_uuid,
        COUNT(DISTINCT po.gestor_id) AS fontes,
        CAST(CASE
             WHEN COUNT(DISTINCT po.nome_cmp)>1
               OR COUNT(DISTINCT CONVERT(char(10),po.data_nascimento,23))>1
               OR COUNT(DISTINCT po.nome_mae_cmp)>1
             THEN 1 ELSE 0 END AS bit) AS divergente
 INTO #gold_stats
 FROM #gold_obs_ids oi
 JOIN silver.pessoa_observacao po
   ON po.pessoa_observacao_id=oi.pessoa_observacao_id
 GROUP BY oi.pessoa_uuid;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_stats_uuid
     ON #gold_stats(pessoa_uuid);
 
 SELECT st.pessoa_uuid,
        CONVERT(char(11),COALESCE(identity_cpf.identificador,cpf_src.cpf)) AS cpf,
        nome_src.nome_completo,
        nasc_src.data_nascimento,
        mae_src.nome_mae,
        st.fontes,
        st.divergente,
        cpf_ausencia.cpf_ausente_motivo,
        CONVERT(nvarchar(20),CASE
          WHEN EXISTS(
            SELECT 1
            FROM identidade.v_vinculo_corrente vc
            WHERE vc.pessoa_uuid=st.pessoa_uuid AND vc.status='RESOLVIDO')
            OR EXISTS(
              SELECT 1
              FROM identidade.pessoa_origem_progressiva p
              WHERE p.estado='REFERENCIA' AND p.canonical_uuid=st.pessoa_uuid)
            OR EXISTS(
              SELECT 1
              FROM identidade.cpf_ancora a
              WHERE a.pessoa_uuid=st.pessoa_uuid)
            THEN 'REFERENCIA'
          WHEN EXISTS(
            SELECT 1
            FROM identidade.pessoa_origem_progressiva p
            WHERE p.initial_uuid=st.pessoa_uuid AND p.estado='INDEFINIDA')
            THEN 'INDEFINIDA'
          ELSE 'PROVISORIA'
        END) AS estado_identidade,
        CONVERT(nvarchar(20),CASE
          WHEN nome_src.nome_completo IS NOT NULL
           AND nasc_src.data_nascimento IS NOT NULL
           AND mae_src.nome_mae IS NOT NULL
            THEN 'COMPLETO'
          ELSE 'PARCIAL'
        END) AS completude_nucleo
 INTO #gold_src
 FROM #gold_stats st
 OUTER APPLY(
     SELECT TOP(1) im.identificador
     FROM identidade.identity_map im
     WHERE im.pessoa_uuid=st.pessoa_uuid
       AND im.tipo='CPF'
       AND im.vigencia_fim IS NULL
       AND im.estado='ATIVO'
     ORDER BY im.vigencia_inicio DESC,im.identity_map_id DESC
 ) identity_cpf
 OUTER APPLY(
     SELECT TOP(1) po.cpf
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='CPF'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.cpf IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) cpf_src
 OUTER APPLY(
     SELECT TOP(1) po.cpf_ausente_motivo
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.cpf IS NULL
       AND po.cpf_ausente_motivo IS NOT NULL
     ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) cpf_ausencia
 OUTER APPLY(
     SELECT TOP(1) po.nome_completo
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='NOME_COMPLETO'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.nome_completo IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) nome_src
 OUTER APPLY(
     SELECT TOP(1) po.data_nascimento
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='DATA_NASCIMENTO'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.data_nascimento IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) nasc_src
 OUTER APPLY(
     SELECT TOP(1) po.nome_mae
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='NOME_MAE'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.nome_mae IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) mae_src;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_src_uuid
     ON #gold_src(pessoa_uuid);
 
 MERGE gold.pessoa WITH (HOLDLOCK) AS t
 USING #gold_src s ON t.pessoa_uuid=s.pessoa_uuid
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
 
 DELETE g
 FROM gold.pessoa g
 JOIN #gold_progressiva a ON a.pessoa_uuid=g.pessoa_uuid
 LEFT JOIN #gold_src s ON s.pessoa_uuid=g.pessoa_uuid
 WHERE s.pessoa_uuid IS NULL;
END;
GO
CREATE OR ALTER PROCEDURE identidade.sp_scale_regression_gold_optimized
 @run_id uniqueidentifier
AS
BEGIN
 IF @@TRANCOUNT=0 THROW 52992,'Recomposição exige transação explícita.',1;
 SET NOCOUNT ON;
 
 -- Preserve the full current-link view once, after publication header transition.
 SELECT pessoa_uuid,pessoa_observacao_id,status
 INTO #jrn_gold_current FROM identidade.v_vinculo_corrente;
 CREATE INDEX IX_jrn_gold_current_uuid ON #jrn_gold_current(pessoa_uuid,status,pessoa_observacao_id);
 
 SELECT u.pessoa_uuid
 INTO #gold_progressiva
 FROM (
     SELECT r.pessoa_uuid_publicado AS pessoa_uuid
     FROM identidade.linkage_resultado r
     WHERE r.linkage_run_id=@run_id
       AND r.pessoa_uuid_publicado IS NOT NULL
     UNION
     SELECT p.initial_uuid
     FROM identidade.linkage_resultado r
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=r.pessoa_observacao_id
     JOIN identidade.pessoa_origem_progressiva p
       ON p.pessoa_origem_id=po.pessoa_origem_id
     WHERE r.linkage_run_id=@run_id
       AND p.initial_uuid IS NOT NULL
 ) u;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_progressiva_uuid
     ON #gold_progressiva(pessoa_uuid);
 
 SELECT DISTINCT x.pessoa_uuid,x.pessoa_observacao_id
 INTO #gold_obs_ids
 FROM (
     SELECT a.pessoa_uuid,vc.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN #jrn_gold_current vc
       ON vc.pessoa_uuid=a.pessoa_uuid AND vc.status='RESOLVIDO'
 
     UNION
 
     SELECT a.pessoa_uuid,po.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN identidade.pessoa_origem_progressiva p
       ON p.estado='REFERENCIA' AND p.canonical_uuid=a.pessoa_uuid
     JOIN silver.pessoa_observacao po
       ON po.pessoa_origem_id=p.pessoa_origem_id
 
     UNION
 
     SELECT a.pessoa_uuid,po.pessoa_observacao_id
     FROM #gold_progressiva a
     JOIN identidade.pessoa ip
       ON ip.pessoa_uuid=a.pessoa_uuid AND ip.status='ATIVO'
     JOIN identidade.pessoa_origem_progressiva p
       ON p.initial_uuid=a.pessoa_uuid
      AND p.estado IN('PROVISORIA','INDEFINIDA')
     JOIN silver.pessoa_observacao po
       ON po.pessoa_origem_id=p.pessoa_origem_id
 ) x;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_obs_ids_uuid_obs
     ON #gold_obs_ids(pessoa_uuid,pessoa_observacao_id);
 
 SELECT oi.pessoa_uuid,
        COUNT(DISTINCT po.gestor_id) AS fontes,
        CAST(CASE
             WHEN COUNT(DISTINCT po.nome_cmp)>1
               OR COUNT(DISTINCT CONVERT(char(10),po.data_nascimento,23))>1
               OR COUNT(DISTINCT po.nome_mae_cmp)>1
             THEN 1 ELSE 0 END AS bit) AS divergente
 INTO #gold_stats
 FROM #gold_obs_ids oi
 JOIN silver.pessoa_observacao po
   ON po.pessoa_observacao_id=oi.pessoa_observacao_id
 GROUP BY oi.pessoa_uuid;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_stats_uuid
     ON #gold_stats(pessoa_uuid);
 
 SELECT st.pessoa_uuid,
        CONVERT(char(11),COALESCE(identity_cpf.identificador,cpf_src.cpf)) AS cpf,
        nome_src.nome_completo,
        nasc_src.data_nascimento,
        mae_src.nome_mae,
        st.fontes,
        st.divergente,
        cpf_ausencia.cpf_ausente_motivo,
        CONVERT(nvarchar(20),CASE
          WHEN EXISTS(
            SELECT 1
            FROM #jrn_gold_current vc
            WHERE vc.pessoa_uuid=st.pessoa_uuid AND vc.status='RESOLVIDO')
            OR EXISTS(
              SELECT 1
              FROM identidade.pessoa_origem_progressiva p
              WHERE p.estado='REFERENCIA' AND p.canonical_uuid=st.pessoa_uuid)
            OR EXISTS(
              SELECT 1
              FROM identidade.cpf_ancora a
              WHERE a.pessoa_uuid=st.pessoa_uuid)
            THEN 'REFERENCIA'
          WHEN EXISTS(
            SELECT 1
            FROM identidade.pessoa_origem_progressiva p
            WHERE p.initial_uuid=st.pessoa_uuid AND p.estado='INDEFINIDA')
            THEN 'INDEFINIDA'
          ELSE 'PROVISORIA'
        END) AS estado_identidade,
        CONVERT(nvarchar(20),CASE
          WHEN nome_src.nome_completo IS NOT NULL
           AND nasc_src.data_nascimento IS NOT NULL
           AND mae_src.nome_mae IS NOT NULL
            THEN 'COMPLETO'
          ELSE 'PARCIAL'
        END) AS completude_nucleo
 INTO #gold_src
 FROM #gold_stats st
 OUTER APPLY(
     SELECT TOP(1) im.identificador
     FROM identidade.identity_map im
     WHERE im.pessoa_uuid=st.pessoa_uuid
       AND im.tipo='CPF'
       AND im.vigencia_fim IS NULL
       AND im.estado='ATIVO'
     ORDER BY im.vigencia_inicio DESC,im.identity_map_id DESC
 ) identity_cpf
 OUTER APPLY(
     SELECT TOP(1) po.cpf
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='CPF'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.cpf IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) cpf_src
 OUTER APPLY(
     SELECT TOP(1) po.cpf_ausente_motivo
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.cpf IS NULL
       AND po.cpf_ausente_motivo IS NOT NULL
     ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) cpf_ausencia
 OUTER APPLY(
     SELECT TOP(1) po.nome_completo
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='NOME_COMPLETO'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.nome_completo IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) nome_src
 OUTER APPLY(
     SELECT TOP(1) po.data_nascimento
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='DATA_NASCIMENTO'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.data_nascimento IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) nasc_src
 OUTER APPLY(
     SELECT TOP(1) po.nome_mae
     FROM #gold_obs_ids oi
     JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=oi.pessoa_observacao_id
     LEFT JOIN silver.pessoa_campo_verificacao_observacao v
       ON v.pessoa_observacao_id=po.pessoa_observacao_id
      AND v.campo_codigo='NOME_MAE'
     WHERE oi.pessoa_uuid=st.pessoa_uuid
       AND po.nome_mae IS NOT NULL
     ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
              v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC
 ) mae_src;
 
 CREATE UNIQUE CLUSTERED INDEX IX_gold_src_uuid
     ON #gold_src(pessoa_uuid);
 
 MERGE gold.pessoa WITH (HOLDLOCK) AS t
 USING #gold_src s ON t.pessoa_uuid=s.pessoa_uuid
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
 
 DELETE g
 FROM gold.pessoa g
 JOIN #gold_progressiva a ON a.pessoa_uuid=g.pessoa_uuid
 LEFT JOIN #gold_src s ON s.pessoa_uuid=g.pessoa_uuid
 WHERE s.pessoa_uuid IS NULL;
END;
GO
-- Version 8 is a transaction-local published-envelope regression fixture,
-- not a V8 scorer run or a new scientific campaign. Use the same real SQL
-- inputs and frozen decisions; do not modify the V9 run or activate a model.
BEGIN TRANSACTION;
DECLARE @run9 uniqueidentifier=(SELECT linkage_run_id FROM identidade.linkage_run WHERE modelo_versao=9 AND status=N'PUBLICADO');
DECLARE @run8 uniqueidentifier=NEWID(),@model8 uniqueidentifier=NEWID(),@now datetimeoffset(7)=SYSDATETIMEOFFSET();
IF EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE versao=8) THROW 52996,'V8 fixture model already exists.',1;
INSERT identidade.modelo_linkage(modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,base_referencia,gerado_em)
 VALUES(@model8,8,N'RASCUNHO',N'REGRESSION_PUBLICATION_ONLY',N'IDENTITY_NORMALIZATION_V1',N'FIXTURE',N'SYNTHETIC',@now);
INSERT identidade.linkage_run(linkage_run_id,modelo_id,modelo_versao,tipo_run,status,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,iniciado_em,finalizado_em,publicado_em)
 SELECT @run8,@model8,8,tipo_run,N'PUBLICADO',registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,@now,@now,@now
 FROM identidade.linkage_run WHERE linkage_run_id=@run9;
INSERT identidade.linkage_resultado(linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
 resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,pessoa_origem_id_publicado,progressiva_versao,politica_publicacao_versao,universo_referencia,publicado_em)
 SELECT @run8,@model8,8,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
 resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,pessoa_origem_id_publicado,progressiva_versao,politica_publicacao_versao,universo_referencia,@now
 FROM identidade.linkage_resultado WHERE linkage_run_id=@run9;
PRINT 'V9_SQL_FIXTURE_STAGE=RECOMPOSITION';
DECLARE @expected TABLE(pessoa_uuid uniqueidentifier PRIMARY KEY,cpf char(11),status_cpf nvarchar(120),
 nome_completo nvarchar(max),data_nascimento date,nome_mae nvarchar(max),fontes_distintas int,
 estado_concordancia nvarchar(40),estado_identidade nvarchar(20),completude_nucleo nvarchar(20),
 nome_publicacao_normalizado nvarchar(200),nome_publicacao_metodo_versao nvarchar(80),nome_publicacao_normalizacao_versao nvarchar(80));
DECLARE @version int=8,@run uniqueidentifier;
WHILE @version<=9
BEGIN
 SET @run=CASE WHEN @version=8 THEN @run8 ELSE @run9 END;
 DELETE FROM @expected;
 SAVE TRANSACTION original_gold;
 EXEC identidade.sp_scale_regression_gold_original @run_id=@run;
 INSERT @expected SELECT pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,
  estado_concordancia,estado_identidade,completude_nucleo,nome_publicacao_normalizado,nome_publicacao_metodo_versao,nome_publicacao_normalizacao_versao FROM gold.pessoa;
 ROLLBACK TRANSACTION original_gold;
 SAVE TRANSACTION optimized_gold;
 IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.LinkagePublicationScale')
  EXEC identidade.sp_recompor_gold_linkage_lote @run_id=@run;
 ELSE
  EXEC identidade.sp_scale_regression_gold_optimized @run_id=@run;
 IF EXISTS(SELECT * FROM @expected EXCEPT SELECT pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,nome_publicacao_normalizado,nome_publicacao_metodo_versao,nome_publicacao_normalizacao_versao FROM gold.pessoa)
  OR EXISTS(SELECT pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,nome_publicacao_normalizado,nome_publicacao_metodo_versao,nome_publicacao_normalizacao_versao FROM gold.pessoa EXCEPT SELECT * FROM @expected)
  THROW 52998,'Gold recomposition differs.',1;
 ROLLBACK TRANSACTION optimized_gold;
 PRINT CONCAT('PUBLICATION_GOLD_V',@version,'_REGRESSION_PASS');
 SET @version+=1;
END;
ROLLBACK TRANSACTION;
DROP PROCEDURE identidade.sp_scale_regression_gold_original;
DROP PROCEDURE identidade.sp_scale_regression_gold_optimized;
GO

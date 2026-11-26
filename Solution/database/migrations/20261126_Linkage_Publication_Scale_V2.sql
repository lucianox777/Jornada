-- GOV-V9-DEV-ESCALA-FULL-20261125 / #1931. Administrative SQLCMD only.
-- V2 is a reversible extension of the V1 publication migration.
:on error exit
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'Jornada.Linkage.Runner.Publish',
 @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;
IF @lock<0 THROW 52991,'Publication migration lock unavailable.',1;
IF OBJECT_ID(N'identidade.sp_recompor_gold_linkage_lote',N'P') IS NULL
 OR OBJECT_ID(N'gold.tr_pessoa_nome_publicacao',N'TR') IS NULL
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.LinkagePublicationScale'
   AND CONVERT(nvarchar(32),value) IN(N'20261125_V1',N'20261126_V2'))
 THROW 52993,'Publication V1 prerequisites missing.',1;
GO
CREATE OR ALTER PROCEDURE identidade.sp_recompor_gold_linkage_lote
 @run_id uniqueidentifier
AS
BEGIN
 IF @@TRANCOUNT=0 THROW 52992,'Recomposição exige transação explícita.',1;
 SET NOCOUNT ON;
 
 -- Preserve the full current-link view once, after publication header transition.
 SELECT pessoa_uuid,pessoa_observacao_id,status
 INTO #jrn_gold_current FROM identidade.v_vinculo_corrente OPTION (HASH JOIN);
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
CREATE OR ALTER TRIGGER gold.tr_pessoa_nome_publicacao
ON gold.pessoa
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(OBJECT_ID('gold.tr_pessoa_nome_publicacao'),'AFTER','DML')>1 RETURN;

    -- Materialize the exact resolved view membership once per statement.
    -- The original per-person TOP/ordering, names and null handling stay unchanged.
    SELECT vc.pessoa_uuid,vc.pessoa_observacao_id,vc.status
    INTO #jrn_gold_name_current
    FROM identidade.v_vinculo_corrente vc
    JOIN inserted i ON i.pessoa_uuid=vc.pessoa_uuid
    WHERE vc.status='RESOLVIDO'
    OPTION (HASH JOIN);
    CREATE INDEX IX_jrn_gold_name_current_uuid ON #jrn_gold_name_current(pessoa_uuid,pessoa_observacao_id);

    ;WITH chave AS(
        SELECT i.pessoa_uuid,
               LEFT(src.nome_cmp,CHARINDEX(N' ',src.nome_cmp+N' ')-1) AS nome_publicacao_normalizado
        FROM inserted i
        OUTER APPLY(
            SELECT TOP(1) po.nome_cmp
            FROM silver.pessoa_observacao po
            JOIN #jrn_gold_name_current vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE vc.pessoa_uuid=i.pessoa_uuid
              AND vc.status='RESOLVIDO'
              AND po.nome_completo=i.nome_completo
              AND NULLIF(LTRIM(RTRIM(po.nome_cmp)),N'') IS NOT NULL
            ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
        ) src
    )
    UPDATE g
    SET nome_publicacao_normalizado=c.nome_publicacao_normalizado,
        nome_publicacao_metodo_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END,
        nome_publicacao_normalizacao_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IDENTITY_NORMALIZATION_V1' END
    FROM gold.pessoa g
    JOIN chave c ON c.pessoa_uuid=g.pessoa_uuid
    WHERE ISNULL(g.nome_publicacao_normalizado,N'')<>ISNULL(c.nome_publicacao_normalizado,N'')
       OR ISNULL(g.nome_publicacao_metodo_versao,N'')<>CASE WHEN c.nome_publicacao_normalizado IS NULL THEN N'' ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END
       OR ISNULL(g.nome_publicacao_normalizacao_versao,N'')<>CASE WHEN c.nome_publicacao_normalizado IS NULL THEN N'' ELSE N'IDENTITY_NORMALIZATION_V1' END;
END;
GO
EXEC sys.sp_updateextendedproperty @name=N'Jornada.LinkagePublicationScale',@value=N'20261126_V2';
COMMIT TRANSACTION;
GO

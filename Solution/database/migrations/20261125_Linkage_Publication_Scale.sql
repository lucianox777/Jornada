-- GOV-V9-DEV-ESCALA-FULL-20261125 / #1931. Administrative SQLCMD only.
-- Post-baseline extension: do not modify sealed 3.70 manifest or historical migrations.
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
IF OBJECT_ID(N'identidade.tr_linkage_resultado_publicacao_imutavel',N'TR') IS NULL
 OR OBJECT_ID(N'identidade.tr_progressiva_origem_guard',N'TR') IS NULL
 OR OBJECT_ID(N'identidade.v_vinculo_corrente',N'V') IS NULL
 THROW 52993,'Publication schema prerequisites missing.',1;
GO
CREATE OR ALTER TRIGGER identidade.tr_linkage_resultado_publicacao_imutavel
ON identidade.linkage_resultado
AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 -- Materialize only the columns consulted by the original predicates.
 SELECT linkage_resultado_id,linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em INTO #jrn_lr_guard_inserted FROM inserted;
 SELECT linkage_resultado_id,linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em INTO #jrn_lr_guard_deleted FROM deleted;
 CREATE UNIQUE CLUSTERED INDEX IX_jrn_lr_guard_i ON #jrn_lr_guard_inserted(linkage_resultado_id);
 CREATE UNIQUE CLUSTERED INDEX IX_jrn_lr_guard_d ON #jrn_lr_guard_deleted(linkage_resultado_id);

 IF EXISTS(
   SELECT 1
   FROM #jrn_lr_guard_inserted i
   JOIN #jrn_lr_guard_deleted d ON d.linkage_resultado_id=i.linkage_resultado_id
   WHERE i.linkage_run_id<>d.linkage_run_id
      OR i.modelo_id<>d.modelo_id
      OR i.modelo_versao<>d.modelo_versao
      OR i.pessoa_observacao_id<>d.pessoa_observacao_id
      OR ISNULL(CONVERT(NVARCHAR(36),i.pessoa_uuid_resolvido),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.pessoa_uuid_resolvido),N'')
      OR ISNULL(CONVERT(NVARCHAR(36),i.melhor_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.melhor_candidato_uuid),N'')
      OR i.score_melhor<>d.score_melhor
      OR ISNULL(CONVERT(NVARCHAR(36),i.segundo_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.segundo_candidato_uuid),N'')
      OR ISNULL(i.score_segundo,CONVERT(DECIMAL(9,8),-1))<>ISNULL(d.score_segundo,CONVERT(DECIMAL(9,8),-1))
      OR ISNULL(i.margem,CONVERT(DECIMAL(18,8),-1))<>ISNULL(d.margem,CONVERT(DECIMAL(18,8),-1))
      OR i.status<>d.status
      OR ISNULL(i.motivo,N'')<>ISNULL(d.motivo,N'')
      OR i.calculado_em<>d.calculado_em)
   THROW 51822,'Evidência bruta de linkage_resultado é imutável.',1;

 IF EXISTS(
   SELECT 1
   FROM #jrn_lr_guard_inserted i
   JOIN identidade.linkage_run lr ON lr.linkage_run_id=i.linkage_run_id
   WHERE lr.status<>N'EXECUTANDO')
   THROW 51823,'Envelope de publicação só pode ser finalizado enquanto o run está EXECUTANDO.',1;
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- Materialize only the columns consulted by the original predicates.
 SELECT pessoa_origem_id,initial_uuid,criado_em,legacy_pessoa_uuid,versao,estado,canonical_uuid,ultimo_destino_externo_uuid,ultima_resolucao_em INTO #jrn_po_guard_inserted FROM inserted;
 SELECT pessoa_origem_id,initial_uuid,criado_em,legacy_pessoa_uuid,versao,estado,canonical_uuid,ultimo_destino_externo_uuid,ultima_resolucao_em INTO #jrn_po_guard_deleted FROM deleted;
 CREATE UNIQUE CLUSTERED INDEX IX_jrn_po_guard_i ON #jrn_po_guard_inserted(pessoa_origem_id);
 CREATE UNIQUE CLUSTERED INDEX IX_jrn_po_guard_d ON #jrn_po_guard_deleted(pessoa_origem_id);
 IF EXISTS(SELECT 1 FROM #jrn_po_guard_deleted d LEFT JOIN #jrn_po_guard_inserted i ON i.pessoa_origem_id=d.pessoa_origem_id WHERE i.pessoa_origem_id IS NULL)
   THROW 51110,'Referência inicial de origem não pode ser excluída ou transferida.',1;
 IF EXISTS(SELECT 1 FROM #jrn_po_guard_inserted i JOIN #jrn_po_guard_deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.initial_uuid<>d.initial_uuid OR i.criado_em<>d.criado_em OR
        ISNULL(i.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000'))
   THROW 51111,'UUID inicial, origem e referência legada são imutáveis.',1;
 IF EXISTS(SELECT 1 FROM #jrn_po_guard_inserted i JOIN #jrn_po_guard_deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.versao<d.versao OR i.versao>d.versao+1 OR
    (i.versao=d.versao AND (i.estado<>d.estado OR
      ISNULL(i.canonical_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.canonical_uuid,'00000000-0000-0000-0000-000000000000') OR
      ISNULL(i.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000') OR
      ISNULL(i.ultima_resolucao_em,'0001-01-01')<>ISNULL(d.ultima_resolucao_em,'0001-01-01'))))
   THROW 51112,'Alteração de referência exige avanço de uma versão.',1;
 IF EXISTS(SELECT 1 FROM #jrn_po_guard_inserted i JOIN #jrn_po_guard_deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.versao<>d.versao AND NOT EXISTS(
   SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e
   WHERE e.pessoa_origem_id=i.pessoa_origem_id AND e.versao=i.versao AND e.tipo='RESOLUCAO' AND e.estado=i.estado
     AND (e.canonical_uuid=i.canonical_uuid OR (e.canonical_uuid IS NULL AND i.canonical_uuid IS NULL))
     AND e.ocorrido_em=i.ultima_resolucao_em))
   THROW 51117,'Avanço exige recibo de resolução correspondente.',1;
END;
GO
CREATE OR ALTER PROCEDURE identidade.sp_recompor_gold_linkage_lote
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
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.LinkagePublicationScale')
 EXEC sys.sp_updateextendedproperty @name=N'Jornada.LinkagePublicationScale',@value=N'20261125_V1';
ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.LinkagePublicationScale',@value=N'20261125_V1';
COMMIT TRANSACTION;
GO

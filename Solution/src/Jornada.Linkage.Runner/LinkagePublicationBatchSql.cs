namespace Jornada.Linkage.Runner;

/// <summary>
/// SQL set-based usado na publicação atômica do linkage. O conjunto de UUIDs
/// afetados é materializado pelo Runner nas tabelas temporárias da mesma sessão.
/// </summary>
internal static class LinkagePublicationBatchSql
{
    internal const string RecomposeAffectedGoldSql = """
        SET NOCOUNT ON;

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
        """;

    internal const string RefreshServingAssignmentsSql = """
        SET NOCOUNT ON;

        ;WITH afetadas AS (
            SELECT DISTINCT pessoa_observacao_id
            FROM identidade.linkage_resultado
            WHERE linkage_run_id=@run_id
        ), corrente AS (
            SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
            FROM afetadas a
            LEFT JOIN identidade.v_vinculo_corrente vc
              ON vc.pessoa_observacao_id=a.pessoa_observacao_id
        )
        UPDATE b SET
            pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
            estado_atribuicao_identidade=CASE
                WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                ELSE 'PENDENTE_IDENTIDADE'
            END,
            atualizado_em=SYSDATETIMEOFFSET()
        FROM gold.beneficio_concedido b
        JOIN silver.registro_observacao ro
          ON ro.registro_observacao_id=b.registro_observacao_id
        JOIN corrente c
          ON c.pessoa_observacao_id=ro.pessoa_observacao_id;

        ;WITH afetadas AS (
            SELECT DISTINCT pessoa_observacao_id
            FROM identidade.linkage_resultado
            WHERE linkage_run_id=@run_id
        ), corrente AS (
            SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
            FROM afetadas a
            LEFT JOIN identidade.v_vinculo_corrente vc
              ON vc.pessoa_observacao_id=a.pessoa_observacao_id
        )
        UPDATE s SET
            pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
            estado_atribuicao_identidade=CASE
                WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                ELSE 'PENDENTE_IDENTIDADE'
            END,
            atualizado_em=SYSDATETIMEOFFSET()
        FROM gold.servico_prestado s
        JOIN silver.registro_observacao ro
          ON ro.registro_observacao_id=s.registro_observacao_id
        JOIN corrente c
          ON c.pessoa_observacao_id=ro.pessoa_observacao_id;

        ;WITH afetadas AS (
            SELECT DISTINCT pessoa_observacao_id
            FROM identidade.linkage_resultado
            WHERE linkage_run_id=@run_id
        ), corrente AS (
            SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
            FROM afetadas a
            LEFT JOIN identidade.v_vinculo_corrente vc
              ON vc.pessoa_observacao_id=a.pessoa_observacao_id
        )
        UPDATE ri SET
            pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
            estado_atribuicao_identidade=CASE
                WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                ELSE 'PENDENTE_IDENTIDADE'
            END,
            atualizado_em=SYSDATETIMEOFFSET()
        FROM serving.registro_integrado ri
        JOIN silver.registro_observacao ro
          ON ro.registro_observacao_id=ri.registro_observacao_id
        JOIN corrente c
          ON c.pessoa_observacao_id=ro.pessoa_observacao_id;
        """;
}

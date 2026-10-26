-- DT-10 canonical V9 lower nonmatch: additive, default-off, no policy rows seeded.
-- This migration defines a governed publication contract, not activation.
-- The experimental LocalDB patch is NOT reused here.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U') IS NULL
BEGIN
    CREATE TABLE identidade.v9_dt10_lower_policy(
        politica_versao NVARCHAR(120) NOT NULL PRIMARY KEY,
        habilitada BIT NOT NULL CONSTRAINT df_v9_dt10_lower_disabled DEFAULT(0),
        modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
        modelo_versao INT NOT NULL,
        modelo_fingerprint BINARY(32) NOT NULL,
        fit_fingerprint BINARY(32) NOT NULL,
        snapshot_sha256 BINARY(32) NOT NULL,
        snapshot_bytes VARBINARY(MAX) NOT NULL,
        limiar_inferior DECIMAL(18,8) NOT NULL,
        limiar_superior DECIMAL(18,8) NOT NULL,
        aprovacao_id UNIQUEIDENTIFIER NOT NULL
            REFERENCES auditoria.modelo_linkage_aprovacao(aprovacao_id),
        criado_em DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT ck_v9_dt10_lower_thresholds
            CHECK(limiar_inferior>0 AND limiar_inferior<limiar_superior
                  AND limiar_superior<=1),
        CONSTRAINT ck_v9_dt10_lower_snapshot_size
            CHECK(DATALENGTH(snapshot_bytes)>0 AND DATALENGTH(snapshot_bytes)<=67108864),
        CONSTRAINT ck_v9_dt10_lower_policy_version
            CHECK(politica_versao LIKE N'V9_DT10_LOWER[_]%'
                  AND politica_versao NOT LIKE N'%EXPERIMENTAL%')
    );
END;
GO

-- Explicit canonical redefinition. The preceding DT-10 batch body is retained
-- in full, including the serializable transaction, source-wide precedence,
-- idempotent replay, locks, and append-only event ledger.
-- DT-10: publicação progressiva em lote, sem cursor por origem.
-- Todas as transições, eventos e propagação do run usam a transação SERIALIZABLE
-- do Runner. A iteração de applocks abaixo é SOMENTE por referência distinta:
-- sys.sp_getapplock não possui interface set-based e os locks precisam ser os
-- mesmos de identidade.sp_recompor_gold_pessoa para coordenar composição.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote
    @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT = 0
        THROW 51801, 'DT-10: publicação progressiva em lote exige transação explícita.', 1;

    DECLARE @isolation SMALLINT;
    SELECT @isolation = transaction_isolation_level
    FROM sys.dm_exec_sessions
    WHERE session_id = @@SPID;
    IF @isolation <> 4
        THROW 51825, 'DT-10: publicação em lote exige isolamento SERIALIZABLE.', 1;

    -- O mesmo lock do runner serializa a publicação quando a procedure é chamada
    -- diretamente por integrações e impede a interposição de dois runs.
    DECLARE @publication_lock INT;
    EXEC @publication_lock = sys.sp_getapplock
        @Resource=N'Jornada.Linkage.Runner.Publish',
        @LockMode=N'Exclusive',
        @LockOwner=N'Transaction',
        @LockTimeout=60000;
    IF @publication_lock < 0
        THROW 51826, 'DT-10: não foi possível serializar o run.', 1;

    DECLARE @status NVARCHAR(30), @avaliados BIGINT, @elegiveis BIGINT,
            @itens BIGINT, @resultados BIGINT;
    SELECT @status=status, @avaliados=avaliados, @elegiveis=registros_elegiveis
    FROM identidade.linkage_run WITH(UPDLOCK,HOLDLOCK)
    WHERE linkage_run_id=@linkage_run_id;

    IF @status IS NULL OR @status<>N'EXECUTANDO'
        THROW 51803, 'DT-10: somente run EXECUTANDO pode publicar.', 1;

    SELECT @itens=COUNT_BIG(*)
    FROM identidade.linkage_run_item WITH(HOLDLOCK)
    WHERE linkage_run_id=@linkage_run_id;

    SELECT @resultados=COUNT_BIG(*)
    FROM identidade.linkage_resultado WITH(UPDLOCK,HOLDLOCK)
    WHERE linkage_run_id=@linkage_run_id;

    IF @avaliados<>@elegiveis OR @itens<>@elegiveis OR @resultados<>@elegiveis
        THROW 51818, 'DT-10: run incompleto não pode publicar identidade progressiva.', 1;

    IF EXISTS(
        SELECT 1 FROM identidade.linkage_resultado r WITH(HOLDLOCK)
        WHERE r.linkage_run_id=@linkage_run_id
          AND NOT EXISTS(
              SELECT 1 FROM identidade.linkage_run_item i WITH(HOLDLOCK)
              WHERE i.linkage_run_id=r.linkage_run_id
                AND i.pessoa_observacao_id=r.pessoa_observacao_id))
        OR EXISTS(
        SELECT 1 FROM identidade.linkage_run_item i WITH(HOLDLOCK)
        WHERE i.linkage_run_id=@linkage_run_id
          AND NOT EXISTS(
              SELECT 1 FROM identidade.linkage_resultado r WITH(HOLDLOCK)
              WHERE r.linkage_run_id=i.linkage_run_id
                AND r.pessoa_observacao_id=i.pessoa_observacao_id))
        THROW 51820, 'DT-10: universo do run divergente da evidência materializada.', 1;

    -- Snapshot estável dos resultados e precedência determinística/governada.
    -- MAX(protegido) por origem abaixo impede publicar um irmão probabilístico
    -- quando QUALQUER observação da mesma origem tem precedência.
    SELECT r.linkage_resultado_id, r.pessoa_observacao_id, po.pessoa_origem_id,
           po.versao_interna, r.resultado_publicacao, r.pessoa_uuid_publicado,
           r.status_publicacao, r.motivo_publicacao, r.politica_publicacao_versao,
           r.universo_referencia, r.status AS raw_status, r.motivo AS raw_motivo,
           r.modelo_id, r.modelo_versao,
           CASE WHEN EXISTS(
               SELECT 1 FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
               WHERE vf.pessoa_observacao_id=r.pessoa_observacao_id AND vf.ativo=1
                 AND vf.metodo_resolucao IN(
                     N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                     N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'))
               THEN 1 ELSE 0 END AS protegido
    INTO #dt10_rows
    FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
    JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
      ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id=@linkage_run_id;

    IF EXISTS(
        SELECT 1 FROM #dt10_rows x
        WHERE x.pessoa_origem_id IS NOT NULL
          AND NOT EXISTS(
              SELECT 1 FROM identidade.pessoa_origem_progressiva p WITH(HOLDLOCK)
              WHERE p.pessoa_origem_id=x.pessoa_origem_id))
        THROW 51819, 'DT-10: origem persistente sem initial_uuid.', 1;

    ;WITH ranked AS (
        SELECT x.*,
               ROW_NUMBER() OVER(
                   PARTITION BY x.pessoa_origem_id
                   ORDER BY x.versao_interna DESC,x.pessoa_observacao_id DESC) AS rn,
               MAX(x.protegido) OVER(
                   PARTITION BY x.pessoa_origem_id) AS origem_protegida
        FROM #dt10_rows x
        WHERE x.pessoa_origem_id IS NOT NULL
    )
    SELECT x.pessoa_origem_id, x.pessoa_observacao_id AS leader_observacao_id,
           x.resultado_publicacao, x.pessoa_uuid_publicado, x.status_publicacao,
           x.motivo_publicacao, x.politica_publicacao_versao, x.universo_referencia,
           x.raw_status, x.raw_motivo, x.modelo_id, x.modelo_versao,
           p.initial_uuid, p.canonical_uuid AS current_uuid, p.estado AS current_state,
           p.versao AS current_version, p.ultimo_destino_externo_uuid,
           e.versao AS replay_version,
           CASE WHEN e.versao IS NOT NULL
                 OR (x.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
                     AND p.estado=N'REFERENCIA' AND p.canonical_uuid=x.pessoa_uuid_publicado)
                 OR (x.resultado_publicacao=N'NOVA_IDENTIDADE'
                     AND p.estado=N'REFERENCIA' AND p.canonical_uuid=p.initial_uuid)
                 OR (x.resultado_publicacao=N'INDEFINIDA' AND p.estado=N'INDEFINIDA')
                THEN 0 ELSE 1 END AS needs_event
    INTO #dt10_leaders
    FROM ranked x
    JOIN identidade.pessoa_origem_progressiva p WITH(UPDLOCK,HOLDLOCK)
      ON p.pessoa_origem_id=x.pessoa_origem_id
    LEFT JOIN identidade.pessoa_origem_progressiva_evento e WITH(HOLDLOCK)
      ON e.pessoa_origem_id=x.pessoa_origem_id
     AND e.linkage_run_id=@linkage_run_id
    WHERE x.rn=1 AND x.origem_protegida=0;

    -- Mesmos guardas da procedure escalar, aplicados a TODAS as origens antes
    -- de escrever o primeiro evento. Replay do mesmo run retém a versão já emitida.
    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.resultado_publicacao IS NULL
           OR l.politica_publicacao_versao IS NULL
           OR LTRIM(RTRIM(l.politica_publicacao_versao))=N'')
        THROW 51805, 'DT-10: política/decisão de publicação ausente.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL
          AND l.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
          AND (l.status_publicacao<>N'RESOLVIDO' OR l.pessoa_uuid_publicado IS NULL))
        THROW 51806, 'DT-10: associação exige UUID e status RESOLVIDO.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
          AND l.current_state=N'REFERENCIA'
          AND (l.current_uuid<>l.pessoa_uuid_publicado OR l.current_uuid IS NULL))
        THROW 51807, 'DT-10: linkage não pode trocar referência já estabelecida.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.needs_event=1 AND l.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
          AND NOT EXISTS(
              SELECT 1 FROM identidade.cpf_ancora a WITH(HOLDLOCK)
              WHERE a.pessoa_uuid=l.pessoa_uuid_publicado)
          AND NOT EXISTS(
              SELECT 1 FROM identidade.pessoa_origem_progressiva p WITH(HOLDLOCK)
              WHERE p.pessoa_origem_id<>l.pessoa_origem_id AND p.estado=N'REFERENCIA'
                AND p.canonical_uuid=l.pessoa_uuid_publicado)
          AND NOT EXISTS(
              SELECT 1 FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
              WHERE vf.ativo=1 AND vf.status=N'RESOLVIDO'
                AND vf.pessoa_uuid=l.pessoa_uuid_publicado
                AND vf.metodo_resolucao IN(
                    N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                    N'CORRECAO_GOVERNADA')))
        THROW 51808, 'DT-10: destino probabilístico não é referência estabelecida.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND (l.status_publicacao<>N'RESOLVIDO'
               OR l.pessoa_uuid_publicado IS NULL
               OR l.pessoa_uuid_publicado<>l.initial_uuid))
        THROW 51809, 'DT-10: NOVA_IDENTIDADE somente promove o initial_uuid da origem.', 1;

    -- DT-10 V9: the lower nonmatch is NOT equivalent to no candidates.
    -- No operational producer emits this reason until a separate DT-15 gate.
    -- An explicit, versioned, content-addressed policy must authorize EACH
    -- leader; any mismatch aborts before the first ledger write.
    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND l.raw_motivo=N'LIMIAR_INFERIOR_V9_GOVERNADO')
    BEGIN
        IF EXISTS(
            SELECT 1 FROM #dt10_leaders l
            LEFT JOIN identidade.v9_dt10_lower_policy p WITH(UPDLOCK,HOLDLOCK)
              ON p.politica_versao=l.politica_publicacao_versao AND p.habilitada=1
            LEFT JOIN identidade.modelo_linkage m WITH(HOLDLOCK)
              ON m.modelo_id=p.modelo_id
            LEFT JOIN auditoria.modelo_linkage_aprovacao a WITH(HOLDLOCK)
              ON a.aprovacao_id=p.aprovacao_id
            WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
              AND l.raw_motivo=N'LIMIAR_INFERIOR_V9_GOVERNADO'
              AND (p.politica_versao IS NULL
                   OR p.modelo_id<>l.modelo_id OR p.modelo_versao<>l.modelo_versao
                   OR m.modelo_id IS NULL OR m.status<>N'ATIVO' OR m.versao<>p.modelo_versao
                   OR m.algoritmo_versao<>N'FELLEGI_SUNTER_DECISION_EVIDENCE_COMPOSITE_NOMINAL_V9'
                   OR a.aprovacao_id IS NULL OR a.acao<>N'ACTIVATE'
                   OR a.modelo_id<>p.modelo_id OR a.modelo_snapshot_sha256<>p.modelo_fingerprint
                   OR a.dossie_sha256 IS NULL
                   OR a.modelo_linkage_aprovacao_id<>(
                       SELECT MAX(a2.modelo_linkage_aprovacao_id)
                       FROM auditoria.modelo_linkage_aprovacao a2 WITH(HOLDLOCK)
                       WHERE a2.modelo_id=p.modelo_id AND a2.acao=N'ACTIVATE')
                   OR COALESCE(l.motivo_publicacao,N'')<>N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO'
                   OR l.universo_referencia<>CONCAT(N'V9_SNAPSHOT_SHA256:',
                         CONVERT(VARCHAR(64),p.snapshot_sha256,2))
                   OR HASHBYTES('SHA2_256',p.snapshot_bytes)<>p.snapshot_sha256
                   OR COALESCE(ISJSON(CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8),0)<>1
                   OR COALESCE(JSON_VALUE(CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.SchemaVersion'),N'')<>N'V9_FITTED_SNAPSHOT_V3'
                   OR COALESCE(JSON_VALUE(CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.CandidateGrouping.CandidateGrouping'),N'')<>N'GOLD'
                   OR LEN(COALESCE(JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.FitFingerprintSha256'),N''))<>64
                   OR TRY_CONVERT(BINARY(32),JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.FitFingerprintSha256'),2) IS NULL
                   OR TRY_CONVERT(BINARY(32),JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.FitFingerprintSha256'),2)<>p.fit_fingerprint
                   OR TRY_CONVERT(DECIMAL(18,8),JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.Threshold')) IS NULL
                   OR TRY_CONVERT(DECIMAL(18,8),JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.Threshold'))<>p.limiar_superior
                   OR LEN(COALESCE(JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.GoldParentSnapshotSha256'),N''))<>64
                   OR TRY_CONVERT(BINARY(32),JSON_VALUE(
                         CONVERT(VARCHAR(MAX),p.snapshot_bytes)
                         COLLATE Latin1_General_100_CI_AS_SC_UTF8,
                         '$.GoldParentSnapshotSha256'),2) IS NULL))
            THROW 51830, 'DT-10 V9: flag, modelo ATIVO, aprovacao DT-15 ou snapshot/fit divergem.', 1;

        -- The DT-15 approval must still match the CURRENT model and dossier,
        -- not merely an ID copied into a policy row.
        DECLARE @v9_modelo UNIQUEIDENTIFIER, @v9_fingerprint BINARY(32),
                @v9_atual BINARY(32), @v9_dossie BINARY(32),
                @v9_politica NVARCHAR(120);
        SELECT TOP(1) @v9_modelo=p.modelo_id,
                      @v9_fingerprint=p.modelo_fingerprint,
                      @v9_dossie=a.dossie_sha256,
                      @v9_politica=p.politica_versao
        FROM #dt10_leaders l
        JOIN identidade.v9_dt10_lower_policy p WITH(HOLDLOCK)
          ON p.politica_versao=l.politica_publicacao_versao
        JOIN auditoria.modelo_linkage_aprovacao a WITH(HOLDLOCK)
          ON a.aprovacao_id=p.aprovacao_id
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND l.raw_motivo=N'LIMIAR_INFERIOR_V9_GOVERNADO';
        IF EXISTS(SELECT 1 FROM #dt10_leaders l
                  WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
                    AND l.raw_motivo=N'LIMIAR_INFERIOR_V9_GOVERNADO'
                    AND (l.modelo_id<>@v9_modelo OR l.politica_publicacao_versao<>@v9_politica))
            THROW 51832, 'DT-10 V9: run mistura politicas ou modelos inferiores.', 1;
        EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
             @modelo_id=@v9_modelo,@fingerprint=@v9_atual OUTPUT;
        IF @v9_atual IS NULL OR @v9_atual<>@v9_fingerprint
            THROW 51831, 'DT-10 V9: fingerprint do modelo mudou apos aprovacao.', 1;
        -- sp_assert_dossie_decisao_modelo_linkage is a PRE-ACTIVATION
        -- guard: it requires the old active model to remain unchanged.
        -- Recalling it after ACTIVATE would reject every legitimate run.
        -- Verify the approved dossier's immutable fingerprint, completeness
        -- and expiry against the now-active V9 model instead.
        IF NOT EXISTS(
            SELECT 1
            FROM auditoria.modelo_linkage_dossie_decisao d WITH(HOLDLOCK)
            WHERE d.modelo_id=@v9_modelo AND d.dossie_sha256=@v9_dossie
              AND d.modelo_snapshot_sha256=@v9_fingerprint
              AND d.estado=N'COMPLETO' AND d.valido_ate>SYSDATETIMEOFFSET()
              AND d.modelo_linkage_dossie_decisao_id=(
                  SELECT MAX(d2.modelo_linkage_dossie_decisao_id)
                  FROM auditoria.modelo_linkage_dossie_decisao d2 WITH(HOLDLOCK)
                  WHERE d2.modelo_id=@v9_modelo AND d2.dossie_sha256=@v9_dossie))
            THROW 51833, 'DT-10 V9: dossie DT-15 aprovado ausente, expirado ou divergente.', 1;
    END;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND (l.raw_status<>N'NAO_RESOLVIDO' OR l.raw_motivo IS NULL
               OR (l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'
                   AND l.raw_motivo<>N'LIMIAR_INFERIOR_V9_GOVERNADO')))
        THROW 51810, 'DT-10: NOVA_IDENTIDADE exige sem candidato ou V9 governado.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND (l.universo_referencia IS NULL OR LTRIM(RTRIM(l.universo_referencia))=N''))
        THROW 51811, 'DT-10: referência de universo completo ausente.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND l.current_state=N'REFERENCIA' AND l.current_uuid<>l.initial_uuid)
        THROW 51812, 'DT-10: referência estabelecida requer recomposição governada.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND l.current_state<>N'REFERENCIA' AND l.ultimo_destino_externo_uuid IS NOT NULL)
        THROW 51813, 'DT-10: associação externa histórica exige recomposição.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'INDEFINIDA'
          AND (l.pessoa_uuid_publicado IS NOT NULL
               OR l.status_publicacao NOT IN(N'NAO_RESOLVIDO',N'CONFLITO')))
        THROW 51814, 'DT-10: indefinição não pode escolher destino.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'INDEFINIDA'
          AND l.current_state=N'REFERENCIA')
        THROW 51815, 'DT-10: indefinição não pode apagar referência estabelecida.', 1;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND
              (l.resultado_publicacao IS NULL OR
               l.resultado_publicacao NOT IN(
                   N'ASSOCIACAO_EXISTENTE',N'NOVA_IDENTIDADE',N'INDEFINIDA')))
        THROW 51816, 'DT-10: resultado de publicação desconhecido.', 1;

    -- Composição e GOLD usam o applock por UUID; adquirir uma vez por referência
    -- DISTINTA em ordem estável. O ledger e os updates abaixo são integralmente
    -- set-based; não há cursor/procedure escalar por pessoa_origem.
    SELECT DISTINCT pessoa_uuid_publicado AS target_uuid
    INTO #dt10_references
    FROM #dt10_leaders
    WHERE needs_event=1 AND pessoa_uuid_publicado IS NOT NULL;

    DECLARE @last_uuid UNIQUEIDENTIFIER=NULL, @next_uuid UNIQUEIDENTIFIER,
            @reference_lock INT, @resource NVARCHAR(255);
    WHILE 1=1
    BEGIN
        SELECT TOP(1) @next_uuid=target_uuid
        FROM #dt10_references
        WHERE @last_uuid IS NULL OR target_uuid>@last_uuid
        ORDER BY target_uuid;
        IF @@ROWCOUNT=0 BREAK;

        SET @resource=N'JORNADA:COMPOSICAO:REF:'+LOWER(CONVERT(NVARCHAR(36),@next_uuid));
        EXEC @reference_lock=sys.sp_getapplock
            @Resource=@resource,@LockMode=N'Exclusive',
            @LockOwner=N'Transaction',@LockTimeout=30000;
        IF @reference_lock<0
            THROW 51817, 'DT-10: falha no lock da referência para composição.', 1;
        SET @last_uuid=@next_uuid;
    END;

    DECLARE @now DATETIMEOFFSET(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');

    INSERT identidade.pessoa_origem_progressiva_evento(
        evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,
        expected_version,resultado,target_uuid,evidencia_referencia,
        politica_versao,modelo_versao,universo_referencia,completo,
        ocorrido_em,linkage_run_id)
    SELECT NEWID(),l.pessoa_origem_id,l.current_version+1,'RESOLUCAO',
           CASE WHEN l.resultado_publicacao=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
           CASE WHEN l.resultado_publicacao=N'INDEFINIDA' THEN NULL ELSE l.pessoa_uuid_publicado END,
           l.current_version,l.resultado_publicacao,
           CASE WHEN l.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
                  THEN l.pessoa_uuid_publicado ELSE NULL END,
           CONCAT(N'LINKAGE_RUN:',CONVERT(NVARCHAR(36),@linkage_run_id),
                  N':OBS:',CONVERT(NVARCHAR(30),l.leader_observacao_id)),
           l.politica_publicacao_versao,
           CONCAT(N'LINKAGE:',CONVERT(NVARCHAR(36),l.modelo_id),
                  N':V',CONVERT(NVARCHAR(12),l.modelo_versao)),
           l.universo_referencia,1,@now,@linkage_run_id
    FROM #dt10_leaders l
    WHERE l.needs_event=1;

    UPDATE p
       SET canonical_uuid=CASE WHEN l.resultado_publicacao=N'INDEFINIDA'
                               THEN NULL ELSE l.pessoa_uuid_publicado END,
           estado=CASE WHEN l.resultado_publicacao=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
           versao=l.current_version+1,
           ultima_resolucao_em=@now,
           ultimo_destino_externo_uuid=CASE
             WHEN l.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
                   AND l.pessoa_uuid_publicado<>l.initial_uuid THEN l.pessoa_uuid_publicado
             WHEN l.resultado_publicacao=N'NOVA_IDENTIDADE' THEN NULL
             ELSE p.ultimo_destino_externo_uuid END,
           atualizado_em=@now
    FROM identidade.pessoa_origem_progressiva p
    JOIN #dt10_leaders l ON l.pessoa_origem_id=p.pessoa_origem_id
    WHERE l.needs_event=1;

    -- A observação líder conserva sua decisão derivada do score. As demais
    -- observações da origem recebem somente a referência efetivamente publicada.
    UPDATE r
       SET progressiva_versao=COALESCE(e.versao,p.versao),
           resultado_publicacao=CASE
             WHEN r.pessoa_observacao_id=l.leader_observacao_id THEN r.resultado_publicacao
             WHEN p.estado=N'REFERENCIA' THEN N'ASSOCIACAO_EXISTENTE' ELSE N'INDEFINIDA' END,
           pessoa_uuid_publicado=CASE
             WHEN r.pessoa_observacao_id=l.leader_observacao_id THEN r.pessoa_uuid_publicado
             WHEN p.estado=N'REFERENCIA' THEN p.canonical_uuid ELSE NULL END,
           status_publicacao=CASE
             WHEN r.pessoa_observacao_id=l.leader_observacao_id THEN r.status_publicacao
             WHEN p.estado=N'REFERENCIA' THEN N'RESOLVIDO'
             WHEN r.status=N'CONFLITO' THEN N'CONFLITO' ELSE N'NAO_RESOLVIDO' END,
           motivo_publicacao=CASE
             WHEN r.pessoa_observacao_id=l.leader_observacao_id THEN r.motivo_publicacao
             WHEN p.estado=N'REFERENCIA' THEN N'REFERENCIA_PROGRESSIVA_PROPAGADA_NA_ORIGEM'
             ELSE N'INDEFINICAO_PROGRESSIVA_PROPAGADA_NA_ORIGEM' END
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po
      ON po.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN #dt10_leaders l ON l.pessoa_origem_id=po.pessoa_origem_id
    JOIN identidade.pessoa_origem_progressiva p
      ON p.pessoa_origem_id=l.pessoa_origem_id
    LEFT JOIN identidade.pessoa_origem_progressiva_evento e
      ON e.pessoa_origem_id=l.pessoa_origem_id AND e.linkage_run_id=@linkage_run_id
    WHERE r.linkage_run_id=@linkage_run_id;

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        JOIN identidade.linkage_resultado r
          ON r.linkage_run_id=@linkage_run_id
         AND r.pessoa_observacao_id=l.leader_observacao_id
        WHERE r.progressiva_versao IS NULL)
        THROW 51821, 'DT-10: origem sem versão progressiva após publicação em lote.', 1;
END;
GO

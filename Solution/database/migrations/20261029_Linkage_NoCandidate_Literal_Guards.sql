-- 2026-10-29: additive hardening of the two installed DT-10 publication procedures.
-- No historical migrations are modified. Underscores in SEM_CANDIDATO_ are
-- literal, not SQL LIKE wildcards. Any persisted best, second, or resolved
-- candidate contradicts a no-candidate promotion and fails closed (51835).
-- Both definitions are copied from the installed 20260919/20260926 contracts,
-- with only those guards added. On integration of #1199, install its later
-- governed DT-10 migration AFTER this migration so V9 authorization is preserved.
-- This migration never activates V9 or a new publication policy.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @pessoa_observacao_id BIGINT,
 @versao_resultado BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 51801,'Publicação progressiva de Linkage exige transação explícita.',1;

 DECLARE @pessoa_origem_id BIGINT,
         @resultado NVARCHAR(30),
         @canonical_uuid UNIQUEIDENTIFIER,
         @status_publicacao NVARCHAR(30),
         @motivo_publicacao NVARCHAR(160),
         @politica NVARCHAR(120),
         @universo NVARCHAR(255),
         @raw_status NVARCHAR(30),
         @raw_motivo NVARCHAR(120),
         @modelo_id UNIQUEIDENTIFIER,
         @modelo_versao INT,
         @run_status NVARCHAR(30),
         @run_avaliados BIGINT,
         @run_elegiveis BIGINT,
         @run_itens BIGINT;

 SELECT @pessoa_origem_id=po.pessoa_origem_id,
        @resultado=r.resultado_publicacao,
        @canonical_uuid=r.pessoa_uuid_publicado,
        @status_publicacao=r.status_publicacao,
        @motivo_publicacao=r.motivo_publicacao,
        @politica=r.politica_publicacao_versao,
        @universo=r.universo_referencia,
        @raw_status=r.status,
        @raw_motivo=r.motivo,
        @modelo_id=r.modelo_id,
        @modelo_versao=r.modelo_versao,
        @run_status=lr.status,
        @run_avaliados=lr.avaliados,
        @run_elegiveis=lr.registros_elegiveis
 FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
 JOIN identidade.linkage_run lr WITH(UPDLOCK,HOLDLOCK) ON lr.linkage_run_id=r.linkage_run_id
 JOIN silver.pessoa_observacao po WITH(HOLDLOCK) ON po.pessoa_observacao_id=r.pessoa_observacao_id
 WHERE r.linkage_run_id=@linkage_run_id
   AND r.pessoa_observacao_id=@pessoa_observacao_id;

 SELECT @run_itens=COUNT_BIG(*)
 FROM identidade.linkage_run_item WITH(HOLDLOCK)
 WHERE linkage_run_id=@linkage_run_id;

 IF @resultado IS NULL THROW 51802,'Resultado de publicação do Linkage ausente.',1;
 IF @run_status<>N'EXECUTANDO' THROW 51803,'Evento progressivo só pode ser produzido antes da transição do run para PUBLICADO.',1;
 IF @run_avaliados<>@run_elegiveis OR @run_itens<>@run_elegiveis
    THROW 51818,'Run incompleto/materialização divergente não pode publicar resolução progressiva.',1;
 IF NOT EXISTS(
   SELECT 1 FROM identidade.linkage_run_item WITH(HOLDLOCK)
   WHERE linkage_run_id=@linkage_run_id AND pessoa_observacao_id=@pessoa_observacao_id)
    THROW 51820,'Observação não pertence ao universo materializado do run.',1;
 IF @pessoa_origem_id IS NULL THROW 51804,'Observação sem origem persistente não possui ledger progressivo.',1;
 IF @politica IS NULL OR LTRIM(RTRIM(@politica))=N'' THROW 51805,'Política de publicação ausente.',1;
 IF EXISTS(
   SELECT 1
   FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
   WHERE vf.pessoa_observacao_id=@pessoa_observacao_id
     AND vf.ativo=1
     AND vf.metodo_resolucao IN(
       N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
       N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'))
   THROW 51819,'Vínculo determinístico/governado tem precedência sobre publicação probabilística.',1;

 DECLARE @ensure TABLE(
   initial_uuid UNIQUEIDENTIFIER NOT NULL,
   legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
   estado VARCHAR(20) NOT NULL,
   versao BIGINT NOT NULL);
 INSERT @ensure(initial_uuid,legacy_pessoa_uuid,estado,versao)
 EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;

 IF EXISTS(
   SELECT 1 FROM identidade.pessoa_origem_progressiva_evento WITH(HOLDLOCK)
   WHERE pessoa_origem_id=@pessoa_origem_id AND linkage_run_id=@linkage_run_id)
 BEGIN
   SELECT @versao_resultado=versao
   FROM identidade.pessoa_origem_progressiva_evento
   WHERE pessoa_origem_id=@pessoa_origem_id AND linkage_run_id=@linkage_run_id;
   RETURN;
 END;

 DECLARE @initial UNIQUEIDENTIFIER,@current UNIQUEIDENTIFIER,@estado VARCHAR(20),
         @versao BIGINT,@externo UNIQUEIDENTIFIER,@nova BIGINT,@now DATETIMEOFFSET(7),
         @reference_lock INT,@reference_resource NVARCHAR(255);

 SELECT @initial=initial_uuid,@current=canonical_uuid,@estado=estado,@versao=versao,
        @externo=ultimo_destino_externo_uuid
 FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK)
 WHERE pessoa_origem_id=@pessoa_origem_id;

 IF @resultado=N'ASSOCIACAO_EXISTENTE'
 BEGIN
   IF @status_publicacao<>N'RESOLVIDO' OR @canonical_uuid IS NULL
      THROW 51806,'Associação publicada exige UUID e status RESOLVIDO.',1;
   IF @estado=N'REFERENCIA' AND @current=@canonical_uuid
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
   IF @estado=N'REFERENCIA' AND (@current<>@canonical_uuid OR @current IS NULL)
      THROW 51807,'Linkage não pode trocar referência progressiva já estabelecida.',1;

   IF NOT EXISTS(SELECT 1 FROM identidade.cpf_ancora WITH(HOLDLOCK) WHERE pessoa_uuid=@canonical_uuid)
      AND NOT EXISTS(
        SELECT 1 FROM identidade.pessoa_origem_progressiva WITH(HOLDLOCK)
        WHERE pessoa_origem_id<>@pessoa_origem_id AND estado=N'REFERENCIA' AND canonical_uuid=@canonical_uuid)
      AND NOT EXISTS(
        SELECT 1 FROM identidade.vinculo_fonte WITH(HOLDLOCK)
        WHERE ativo=1 AND status=N'RESOLVIDO' AND pessoa_uuid=@canonical_uuid
          AND metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA'))
      THROW 51808,'Destino probabilístico não é referência canônica estabelecida.',1;
 END
 ELSE IF @resultado=N'NOVA_IDENTIDADE'
 BEGIN
   IF @status_publicacao<>N'RESOLVIDO' OR @canonical_uuid IS NULL OR @canonical_uuid<>@initial
      THROW 51809,'NOVA_IDENTIDADE só pode promover o initial_uuid da própria origem.',1;
   IF @raw_status<>N'NAO_RESOLVIDO' OR @raw_motivo IS NULL OR @raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'
      THROW 51810,'NOVA_IDENTIDADE exige resultado bruto sem candidato após busca completa.',1;
   -- Fail closed if a purported no-candidate result contains physical candidates.
   -- This must be checked before the first progressiva ledger event.
   IF EXISTS(
      SELECT 1 FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
      WHERE r.linkage_run_id=@linkage_run_id
        AND r.pessoa_observacao_id=@pessoa_observacao_id
        AND (r.melhor_candidato_uuid IS NOT NULL
             OR r.segundo_candidato_uuid IS NOT NULL
             OR r.pessoa_uuid_resolvido IS NOT NULL))
      THROW 51835,'NOVA_IDENTIDADE por SEM_CANDIDATO contradiz candidatos persistidos.',1;
   IF @universo IS NULL OR LTRIM(RTRIM(@universo))=N''
      THROW 51811,'NOVA_IDENTIDADE exige referência do universo completo.',1;
   IF @estado=N'REFERENCIA' AND @current=@initial
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
   IF @estado=N'REFERENCIA'
      THROW 51812,'Referência existente exige recomposição explícita antes de NOVA_IDENTIDADE.',1;
   IF @externo IS NOT NULL
      THROW 51813,'Associação externa histórica exige recomposição explícita antes de retornar ao initial_uuid.',1;
 END
 ELSE IF @resultado=N'INDEFINIDA'
 BEGIN
   IF @canonical_uuid IS NOT NULL OR @status_publicacao NOT IN(N'NAO_RESOLVIDO',N'CONFLITO')
      THROW 51814,'INDEFINIDA não pode escolher destino.',1;
   IF @estado=N'REFERENCIA'
      THROW 51815,'Publicação indefinida não pode apagar referência progressiva existente.',1;
   IF @estado=N'INDEFINIDA'
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
 END
 ELSE
   THROW 51816,'Resultado de publicação progressiva desconhecido.',1;

 IF @canonical_uuid IS NOT NULL
 BEGIN
   SET @reference_resource=N'JORNADA:COMPOSICAO:REF:'+LOWER(CONVERT(NVARCHAR(36),@canonical_uuid));
   EXEC @reference_lock=sys.sp_getapplock
     @Resource=@reference_resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
   IF @reference_lock<0 THROW 51817,'Não foi possível serializar referência progressiva com composição.',1;
 END;

 SET @nova=@versao+1;
 SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');

 INSERT identidade.pessoa_origem_progressiva_evento(
   evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
   evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em,linkage_run_id)
 VALUES(
   NEWID(),@pessoa_origem_id,@nova,'RESOLUCAO',
   CASE WHEN @resultado=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
   CASE WHEN @resultado=N'INDEFINIDA' THEN NULL ELSE @canonical_uuid END,
   @versao,@resultado,
   CASE WHEN @resultado=N'ASSOCIACAO_EXISTENTE' THEN @canonical_uuid ELSE NULL END,
   CONCAT(N'LINKAGE_RUN:',CONVERT(NVARCHAR(36),@linkage_run_id),N':OBS:',CONVERT(NVARCHAR(30),@pessoa_observacao_id)),
   @politica,
   CONCAT(N'LINKAGE:',CONVERT(NVARCHAR(36),@modelo_id),N':V',CONVERT(NVARCHAR(12),@modelo_versao)),
   @universo,1,@now,@linkage_run_id);

 UPDATE identidade.pessoa_origem_progressiva
 SET canonical_uuid=CASE WHEN @resultado=N'INDEFINIDA' THEN NULL ELSE @canonical_uuid END,
     estado=CASE WHEN @resultado=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
     versao=@nova,
     ultima_resolucao_em=@now,
     ultimo_destino_externo_uuid=CASE
       WHEN @resultado=N'ASSOCIACAO_EXISTENTE' AND @canonical_uuid<>@initial THEN @canonical_uuid
       WHEN @resultado=N'NOVA_IDENTIDADE' THEN NULL
       ELSE ultimo_destino_externo_uuid END,
     atualizado_em=@now
 WHERE pessoa_origem_id=@pessoa_origem_id;

 SET @versao_resultado=@nova;
END;
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

    IF EXISTS(
        SELECT 1 FROM #dt10_leaders l
        WHERE l.replay_version IS NULL AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND (l.raw_status<>N'NAO_RESOLVIDO' OR l.raw_motivo IS NULL
               OR l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'))
        THROW 51810, 'DT-10: NOVA_IDENTIDADE exige busca completa sem candidato.', 1;
    -- Complete the legacy batch guard before writing ANY event for this run;
    -- this is the same leader-observation invariant as governed DT-10 #1199.
    IF EXISTS(
        SELECT 1
        FROM #dt10_leaders l
        JOIN identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
          ON r.linkage_run_id=@linkage_run_id
         AND r.pessoa_observacao_id=l.leader_observacao_id
        WHERE l.replay_version IS NULL
          AND l.resultado_publicacao=N'NOVA_IDENTIDADE'
          AND l.raw_motivo LIKE N'SEM[_]CANDIDATO[_]%'
          AND (r.melhor_candidato_uuid IS NOT NULL
               OR r.segundo_candidato_uuid IS NOT NULL
               OR r.pessoa_uuid_resolvido IS NOT NULL))
        THROW 51835, 'DT-10: SEM_CANDIDATO contradiz candidatos persistidos.', 1;

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

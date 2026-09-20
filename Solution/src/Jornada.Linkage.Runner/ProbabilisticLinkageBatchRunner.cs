using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Executa o score probabilístico em lotes e publica o run de forma logicamente atômica.
/// Resultados físicos são gravados append-only durante a execução, mas somente runs com
/// status PUBLICADO participam de identidade.v_vinculo_corrente. Assim, uma execução de
/// milhões de Pessoas não mantém uma transação SQL aberta por horas. Na Fase 1, o Runner
/// obtém janela exclusiva do corpus integrado durante o run inteiro; a API/Bronze permanece disponível
/// e o Processor acumula backlog sem materializar Silver/Gold enquanto o linkage está ativo.
/// </summary>
public sealed class ProbabilisticLinkageBatchRunner(
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    IProbabilisticIdentityLinkage linkage,
    SqlPipelineCoordinator pipelineCoordinator,
    ILogger<ProbabilisticLinkageBatchRunner> logger) : IProbabilisticLinkageBatchRunner
{
    public async Task<ProbabilisticLinkageRunSummary> RunAsync(
        ProbabilisticLinkageRunRequest request,
        CancellationToken ct)
    {
        ValidateRequest(request);
        var drainTimeoutSeconds = Math.Max(30,
            configuration.GetValue("PipelineCoordination:CurrentBatchDrainTimeoutSeconds", 900));
        await using var pipelineLease = await pipelineCoordinator.AcquireExclusiveJobAsync(
            "Jornada.Linkage.Runner",
            TimeSpan.FromSeconds(drainTimeoutSeconds),
            ct);
        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(ct, pipelineLease.LostToken);
        var workCt = workCts.Token;

        var started = DateTimeOffset.UtcNow;
        var model = request.ModelVersion is int version
            ? await linkage.GetModelByVersionAsync(version, workCt)
            : await linkage.GetActiveModelAsync(workCt);

        var runId = Guid.NewGuid();
        long eligible = 0;
        long evaluated = 0;
        long resolved = 0;
        long unresolved = 0;
        long conflicts = 0;
        long noCandidateInBlock = 0;
        long afterObservationId = 0;

        try
        {
            var universe = await CreateRunAndMaterializeUniverseAsync(runId, model, request, started, workCt);
            eligible = universe.Eligible;

            while (evaluated < eligible)
            {
                workCt.ThrowIfCancellationRequested();
                var remaining = eligible - evaluated;
                var take = (int)Math.Min(request.BatchSize, remaining);
                var batch = await ReadBatchAsync(
                    runId, afterObservationId, take, workCt);

                if (batch.Count == 0)
                    break;

                var decisions = await ScoreBatchAsync(batch, model, request.MaxParallelism, workCt);
                var batchResolved = decisions.LongCount(x => x.Decision.Status == ResolutionStatus.RESOLVIDO);
                var batchConflicts = decisions.LongCount(x => x.Decision.Status == ResolutionStatus.CONFLITO);
                var batchUnresolved = decisions.Count - batchResolved - batchConflicts;
                var batchNoCandidate = decisions.LongCount(x =>
                    x.Decision.Status == ResolutionStatus.NAO_RESOLVIDO &&
                    IsNoCandidateReason(x.Decision.Motivo));
                var batchLastId = batch[batch.Count - 1].PessoaObservacaoId;

                await PersistBatchAsync(
                    runId, model, decisions,
                    batchResolved, batchUnresolved, batchConflicts, batchNoCandidate, batchLastId, workCt);

                evaluated += decisions.Count;
                resolved += batchResolved;
                unresolved += batchUnresolved;
                conflicts += batchConflicts;
                noCandidateInBlock += batchNoCandidate;
                afterObservationId = batchLastId;

                logger.LogInformation(
                    "Linkage run {RunId}: {Evaluated}/{Eligible} avaliados; modelo v{ModelVersion}.",
                    runId, evaluated, eligible, model.Version);
            }

            // O universo é uma lista imutável de IDs em linkage_run_item. Se algum item não produzir
            // exatamente um resultado, não publicamos silenciosamente uma execução incompleta.
            if (evaluated != eligible)
                throw new InvalidOperationException(
                    $"Execução incompleta: elegíveis={eligible}, avaliados={evaluated}. O run não será publicado.");

            var finished = DateTimeOffset.UtcNow;
            var status = request.Publish
                ? await PublishAsync(runId, eligible, evaluated, finished, workCt)
                : await CompleteWithoutPublicationAsync(runId, eligible, evaluated, finished, workCt);

            var publishedAt = status == LinkageRunStatus.PUBLICADO ? finished : (DateTimeOffset?)null;
            logger.LogInformation(
                "Linkage concluído. RunId={RunId}; ModeloId={ModelId}; ModeloVersao={ModelVersion}; Status={Status}; " +
                "Avaliados={Evaluated}; Resolvidos={Resolved}; NaoResolvidos={Unresolved}; Conflitos={Conflicts}; SemCandidatoBloco={NoCandidate}",
                runId, model.ModelId, model.Version, status, evaluated, resolved, unresolved, conflicts, noCandidateInBlock);

            return new ProbabilisticLinkageRunSummary(
                runId, model.ModelId, model.Version, status, eligible, evaluated,
                resolved, unresolved, conflicts, noCandidateInBlock, started, finished, publishedAt);
        }
        catch (OperationCanceledException) when (pipelineLease.IsLost)
        {
            await MarkTerminalAsync(runId, LinkageRunStatus.FALHOU, DateTimeOffset.UtcNow, "PIPELINE_COORDINATION_LOST", CancellationToken.None);
            throw new InvalidOperationException(
                $"Sessão coordenadora do pipeline foi perdida durante o linkage (SPID={pipelineLease.SessionId}); run abortado fail-closed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkTerminalAsync(runId, LinkageRunStatus.CANCELADO, DateTimeOffset.UtcNow, "CANCELADO", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await MarkTerminalAsync(
                runId, LinkageRunStatus.FALHOU, DateTimeOffset.UtcNow,
                ex.Message.Length <= 200 ? ex.Message : ex.Message[..200], CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<ScoredRow>> ScoreBatchAsync(
        IReadOnlyList<PendingRow> batch,
        ProbabilisticLinkageModelRef model,
        int maxParallelism,
        CancellationToken ct)
    {
        var bag = new ConcurrentBag<ScoredRow>();
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct },
            async (row, token) =>
            {
                var decision = await linkage.ResolveWithoutCpfAsync(row.Observation, model.ModelId, token);
                if (decision.ModeloId != model.ModelId)
                    throw new InvalidOperationException("Uma execução de linkage não pode misturar versões de modelo.");
                bag.Add(new ScoredRow(row.PessoaObservacaoId, decision));
            });

        return bag.OrderBy(x => x.PessoaObservacaoId).ToArray();
    }

    private async Task<MaterializedRunUniverse> CreateRunAndMaterializeUniverseAsync(
        Guid runId,
        ProbabilisticLinkageModelRef model,
        ProbabilisticLinkageRunRequest request,
        DateTimeOffset started,
        CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);

        var materializeCommandTimeoutSeconds = Math.Max(
            30,
            configuration.GetValue("ProbabilisticLinkage:FreezeUniverseCommandTimeoutSeconds", 900));

        // O corpus está imóvel porque o Runner segura Jornada.Pipeline.Corpus em sessão dedicada.
        // A transação abaixo é curta e serve apenas para tornar a criação do run + lista de IDs atômica.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        try
        {
            var command = new SqlCommand(
                $"""
                DECLARE @lock_result INT;
                EXEC @lock_result = sys.sp_getapplock
                    @Resource='Jornada.Linkage.Runner.Start',
                    @LockMode='Exclusive',
                    @LockOwner='Transaction',
                    @LockTimeout=60000;
                IF @lock_result < 0
                    THROW 51100, 'Não foi possível obter lock para iniciar o linkage.', 1;

                IF EXISTS (SELECT 1 FROM identidade.linkage_run
                           WHERE status IN('PREPARANDO','EXECUTANDO'))
                    THROW 51101, 'Já existe um linkage_run em execução.', 1;

                DECLARE @high_watermark BIGINT = (SELECT ISNULL(MAX(pessoa_observacao_id),0) FROM silver.pessoa_observacao);

                INSERT identidade.linkage_run(
                    linkage_run_id,modelo_id,modelo_versao,tipo_run,status,
                    pessoa_observacao_id_filtro,gestor_codigo_filtro,desde_filtro,
                    limite_solicitado,escopo_json,batch_size,max_parallelism,
                    pessoa_observacao_id_high_watermark,registros_elegiveis,
                    avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,
                    solicitado_por,motivo,correlation_id,iniciado_em)
                VALUES(
                    @run_id,@modelo_id,@modelo_versao,@tipo_run,'PREPARANDO',
                    @obs,@gestor,@desde,@limite,@escopo,@batch_size,@parallelism,
                    @high_watermark,0,0,0,0,0,0,@solicitado_por,@motivo,@correlation_id,@inicio);

                DECLARE @top_limit BIGINT = COALESCE(@limite,2147483647);
                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                SELECT @run_id,x.pessoa_observacao_id
                FROM (
                    SELECT TOP (@top_limit) po.pessoa_observacao_id
                    {EligibleFromWhereSql()}
                    ORDER BY po.pessoa_observacao_id
                ) x;

                DECLARE @elegiveis BIGINT = (SELECT COUNT_BIG(*) FROM identidade.linkage_run_item WHERE linkage_run_id=@run_id);
                UPDATE identidade.linkage_run
                SET status='EXECUTANDO', registros_elegiveis=@elegiveis
                WHERE linkage_run_id=@run_id AND status='PREPARANDO';

                SELECT @high_watermark,@elegiveis;
                """,
                connection, transaction)
            {
                CommandTimeout = materializeCommandTimeoutSeconds
            };
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = model.ModelId;
            command.Parameters.Add("@modelo_versao", SqlDbType.Int).Value = model.Version;
            command.Parameters.Add("@tipo_run", SqlDbType.NVarChar, 30).Value = request.Mode.ToString();
            command.Parameters.Add("@obs", SqlDbType.BigInt).Value = (object?)request.PessoaObservacaoId ?? DBNull.Value;
            command.Parameters.Add("@gestor", SqlDbType.NVarChar, 30).Value = (object?)request.GestorCodigo ?? DBNull.Value;
            command.Parameters.Add("@desde", SqlDbType.DateTimeOffset).Value = (object?)request.Since ?? DBNull.Value;
            command.Parameters.Add("@limite", SqlDbType.BigInt).Value = (object?)request.MaxRecords ?? DBNull.Value;
            command.Parameters.Add("@escopo", SqlDbType.NVarChar, -1).Value = BuildScopeJson(request, model);
            command.Parameters.Add("@batch_size", SqlDbType.Int).Value = request.BatchSize;
            command.Parameters.Add("@parallelism", SqlDbType.Int).Value = request.MaxParallelism;
            command.Parameters.Add("@solicitado_por", SqlDbType.NVarChar, 120).Value = (object?)request.RequestedBy ?? DBNull.Value;
            command.Parameters.Add("@motivo", SqlDbType.NVarChar, 400).Value = (object?)request.Reason ?? DBNull.Value;
            command.Parameters.Add("@correlation_id", SqlDbType.UniqueIdentifier).Value = request.CorrelationId;
            command.Parameters.Add("@inicio", SqlDbType.DateTimeOffset).Value = started;
            command.Parameters.Add("@pessoa_observacao_id", SqlDbType.BigInt).Value = (object?)request.PessoaObservacaoId ?? DBNull.Value;
            command.Parameters.Add("@gestor_codigo", SqlDbType.NVarChar, 30).Value = (object?)request.GestorCodigo ?? DBNull.Value;
            command.Parameters.Add("@mode", SqlDbType.NVarChar, 30).Value = request.Mode.ToString();

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Falha ao materializar universo do linkage_run.");
            var materialized = new MaterializedRunUniverse(reader.GetInt64(0), reader.GetInt64(1));
            await reader.CloseAsync();
            await transaction.CommitAsync(ct);
            return materialized;
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(
                ex,
                "Materialização do universo excedeu {TimeoutSeconds}s; o run não iniciou e pode ser reagendado.",
                materializeCommandTimeoutSeconds);
            throw new TimeoutException(
                $"O comando de materialização do universo excedeu {materializeCommandTimeoutSeconds}s. " +
                "O run não iniciou; verifique carga/índices e calibre o timeout homologado antes de reagendar.",
                ex);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<PendingRow>> ReadBatchAsync(
        Guid runId,
        long afterObservationId,
        int take,
        CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var eligibleAttributes = PersonResolutionContractCatalog.EligibleTransversal
            .Select(static field => field.Code)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();
        var eligibleParameters = eligibleAttributes
            .Select((_, index) => $"@eligible_attr_{index}")
            .ToArray();
        if (eligibleParameters.Length == 0)
            throw new InvalidOperationException("O contrato de resolução não possui atributos transversais elegíveis para o runtime.");

        var command = new SqlCommand(
            $"""
            SELECT TOP (@take)
                po.pessoa_observacao_id, po.cpf, po.cpf_ausente_motivo,
                po.nome_completo, po.data_nascimento, po.nome_mae,
                (
                    SELECT pa.atributo_codigo AS [AttributeCode], pa.valor AS [Value]
                    FROM silver.pessoa_atributo_observacao pa
                    WHERE pa.pessoa_observacao_id=po.pessoa_observacao_id
                      AND pa.atributo_codigo IN ({string.Join(",", eligibleParameters)})
                    ORDER BY pa.atributo_codigo,pa.atributo_instancia_chave,pa.pessoa_atributo_observacao_id
                    FOR JSON PATH
                ) AS resolution_attributes_json
            FROM identidade.linkage_run_item ri
            JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ri.pessoa_observacao_id
            WHERE ri.linkage_run_id=@run_id
              AND po.pessoa_observacao_id > @after_id
              AND NOT EXISTS (
                    SELECT 1 FROM identidade.linkage_resultado x
                    WHERE x.linkage_run_id=@run_id
                      AND x.pessoa_observacao_id=po.pessoa_observacao_id)
            ORDER BY po.pessoa_observacao_id;
            """,
            connection)
        {
            CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
        };

        command.Parameters.Add("@take", SqlDbType.Int).Value = take;
        command.Parameters.Add("@after_id", SqlDbType.BigInt).Value = afterObservationId;
        command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
        for (var index = 0; index < eligibleAttributes.Length; index++)
            command.Parameters.Add(eligibleParameters[index], SqlDbType.NVarChar, 80).Value = eligibleAttributes[index];

        var result = new List<PendingRow>(take);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            var pessoaObservacaoId = reader.GetInt64(0);
            var cpf = reader.IsDBNull(1) ? null : reader.GetString(1);
            var cpfAusenteMotivo = reader.IsDBNull(2) ? null : reader.GetString(2);
            var nomeCompleto = reader.IsDBNull(3) ? null : reader.GetString(3);
            DateOnly? dataNascimento = reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4));
            var nomeMae = reader.IsDBNull(5) ? null : reader.GetString(5);
            var attributes = reader.IsDBNull(6)
                ? Array.Empty<IdentityResolutionAttributeValue>()
                : JsonSerializer.Deserialize<IdentityResolutionAttributeValue[]>(reader.GetString(6))
                    ?? Array.Empty<IdentityResolutionAttributeValue>();

            result.Add(new PendingRow(
                pessoaObservacaoId,
                new IdentityObservation(
                    cpf,
                    cpfAusenteMotivo,
                    nomeCompleto,
                    dataNascimento,
                    nomeMae,
                    attributes)));
        }
        return result;
    }

    private static string EligibleFromWhereSql() =>
        """
        FROM silver.pessoa_observacao po
        JOIN ref.gestor g ON g.gestor_id=po.gestor_id
        LEFT JOIN identidade.v_vinculo_corrente vc
          ON vc.pessoa_observacao_id=po.pessoa_observacao_id
        WHERE po.cpf IS NULL
          AND po.pessoa_observacao_id <= @high_watermark
          AND (@pessoa_observacao_id IS NULL OR po.pessoa_observacao_id=@pessoa_observacao_id)
          AND (@gestor_codigo IS NULL OR g.codigo=@gestor_codigo)
          AND (@desde IS NULL OR po.source_as_of>=@desde)
          AND (
                @mode IN('FULL','MODEL_VALIDATION')
                OR (@mode='REPLAY' AND (vc.status IN('NAO_RESOLVIDO','CONFLITO') OR vc.metodo_resolucao='PENDENTE_PROBABILISTICO'))
                OR (@mode='INCREMENTAL' AND (vc.pessoa_observacao_id IS NULL OR vc.metodo_resolucao='PENDENTE_PROBABILISTICO'))
                OR (@mode='ON_DEMAND' AND (
                    @pessoa_observacao_id IS NOT NULL
                    OR vc.pessoa_observacao_id IS NULL
                    OR vc.status IN('NAO_RESOLVIDO','CONFLITO')
                    OR vc.metodo_resolucao='PENDENTE_PROBABILISTICO'))
              )
        """;

    internal static string PublicationIntegrityGuardSql() =>
        """
        DECLARE @run_modelo_id UNIQUEIDENTIFIER,
                @run_modelo_versao INT,
                @run_elegiveis BIGINT,
                @run_high_watermark BIGINT,
                @itens BIGINT;

        SELECT @run_modelo_id=modelo_id,
               @run_modelo_versao=modelo_versao,
               @run_elegiveis=registros_elegiveis,
               @run_high_watermark=pessoa_observacao_id_high_watermark
        FROM identidade.linkage_run WITH (UPDLOCK,HOLDLOCK)
        WHERE linkage_run_id=@run_id;

        SELECT @itens=COUNT_BIG(*)
        FROM identidade.linkage_run_item WITH (HOLDLOCK)
        WHERE linkage_run_id=@run_id;

        IF @run_elegiveis IS NULL OR @run_elegiveis<>@elegiveis OR @itens<>@elegiveis
            THROW 51108, 'Universo materializado diverge do cabeçalho/execução; publicação recusada.', 1;

        IF EXISTS (
            SELECT 1
            FROM identidade.linkage_resultado r WITH (HOLDLOCK)
            WHERE r.linkage_run_id=@run_id
              AND (r.modelo_id<>@run_modelo_id OR r.modelo_versao<>@run_modelo_versao)
        )
            THROW 51109, 'Resultados misturam modelo/versão diferentes do linkage_run; publicação recusada.', 1;

        IF EXISTS (
            SELECT 1
            FROM identidade.linkage_resultado r WITH (HOLDLOCK)
            WHERE r.linkage_run_id=@run_id
              AND NOT EXISTS (
                    SELECT 1
                    FROM identidade.linkage_run_item i WITH (HOLDLOCK)
                    WHERE i.linkage_run_id=r.linkage_run_id
                      AND i.pessoa_observacao_id=r.pessoa_observacao_id)
        )
            THROW 51110, 'Resultado fora do universo materializado do linkage_run; publicação recusada.', 1;
        """;

    internal static string ConflictReviewQueueSql() =>
        """
        EXEC qualidade.sp_sincronizar_divergencias_linkage @linkage_run_id=@run_id;
        """;

    internal static string ProgressivePublicationSql() =>
        """
        DECLARE @politica_publicacao NVARCHAR(120)=N'LINKAGE_PROGRESSIVE_PUBLICATION_V1';
        DECLARE @universo_publicacao NVARCHAR(255)=CONCAT(
            N'LINKAGE_RUN:',CONVERT(NVARCHAR(36),@run_id),
            N';HW:',CONVERT(NVARCHAR(30),@run_high_watermark),
            N';ELIGIVEIS:',CONVERT(NVARCHAR(30),@run_elegiveis));

        IF EXISTS(
            SELECT 1
            FROM identidade.linkage_resultado r WITH(HOLDLOCK)
            JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
              ON po.pessoa_observacao_id=r.pessoa_observacao_id
            LEFT JOIN identidade.pessoa_origem_progressiva p WITH(HOLDLOCK)
              ON p.pessoa_origem_id=po.pessoa_origem_id
            WHERE r.linkage_run_id=@run_id
              AND po.pessoa_origem_id IS NOT NULL
              AND p.pessoa_origem_id IS NULL)
            THROW 51819, 'Origem persistente sem initial_uuid; publicação progressiva recusada.', 1;

        ;WITH contexto AS (
            SELECT r.linkage_resultado_id,
                   r.pessoa_observacao_id,
                   r.status AS raw_status,
                   r.motivo AS raw_motivo,
                   r.pessoa_uuid_resolvido AS raw_uuid,
                   po.pessoa_origem_id,
                   p.initial_uuid,
                   p.canonical_uuid,
                   p.estado AS estado_progressivo,
                   p.ultimo_destino_externo_uuid,
                   protegido.metodo_resolucao AS metodo_protegido,
                   protegido.status AS status_protegido,
                   CASE WHEN r.pessoa_uuid_resolvido IS NOT NULL AND (
                        EXISTS(SELECT 1 FROM identidade.cpf_ancora a WITH(HOLDLOCK)
                               WHERE a.pessoa_uuid=r.pessoa_uuid_resolvido)
                        OR EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva px WITH(HOLDLOCK)
                                  WHERE px.estado=N'REFERENCIA' AND px.canonical_uuid=r.pessoa_uuid_resolvido)
                        OR EXISTS(SELECT 1 FROM identidade.vinculo_fonte vx WITH(HOLDLOCK)
                                  WHERE vx.ativo=1 AND vx.status=N'RESOLVIDO'
                                    AND vx.pessoa_uuid=r.pessoa_uuid_resolvido
                                    AND vx.metodo_resolucao IN(
                                      N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA'))
                   ) THEN 1 ELSE 0 END AS destino_estabelecido
            FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
            JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
              ON po.pessoa_observacao_id=r.pessoa_observacao_id
            LEFT JOIN identidade.pessoa_origem_progressiva p WITH(HOLDLOCK)
              ON p.pessoa_origem_id=po.pessoa_origem_id
            OUTER APPLY(
                SELECT TOP(1) vf.metodo_resolucao,vf.status,vf.pessoa_uuid
                FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
                WHERE vf.pessoa_observacao_id=r.pessoa_observacao_id
                  AND vf.ativo=1
                  AND vf.metodo_resolucao IN(
                    N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                    N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
                ORDER BY vf.vinculo_id DESC
            ) protegido
            WHERE r.linkage_run_id=@run_id
        )
        UPDATE r
           SET resultado_publicacao=
               CASE
                 WHEN c.metodo_protegido IS NOT NULL THEN N'INDEFINIDA'
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL THEN N'ASSOCIACAO_EXISTENTE'
                 WHEN c.raw_status=N'RESOLVIDO' AND c.destino_estabelecido=1 THEN N'ASSOCIACAO_EXISTENTE'
                 WHEN c.raw_status=N'NAO_RESOLVIDO'
                      AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.pessoa_origem_id IS NOT NULL
                      AND c.initial_uuid IS NOT NULL
                      AND c.ultimo_destino_externo_uuid IS NULL THEN N'NOVA_IDENTIDADE'
                 ELSE N'INDEFINIDA'
               END,
               pessoa_uuid_publicado=
               CASE
                 WHEN c.metodo_protegido IS NOT NULL THEN NULL
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL THEN c.canonical_uuid
                 WHEN c.raw_status=N'RESOLVIDO' AND c.destino_estabelecido=1 THEN c.raw_uuid
                 WHEN c.raw_status=N'NAO_RESOLVIDO'
                      AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.pessoa_origem_id IS NOT NULL
                      AND c.initial_uuid IS NOT NULL
                      AND c.ultimo_destino_externo_uuid IS NULL THEN c.initial_uuid
                 ELSE NULL
               END,
               status_publicacao=
               CASE
                 WHEN c.metodo_protegido IS NOT NULL
                    THEN CASE WHEN c.status_protegido=N'CONFLITO' THEN N'CONFLITO' ELSE N'NAO_RESOLVIDO' END
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL THEN N'RESOLVIDO'
                 WHEN c.raw_status=N'RESOLVIDO' AND c.destino_estabelecido=1 THEN N'RESOLVIDO'
                 WHEN c.raw_status=N'NAO_RESOLVIDO'
                      AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.pessoa_origem_id IS NOT NULL
                      AND c.initial_uuid IS NOT NULL
                      AND c.ultimo_destino_externo_uuid IS NULL THEN N'RESOLVIDO'
                 WHEN c.raw_status=N'CONFLITO' THEN N'CONFLITO'
                 ELSE N'NAO_RESOLVIDO'
               END,
               motivo_publicacao=
               CASE
                 WHEN c.metodo_protegido IS NOT NULL THEN LEFT(CONCAT(N'PRECEDENCIA_',c.metodo_protegido),160)
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL
                      AND c.raw_status=N'RESOLVIDO' AND c.raw_uuid=c.canonical_uuid
                    THEN N'REFERENCIA_PROGRESSIVA_CONFIRMADA'
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL
                      AND c.raw_status=N'RESOLVIDO' AND c.raw_uuid<>c.canonical_uuid
                    THEN N'REFERENCIA_PROGRESSIVA_PRESERVADA_DESTINO_DIVERGENTE'
                 WHEN c.estado_progressivo=N'REFERENCIA' AND c.canonical_uuid IS NOT NULL
                    THEN N'REFERENCIA_PROGRESSIVA_PRESERVADA'
                 WHEN c.raw_status=N'RESOLVIDO' AND c.destino_estabelecido=1
                    THEN N'ASSOCIACAO_EXISTENTE_LINKAGE'
                 WHEN c.raw_status=N'RESOLVIDO'
                    THEN N'DESTINO_LINKAGE_NAO_ESTABELECIDO'
                 WHEN c.raw_status=N'NAO_RESOLVIDO' AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.pessoa_origem_id IS NULL
                    THEN N'SEM_ORIGEM_PERSISTENTE_PARA_NOVA_IDENTIDADE'
                 WHEN c.raw_status=N'NAO_RESOLVIDO' AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.ultimo_destino_externo_uuid IS NOT NULL
                    THEN N'RECOMPOSICAO_REQUERIDA_ANTES_NOVA_IDENTIDADE'
                 WHEN c.raw_status=N'NAO_RESOLVIDO' AND c.raw_motivo LIKE N'SEM_CANDIDATO_%'
                      AND c.pessoa_origem_id IS NOT NULL AND c.initial_uuid IS NOT NULL
                    THEN N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA'
                 WHEN c.raw_status=N'CONFLITO' THEN COALESCE(c.raw_motivo,N'LINKAGE_AMBIGUO')
                 ELSE COALESCE(c.raw_motivo,N'LINKAGE_INDEFINIDO')
               END,
               pessoa_origem_id_publicado=c.pessoa_origem_id,
               progressiva_versao=NULL,
               politica_publicacao_versao=@politica_publicacao,
               universo_referencia=@universo_publicacao,
               publicado_em=@fim
        FROM identidade.linkage_resultado r
        JOIN contexto c ON c.linkage_resultado_id=r.linkage_resultado_id;

        IF EXISTS(
            SELECT 1 FROM identidade.linkage_resultado
            WHERE linkage_run_id=@run_id
              AND (resultado_publicacao IS NULL OR status_publicacao IS NULL
                   OR motivo_publicacao IS NULL OR politica_publicacao_versao IS NULL OR publicado_em IS NULL))
            THROW 51820, 'Decisão operacional incompleta; publicação do linkage recusada.', 1;

        DECLARE @progressiva_origem BIGINT,@progressiva_obs BIGINT,@progressiva_versao BIGINT;
        DECLARE progressiva_linkage CURSOR LOCAL FAST_FORWARD FOR
            SELECT y.pessoa_origem_id,y.pessoa_observacao_id
            FROM (
                SELECT x.*,
                       MAX(x.protegido) OVER(PARTITION BY x.pessoa_origem_id) AS origem_protegida
                FROM (
                    SELECT po.pessoa_origem_id,r.pessoa_observacao_id,
                           CASE WHEN EXISTS(
                               SELECT 1 FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
                               WHERE vf.pessoa_observacao_id=r.pessoa_observacao_id
                                 AND vf.ativo=1
                                 AND vf.metodo_resolucao IN(
                                   N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                                   N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
                           ) THEN 1 ELSE 0 END AS protegido,
                           ROW_NUMBER() OVER(
                             PARTITION BY po.pessoa_origem_id
                             ORDER BY po.versao_interna DESC,r.pessoa_observacao_id DESC) rn
                    FROM identidade.linkage_resultado r WITH(HOLDLOCK)
                    JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
                      ON po.pessoa_observacao_id=r.pessoa_observacao_id
                    WHERE r.linkage_run_id=@run_id
                      AND po.pessoa_origem_id IS NOT NULL
                ) x
            ) y
            WHERE y.rn=1 AND y.origem_protegida=0
            ORDER BY y.pessoa_origem_id;

        OPEN progressiva_linkage;
        FETCH NEXT FROM progressiva_linkage INTO @progressiva_origem,@progressiva_obs;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @progressiva_versao=NULL;
            EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                 @linkage_run_id=@run_id,
                 @pessoa_observacao_id=@progressiva_obs,
                 @versao_resultado=@progressiva_versao OUTPUT;

            UPDATE rr
               SET progressiva_versao=@progressiva_versao,
                   resultado_publicacao=CASE
                     WHEN rr.pessoa_observacao_id=@progressiva_obs THEN rr.resultado_publicacao
                     WHEN p.estado=N'REFERENCIA' THEN N'ASSOCIACAO_EXISTENTE'
                     ELSE N'INDEFINIDA' END,
                   pessoa_uuid_publicado=CASE
                     WHEN rr.pessoa_observacao_id=@progressiva_obs THEN rr.pessoa_uuid_publicado
                     WHEN p.estado=N'REFERENCIA' THEN p.canonical_uuid
                     ELSE NULL END,
                   status_publicacao=CASE
                     WHEN rr.pessoa_observacao_id=@progressiva_obs THEN rr.status_publicacao
                     WHEN p.estado=N'REFERENCIA' THEN N'RESOLVIDO'
                     WHEN rr.status=N'CONFLITO' THEN N'CONFLITO'
                     ELSE N'NAO_RESOLVIDO' END,
                   motivo_publicacao=CASE
                     WHEN rr.pessoa_observacao_id=@progressiva_obs THEN rr.motivo_publicacao
                     WHEN p.estado=N'REFERENCIA' THEN N'REFERENCIA_PROGRESSIVA_PROPAGADA_NA_ORIGEM'
                     ELSE N'INDEFINICAO_PROGRESSIVA_PROPAGADA_NA_ORIGEM' END
            FROM identidade.linkage_resultado rr
            JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=rr.pessoa_observacao_id
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            WHERE rr.linkage_run_id=@run_id
              AND po.pessoa_origem_id=@progressiva_origem;

            FETCH NEXT FROM progressiva_linkage INTO @progressiva_origem,@progressiva_obs;
        END;
        CLOSE progressiva_linkage;
        DEALLOCATE progressiva_linkage;

        IF EXISTS(
            SELECT 1 FROM identidade.linkage_resultado r
            WHERE r.linkage_run_id=@run_id
              AND r.pessoa_origem_id_publicado IS NOT NULL
              AND NOT EXISTS(
                  SELECT 1
                  FROM identidade.linkage_resultado r2
                  JOIN silver.pessoa_observacao po2
                    ON po2.pessoa_observacao_id=r2.pessoa_observacao_id
                  JOIN identidade.vinculo_fonte vf
                    ON vf.pessoa_observacao_id=r2.pessoa_observacao_id
                   AND vf.ativo=1
                   AND vf.metodo_resolucao IN(
                     N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                     N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
                  WHERE r2.linkage_run_id=@run_id
                    AND po2.pessoa_origem_id=r.pessoa_origem_id_publicado)
              AND r.progressiva_versao IS NULL)
            THROW 51821, 'Origem persistente ficou sem versão progressiva na publicação.', 1;
        """;

    private async Task PersistBatchAsync(
        Guid runId,
        ProbabilisticLinkageModelRef model,
        IReadOnlyList<ScoredRow> decisions,
        long batchResolved,
        long batchUnresolved,
        long batchConflicts,
        long batchNoCandidate,
        long lastObservationId,
        CancellationToken ct)
    {
        if (decisions.Count == 0) return;

        await using var connection = await operationalSql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var table = BuildResultDataTable(runId, model, decisions);
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = "identidade.linkage_resultado",
                BatchSize = decisions.Count,
                BulkCopyTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
            };
            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(table, ct);

            var update = new SqlCommand(
                """
                UPDATE identidade.linkage_run
                SET avaliados=avaliados+@n,
                    resolvidos=resolvidos+@res,
                    nao_resolvidos=nao_resolvidos+@nao,
                    conflitos=conflitos+@conf,
                    sem_candidato_no_bloco=sem_candidato_no_bloco+@sem_bloco,
                    ultimo_observacao_id=@ultimo
                WHERE linkage_run_id=@run_id AND status='EXECUTANDO';
                IF @@ROWCOUNT<>1 THROW 51103, 'Run deixou de estar EXECUTANDO durante persistência do lote.', 1;
                """, connection, transaction);
            update.Parameters.Add("@n", SqlDbType.BigInt).Value = decisions.Count;
            update.Parameters.Add("@res", SqlDbType.BigInt).Value = batchResolved;
            update.Parameters.Add("@nao", SqlDbType.BigInt).Value = batchUnresolved;
            update.Parameters.Add("@conf", SqlDbType.BigInt).Value = batchConflicts;
            update.Parameters.Add("@sem_bloco", SqlDbType.BigInt).Value = batchNoCandidate;
            update.Parameters.Add("@ultimo", SqlDbType.BigInt).Value = lastObservationId;
            update.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await update.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<LinkageRunStatus> PublishAsync(
        Guid runId, long eligible, long evaluated, DateTimeOffset finished, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var command = new SqlCommand(
                $"""
                DECLARE @lock_result INT;
                EXEC @lock_result = sys.sp_getapplock
                    @Resource='Jornada.Linkage.Runner.Publish',
                    @LockMode='Exclusive',
                    @LockOwner='Transaction',
                    @LockTimeout=60000;
                IF @lock_result < 0
                    THROW 51104, 'Não foi possível obter lock para publicação do linkage.', 1;

                DECLARE @status NVARCHAR(30), @run_avaliados BIGINT, @resultados BIGINT;
                SELECT @status=status, @run_avaliados=avaliados
                FROM identidade.linkage_run WITH (UPDLOCK,HOLDLOCK)
                WHERE linkage_run_id=@run_id;

                SELECT @resultados=COUNT_BIG(*)
                FROM identidade.linkage_resultado WITH (HOLDLOCK)
                WHERE linkage_run_id=@run_id;

                IF @status IS NULL OR @status<>'EXECUTANDO'
                    THROW 51105, 'Somente run EXECUTANDO pode ser publicado.', 1;
                IF @run_avaliados<>@avaliados OR @resultados<>@avaliados OR @avaliados<>@elegiveis
                    THROW 51106, 'Contagens do run não fecham; publicação recusada.', 1;

                {PublicationIntegrityGuardSql()}

                {ProgressivePublicationSql()}

                UPDATE identidade.linkage_run
                SET status='PUBLICADO', finalizado_em=@fim, publicado_em=@fim
                WHERE linkage_run_id=@run_id;

                {ConflictReviewQueueSql()}

                -- A view corrente só passa a enxergar o run após PUBLICADO.
                -- Recompomos referência publicada e initial_uuid na mesma transação.
                DECLARE @gold_uuid UNIQUEIDENTIFIER;
                DECLARE gold_progressiva CURSOR LOCAL FAST_FORWARD FOR
                    SELECT DISTINCT pessoa_uuid
                    FROM (
                        SELECT r.pessoa_uuid_publicado pessoa_uuid
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
                    ) u
                    WHERE pessoa_uuid IS NOT NULL;

                OPEN gold_progressiva;
                FETCH NEXT FROM gold_progressiva INTO @gold_uuid;
                WHILE @@FETCH_STATUS=0
                BEGIN
                    EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@gold_uuid;
                    FETCH NEXT FROM gold_progressiva INTO @gold_uuid;
                END;
                CLOSE gold_progressiva;
                DEALLOCATE gold_progressiva;

                -- v3.45: o fato já existe independentemente da identidade. Ao publicar o linkage,
                -- sincroniza-se somente a atribuição canônica materializada, sem reescrever o
                -- sujeito declarado (origem/CPF snapshot) nem criar nova versão factual.
                ;WITH afetadas AS (
                    SELECT DISTINCT pessoa_observacao_id
                    FROM identidade.linkage_resultado
                    WHERE linkage_run_id=@run_id
                ), corrente AS (
                    SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
                    FROM afetadas a
                    LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=a.pessoa_observacao_id
                )
                UPDATE b SET
                    pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
                    estado_atribuicao_identidade=CASE WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                                                      WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                                                      ELSE 'PENDENTE_IDENTIDADE' END,
                    atualizado_em=SYSDATETIMEOFFSET()
                FROM gold.beneficio_concedido b
                JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id
                JOIN corrente c ON c.pessoa_observacao_id=ro.pessoa_observacao_id;

                ;WITH afetadas AS (
                    SELECT DISTINCT pessoa_observacao_id FROM identidade.linkage_resultado WHERE linkage_run_id=@run_id
                ), corrente AS (
                    SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
                    FROM afetadas a LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=a.pessoa_observacao_id
                )
                UPDATE s SET
                    pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
                    estado_atribuicao_identidade=CASE WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                                                      WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                                                      ELSE 'PENDENTE_IDENTIDADE' END,
                    atualizado_em=SYSDATETIMEOFFSET()
                FROM gold.servico_prestado s
                JOIN silver.registro_observacao ro ON ro.registro_observacao_id=s.registro_observacao_id
                JOIN corrente c ON c.pessoa_observacao_id=ro.pessoa_observacao_id;

                ;WITH afetadas AS (
                    SELECT DISTINCT pessoa_observacao_id FROM identidade.linkage_resultado WHERE linkage_run_id=@run_id
                ), corrente AS (
                    SELECT a.pessoa_observacao_id,vc.pessoa_uuid,vc.status
                    FROM afetadas a LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=a.pessoa_observacao_id
                )
                UPDATE ri SET
                    pessoa_uuid=CASE WHEN c.status='RESOLVIDO' THEN c.pessoa_uuid ELSE NULL END,
                    estado_atribuicao_identidade=CASE WHEN c.status='RESOLVIDO' AND c.pessoa_uuid IS NOT NULL THEN 'ATRIBUIDA'
                                                      WHEN c.status='CONFLITO' THEN 'CONFLITO_IDENTIDADE'
                                                      ELSE 'PENDENTE_IDENTIDADE' END,
                    atualizado_em=SYSDATETIMEOFFSET()
                FROM serving.registro_integrado ri
                JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id
                JOIN corrente c ON c.pessoa_observacao_id=ro.pessoa_observacao_id;
                """, connection, transaction)
            {
                CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
            };
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@avaliados", SqlDbType.BigInt).Value = evaluated;
            command.Parameters.Add("@elegiveis", SqlDbType.BigInt).Value = eligible;
            command.Parameters.Add("@fim", SqlDbType.DateTimeOffset).Value = finished;
            await command.ExecuteNonQueryAsync(ct);

            // NOVA_IDENTIDADE cria uma referência que ainda não existia no corpus.
            // Ela precisa ganhar blocking antes do commit para o próximo run poder encontrá-la.
            var newReferences = new List<Guid>();
            await using (var projected = connection.CreateCommand())
            {
                projected.Transaction = transaction;
                projected.CommandText = """
                    SELECT DISTINCT pessoa_uuid_publicado
                    FROM identidade.linkage_resultado
                    WHERE linkage_run_id=@run_id
                      AND resultado_publicacao='NOVA_IDENTIDADE'
                      AND pessoa_uuid_publicado IS NOT NULL;
                    """;
                projected.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
                await using var reader = await projected.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    newReferences.Add(reader.GetGuid(0));
            }

            foreach (var uuid in newReferences)
                await BlockingProjectionPersistence.RefreshSqlServerAsync(connection, transaction, uuid, ct);

            await transaction.CommitAsync(ct);
            return LinkageRunStatus.PUBLICADO;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<LinkageRunStatus> CompleteWithoutPublicationAsync(
        Guid runId, long eligible, long evaluated, DateTimeOffset finished, CancellationToken ct)
    {
        if (eligible != evaluated)
            throw new InvalidOperationException("Run sem publicação também exige processamento completo do universo congelado.");

        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand(
            """
            UPDATE identidade.linkage_run
            SET status='CONCLUIDO_SEM_PUBLICACAO', finalizado_em=@fim
            WHERE linkage_run_id=@run_id AND status='EXECUTANDO' AND avaliados=@avaliados;
            IF @@ROWCOUNT<>1 THROW 51107, 'Não foi possível concluir run sem publicação.', 1;
            """, connection);
        command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
        command.Parameters.Add("@avaliados", SqlDbType.BigInt).Value = evaluated;
        command.Parameters.Add("@fim", SqlDbType.DateTimeOffset).Value = finished;
        await command.ExecuteNonQueryAsync(ct);
        return LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO;
    }

    private async Task MarkTerminalAsync(
        Guid runId, LinkageRunStatus status, DateTimeOffset finished, string error, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand(
            """
            UPDATE identidade.linkage_run
            SET status=@status, finalizado_em=@fim, erro_resumo=@erro
            WHERE linkage_run_id=@run_id AND status IN('PREPARANDO','EXECUTANDO');
            """, connection);
        command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
        command.Parameters.Add("@status", SqlDbType.NVarChar, 30).Value = status.ToString();
        command.Parameters.Add("@fim", SqlDbType.DateTimeOffset).Value = finished;
        command.Parameters.Add("@erro", SqlDbType.NVarChar, 200).Value = error;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DataTable BuildResultDataTable(
        Guid runId,
        ProbabilisticLinkageModelRef model,
        IReadOnlyList<ScoredRow> decisions)
    {
        var table = new DataTable();
        table.Columns.Add("linkage_run_id", typeof(Guid));
        table.Columns.Add("modelo_id", typeof(Guid));
        table.Columns.Add("modelo_versao", typeof(int));
        table.Columns.Add("pessoa_observacao_id", typeof(long));
        table.Columns.Add("pessoa_uuid_resolvido", typeof(Guid));
        table.Columns.Add("melhor_candidato_uuid", typeof(Guid));
        table.Columns.Add("score_melhor", typeof(decimal));
        table.Columns.Add("segundo_candidato_uuid", typeof(Guid));
        table.Columns.Add("score_segundo", typeof(decimal));
        table.Columns.Add("margem", typeof(decimal));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("motivo", typeof(string));
        table.Columns.Add("calculado_em", typeof(DateTimeOffset));

        foreach (var row in decisions)
        {
            var d = row.Decision;
            table.Rows.Add(
                runId, model.ModelId, model.Version, row.PessoaObservacaoId,
                (object?)d.PessoaUuidResolvido ?? DBNull.Value,
                (object?)d.MelhorCandidatoUuid ?? DBNull.Value,
                d.MelhorScore,
                (object?)d.SegundoCandidatoUuid ?? DBNull.Value,
                (object?)d.SegundoScore ?? DBNull.Value,
                (object?)d.Margem ?? DBNull.Value,
                d.Status.ToString(),
                (object?)d.Motivo ?? DBNull.Value,
                DateTimeOffset.UtcNow);
        }
        return table;
    }

    internal static bool IsNoCandidateReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.StartsWith("SEM_CANDIDATO_", StringComparison.Ordinal);

    private static void ValidateRequest(ProbabilisticLinkageRunRequest request)
    {
        if (request.BatchSize is < 100 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(request), request.BatchSize, "BatchSize deve estar entre 100 e 100000.");
        if (request.MaxParallelism is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(request), request.MaxParallelism, "MaxParallelism deve estar entre 1 e 32.");
        if (request.MaxRecords is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.MaxRecords, "MaxRecords deve ser maior que zero quando informado.");
        if (request.Mode == LinkageRunType.MODEL_VALIDATION && request.Publish)
            throw new InvalidOperationException("MODEL_VALIDATION não pode publicar.");
    }

    internal static string BuildScopeJson(
        ProbabilisticLinkageRunRequest request,
        ProbabilisticLinkageModelRef model) =>
        JsonSerializer.Serialize(new
        {
            mode = request.Mode.ToString(),
            modelVersion = request.ModelVersion,
            pessoaObservacaoId = request.PessoaObservacaoId,
            gestorCodigo = request.GestorCodigo,
            since = request.Since,
            maxRecords = request.MaxRecords,
            publish = request.Publish,
            selectedModel = new
            {
                modelId = model.ModelId,
                version = model.Version,
                algorithmVersion = model.AlgorithmVersion
            },
            blocking = new
            {
                mode = model.BlockingContract is null ? "LEGACY" : "RULESET",
                ruleSetVersion = model.BlockingContract?.RuleSetVersion,
                ruleSetFingerprintSha256 = model.BlockingContract?.RuleSetFingerprintSha256,
                projectionSchemaVersion = model.BlockingContract?.ProjectionSchemaVersion,
                projectionFingerprintSha256 = model.BlockingContract?.ProjectionFingerprintSha256
            }
        });

    private sealed record MaterializedRunUniverse(long HighWatermarkObservationId, long Eligible);

    private sealed record PendingRow(long PessoaObservacaoId, IdentityObservation Observation);
    private sealed record ScoredRow(long PessoaObservacaoId, ProbabilisticLinkageDecision Decision);
}

using System.Collections.Concurrent;
using System.Data;
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
                    string.Equals(x.Decision.Motivo, "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO", StringComparison.Ordinal));
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
            command.Parameters.Add("@escopo", SqlDbType.NVarChar, -1).Value = BuildScopeJson(request);
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
        var command = new SqlCommand(
            """
            SELECT TOP (@take)
                po.pessoa_observacao_id, po.cpf, po.cpf_ausente_motivo,
                po.nome_completo, po.data_nascimento, po.nome_mae
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

        var result = new List<PendingRow>(take);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new PendingRow(
                reader.GetInt64(0),
                new IdentityObservation(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    DateOnly.FromDateTime(reader.GetDateTime(4)),
                    reader.GetString(5))));
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
                """
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

                IF @status<>'EXECUTANDO'
                    THROW 51105, 'Somente run EXECUTANDO pode ser publicado.', 1;
                IF @run_avaliados<>@avaliados OR @resultados<>@avaliados OR @avaliados<>@elegiveis
                    THROW 51106, 'Contagens do run não fecham; publicação recusada.', 1;

                UPDATE identidade.linkage_run
                SET status='PUBLICADO', finalizado_em=@fim, publicado_em=@fim
                WHERE linkage_run_id=@run_id;

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
                """, connection, transaction);
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@avaliados", SqlDbType.BigInt).Value = evaluated;
            command.Parameters.Add("@elegiveis", SqlDbType.BigInt).Value = eligible;
            command.Parameters.Add("@fim", SqlDbType.DateTimeOffset).Value = finished;
            await command.ExecuteNonQueryAsync(ct);
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

    private static string BuildScopeJson(ProbabilisticLinkageRunRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            mode = request.Mode.ToString(),
            modelVersion = request.ModelVersion,
            pessoaObservacaoId = request.PessoaObservacaoId,
            gestorCodigo = request.GestorCodigo,
            since = request.Since,
            maxRecords = request.MaxRecords,
            publish = request.Publish
        });

    private sealed record MaterializedRunUniverse(long HighWatermarkObservationId, long Eligible);

    private sealed record PendingRow(long PessoaObservacaoId, IdentityObservation Observation);
    private sealed record ScoredRow(long PessoaObservacaoId, ProbabilisticLinkageDecision Decision);
}

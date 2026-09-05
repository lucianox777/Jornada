using Jornada.Contracts;
using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Pipeline.Coordination;

/// <summary>
/// Serializa a materialização do corpus integrado na Fase 1 sem bloquear o recebimento HTTP/Bronze.
///
/// Protocolo:
/// - Processor: Shared em ExclusiveRequest + Exclusive em Corpus; depois libera ExclusiveRequest
///   e mantém Corpus somente durante um lote. Se houver job exclusivo pendente/ativo, não reserva lote.
/// - Parameters GENERATE_DRAFT / Linkage Runner: Exclusive em ExclusiveRequest e depois Exclusive
///   em Corpus. O primeiro lock impede novos lotes; o segundo aguarda apenas o lote corrente drenar.
///
/// Os locks têm owner Session em conexão coordenadora dedicada, sem pooling. A liberação normal é
/// explícita por sp_releaseapplock; se o processo morrer, o encerramento da sessão física libera os locks.
/// </summary>
public sealed class SqlPipelineCoordinator
{
    public const string ExclusiveRequestResource = "Jornada.Pipeline.ExclusiveRequest";
    public const string CorpusResource = "Jornada.Pipeline.Corpus";

    private readonly IOperationalSqlAdapter operationalSql;
    private readonly TimeSpan heartbeatInterval;
    private readonly TimeSpan exclusiveIntentTimeout;

    public SqlPipelineCoordinator(
        string connectionString,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? exclusiveIntentTimeout = null)
        : this(new OperationalSqlAdapter(connectionString), heartbeatInterval, exclusiveIntentTimeout)
    {
    }

    public SqlPipelineCoordinator(
        IOperationalSqlAdapter operationalSql,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? exclusiveIntentTimeout = null)
    {
        this.operationalSql = operationalSql ?? throw new ArgumentNullException(nameof(operationalSql));
        this.heartbeatInterval = heartbeatInterval is { } hb && hb > TimeSpan.Zero ? hb : TimeSpan.FromSeconds(5);
        this.exclusiveIntentTimeout = exclusiveIntentTimeout is { } it && it >= TimeSpan.Zero ? it : TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Tenta obter a janela de um único lote do Processor sem esperar. Retorna null quando um job
    /// exclusivo já declarou intenção ou quando outro materializador ainda possui o corpus.
    /// </summary>
    public async Task<PipelineCoordinationLease?> TryAcquireProcessorBatchAsync(CancellationToken cancellationToken)
    {
        var connection = operationalSql.CreateDedicatedSessionConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);

            var request = await GetAppLockAsync(
                connection, ExclusiveRequestResource, "Shared", TimeSpan.Zero, cancellationToken);
            if (request < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            var corpus = await GetAppLockAsync(
                connection, CorpusResource, "Exclusive", TimeSpan.Zero, cancellationToken);
            if (corpus < 0)
            {
                await ReleaseAppLockBestEffortAsync(connection, ExclusiveRequestResource);
                await connection.DisposeAsync();
                return null;
            }

            // O Processor já possui o corpus. Liberar o Shared de intenção permite que um job exclusivo
            // sinalize prioridade enquanto este lote termina; novos lotes deixarão de iniciar.
            var released = await ReleaseAppLockAsync(connection, ExclusiveRequestResource, cancellationToken);
            if (released < 0)
                throw new InvalidOperationException("Falha ao liberar o lock de intenção compartilhada do Processor.");

            return await PipelineCoordinationLease.CreateAsync(
                connection,
                "ProcessorBatch",
                new[] { new PipelineExpectedLock(CorpusResource, "Exclusive") },
                heartbeatInterval,
                cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Obtém janela exclusiva para job analítico. Aguarda brevemente pela intenção exclusiva para
    /// reduzir corridas entre jobs; depois de declarar intenção, espera de forma finita apenas o lote
    /// corrente do Processor terminar.
    /// </summary>
    public async Task<PipelineCoordinationLease> AcquireExclusiveJobAsync(
        string ownerName,
        TimeSpan currentBatchDrainTimeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new ArgumentException("Nome do job exclusivo é obrigatório.", nameof(ownerName));
        ArgumentOutOfRangeException.ThrowIfLessThan(currentBatchDrainTimeout, TimeSpan.Zero);

        var connection = operationalSql.CreateDedicatedSessionConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);

            var request = await GetAppLockAsync(
                connection, ExclusiveRequestResource, "Exclusive", exclusiveIntentTimeout, cancellationToken);
            if (request < 0)
                throw new PipelineCoordinationBusyException(
                    $"Outro job exclusivo do pipeline já está ativo ou aguardando. {ownerName} deve ser reagendado.");

            var corpus = await GetAppLockAsync(
                connection, CorpusResource, "Exclusive", currentBatchDrainTimeout, cancellationToken);
            if (corpus < 0)
            {
                await ReleaseAppLockBestEffortAsync(connection, ExclusiveRequestResource);
                throw new PipelineCoordinationBusyException(
                    $"O lote corrente do Processor não liberou o corpus em {currentBatchDrainTimeout.TotalSeconds:0}s. " +
                    $"{ownerName} não iniciou e deve ser reagendado após verificar o lote em execução.");
            }

            return await PipelineCoordinationLease.CreateAsync(
                connection,
                ownerName,
                new[]
                {
                    new PipelineExpectedLock(CorpusResource, "Exclusive"),
                    new PipelineExpectedLock(ExclusiveRequestResource, "Exclusive")
                },
                heartbeatInterval,
                cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> GetAppLockAsync(
        SqlConnection connection,
        string resource,
        string mode,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var timeoutMilliseconds = timeout <= TimeSpan.Zero
            ? 0
            : (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalMilliseconds));

        await using var command = new SqlCommand(
            """
            DECLARE @result INT;
            EXEC @result = sys.sp_getapplock
                @Resource=@resource,
                @LockMode=@mode,
                @LockOwner='Session',
                @LockTimeout=@timeout_ms;
            SELECT @result;
            """,
            connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(30, (timeoutMilliseconds / 1000) + 30)
        };
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = resource;
        command.Parameters.Add("@mode", SqlDbType.NVarChar, 32).Value = mode;
        command.Parameters.Add("@timeout_ms", SqlDbType.Int).Value = timeoutMilliseconds;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task<int> ReleaseAppLockAsync(
        SqlConnection connection,
        string resource,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            DECLARE @result INT;
            EXEC @result = sys.sp_releaseapplock
                @Resource=@resource,
                @LockOwner='Session';
            SELECT @result;
            """,
            connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 30
        };
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = resource;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ReleaseAppLockBestEffortAsync(SqlConnection connection, string resource)
    {
        try { await ReleaseAppLockAsync(connection, resource, CancellationToken.None); }
        catch { /* Pooling=false: Dispose da conexão física é o fallback final de liberação. */ }
    }
}

public sealed record PipelineExpectedLock(string Resource, string ExpectedMode);

public sealed class PipelineCoordinationLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly IReadOnlyList<PipelineExpectedLock> locksInReleaseOrder;
    private readonly CancellationTokenSource heartbeatStop = new();
    private readonly CancellationTokenSource lost = new();
    private readonly Task heartbeatTask;
    private int disposed;
    private int lostState;

    private PipelineCoordinationLease(
        SqlConnection connection,
        string ownerName,
        int sessionId,
        IReadOnlyList<PipelineExpectedLock> locksInReleaseOrder,
        TimeSpan heartbeatInterval)
    {
        this.connection = connection;
        OwnerName = ownerName;
        SessionId = sessionId;
        this.locksInReleaseOrder = locksInReleaseOrder;
        heartbeatTask = HeartbeatLoopAsync(heartbeatInterval, heartbeatStop.Token);
    }

    internal static async Task<PipelineCoordinationLease> CreateAsync(
        SqlConnection connection,
        string ownerName,
        IReadOnlyList<PipelineExpectedLock> locksInReleaseOrder,
        TimeSpan heartbeatInterval,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT @@SPID", connection) { CommandTimeout = 30 };
        var sessionId = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        return new PipelineCoordinationLease(connection, ownerName, sessionId, locksInReleaseOrder, heartbeatInterval);
    }

    public string OwnerName { get; }
    public int SessionId { get; }
    public CancellationToken LostToken => lost.Token;
    public bool IsLost => Volatile.Read(ref lostState) != 0;

    private async Task HeartbeatLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                foreach (var expected in locksInReleaseOrder)
                {
                    var heartbeatTimeoutSeconds = Math.Clamp(
                        (int)Math.Ceiling(interval.TotalSeconds * 2), 2, 30);
                    await using var command = new SqlCommand(
                        "SELECT @@SPID, APPLOCK_MODE('public', @resource, 'Session')", connection)
                    { CommandTimeout = heartbeatTimeoutSeconds };
                    command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = expected.Resource;
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        throw new InvalidOperationException($"Heartbeat sem resposta para {expected.Resource}; SPID esperado={SessionId}.");
                    var observedSessionId = reader.GetInt32(0);
                    var mode = reader.IsDBNull(1) ? "NoLock" : reader.GetString(1);
                    if (observedSessionId != SessionId)
                        throw new InvalidOperationException(
                            $"Sessão coordenadora mudou: SPID esperado={SessionId}, observado={observedSessionId}.");
                    if (!string.Equals(mode, expected.ExpectedMode, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Lock {expected.Resource} esperado={expected.ExpectedMode}, observado={mode}, SPID={SessionId}.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            MarkLost();
        }
    }

    private void MarkLost()
    {
        if (Interlocked.Exchange(ref lostState, 1) == 0)
        {
            JornadaTelemetry.RecordPipelineLostToken(OwnerName);
            try { lost.Cancel(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        heartbeatStop.Cancel();
        try { await heartbeatTask; } catch { MarkLost(); }
        try
        {
            foreach (var expected in locksInReleaseOrder)
            {
                try { await SqlPipelineCoordinator.ReleaseAppLockAsync(connection, expected.Resource, CancellationToken.None); }
                catch { }
            }
        }
        finally
        {
            await connection.DisposeAsync();
            heartbeatStop.Dispose();
            lost.Dispose();
        }
    }
}

public sealed class PipelineCoordinationBusyException(string message) : InvalidOperationException(message);

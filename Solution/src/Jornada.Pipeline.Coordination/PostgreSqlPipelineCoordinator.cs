using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Pipeline.Coordination;

/// <summary>
/// Coordenação do pipeline para PostgreSQL usando advisory locks vinculados à sessão.
///
/// O protocolo é o mesmo do SqlPipelineCoordinator: o Processor usa Shared em ExclusiveRequest e
/// Exclusive em Corpus; jobs analíticos usam Exclusive em ambos. O hash da chave é determinístico
/// e só identifica o recurso de lock, não contém dado de cidadão.
/// </summary>
public sealed class PostgreSqlPipelineCoordinator
{
    public const string ExclusiveRequestResource = SqlPipelineCoordinator.ExclusiveRequestResource;
    public const string CorpusResource = SqlPipelineCoordinator.CorpusResource;

    private readonly IOperationalDatabaseAdapter operationalDatabase;
    private readonly TimeSpan heartbeatInterval;
    private readonly TimeSpan exclusiveIntentTimeout;

    public PostgreSqlPipelineCoordinator(
        string connectionString,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? exclusiveIntentTimeout = null)
        : this(new PostgreSqlOperationalAdapter(connectionString), heartbeatInterval, exclusiveIntentTimeout)
    {
    }

    public PostgreSqlPipelineCoordinator(
        IOperationalDatabaseAdapter operationalDatabase,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? exclusiveIntentTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(operationalDatabase);
        if (!string.Equals(operationalDatabase.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("PostgreSqlPipelineCoordinator exige provider PostgreSql.", nameof(operationalDatabase));

        this.operationalDatabase = operationalDatabase;
        this.heartbeatInterval = heartbeatInterval is { } hb && hb > TimeSpan.Zero ? hb : TimeSpan.FromSeconds(5);
        this.exclusiveIntentTimeout = exclusiveIntentTimeout is { } it && it >= TimeSpan.Zero ? it : TimeSpan.FromSeconds(5);
    }

    public async Task<PostgreSqlPipelineCoordinationLease?> TryAcquireProcessorBatchAsync(CancellationToken cancellationToken)
    {
        var connection = operationalDatabase.CreateDedicatedSessionConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);

            if (!await TryAcquireAsync(connection, ExclusiveRequestResource, shared: true, cancellationToken))
            {
                await connection.DisposeAsync();
                return null;
            }

            if (!await TryAcquireAsync(connection, CorpusResource, shared: false, cancellationToken))
            {
                await ReleaseBestEffortAsync(connection, ExclusiveRequestResource, shared: true);
                await connection.DisposeAsync();
                return null;
            }

            if (!await ReleaseAsync(connection, ExclusiveRequestResource, shared: true, cancellationToken))
                throw new InvalidOperationException("Falha ao liberar advisory lock compartilhado de intenção do Processor.");

            return await PostgreSqlPipelineCoordinationLease.CreateAsync(
                connection,
                "ProcessorBatch",
                new[] { new PostgreSqlExpectedAdvisoryLock(CorpusResource, Shared: false) },
                heartbeatInterval,
                cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<PostgreSqlPipelineCoordinationLease> AcquireExclusiveJobAsync(
        string ownerName,
        TimeSpan currentBatchDrainTimeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new ArgumentException("Nome do job exclusivo é obrigatório.", nameof(ownerName));
        ArgumentOutOfRangeException.ThrowIfLessThan(currentBatchDrainTimeout, TimeSpan.Zero);

        var connection = operationalDatabase.CreateDedicatedSessionConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);

            if (!await AcquireUntilAsync(
                    connection,
                    ExclusiveRequestResource,
                    shared: false,
                    exclusiveIntentTimeout,
                    cancellationToken))
                throw new PipelineCoordinationBusyException(
                    $"Outro job exclusivo do pipeline já está ativo ou aguardando. {ownerName} deve ser reagendado.");

            if (!await AcquireUntilAsync(
                    connection,
                    CorpusResource,
                    shared: false,
                    currentBatchDrainTimeout,
                    cancellationToken))
            {
                await ReleaseBestEffortAsync(connection, ExclusiveRequestResource, shared: false);
                throw new PipelineCoordinationBusyException(
                    $"O lote corrente do Processor não liberou o corpus em {currentBatchDrainTimeout.TotalSeconds:0}s. " +
                    $"{ownerName} não iniciou e deve ser reagendado após verificar o lote em execução.");
            }

            return await PostgreSqlPipelineCoordinationLease.CreateAsync(
                connection,
                ownerName,
                new[]
                {
                    new PostgreSqlExpectedAdvisoryLock(CorpusResource, Shared: false),
                    new PostgreSqlExpectedAdvisoryLock(ExclusiveRequestResource, Shared: false)
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

    internal static long ResourceKey(string resource)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(digest.AsSpan(0, sizeof(long)));
    }

    private static async Task<bool> AcquireUntilAsync(
        DbConnection connection,
        string resource,
        bool shared,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (await TryAcquireAsync(connection, resource, shared, cancellationToken)) return true;
        if (timeout <= TimeSpan.Zero) return false;

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var remaining = timeout - stopwatch.Elapsed;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100), cancellationToken);
            if (await TryAcquireAsync(connection, resource, shared, cancellationToken)) return true;
        }
        return false;
    }

    internal static async Task<bool> TryAcquireAsync(
        DbConnection connection,
        string resource,
        bool shared,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 30;
        command.CommandText = shared
            ? "SELECT pg_try_advisory_lock_shared(@key);"
            : "SELECT pg_try_advisory_lock(@key);";
        AddInt64(command, "@key", ResourceKey(resource));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task<bool> ReleaseAsync(
        DbConnection connection,
        string resource,
        bool shared,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 30;
        command.CommandText = shared
            ? "SELECT pg_advisory_unlock_shared(@key);"
            : "SELECT pg_advisory_unlock(@key);";
        AddInt64(command, "@key", ResourceKey(resource));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ReleaseBestEffortAsync(DbConnection connection, string resource, bool shared)
    {
        try { await ReleaseAsync(connection, resource, shared, CancellationToken.None); }
        catch { /* Pooling=false: fechar a sessão física libera advisory locks remanescentes. */ }
    }

    private static void AddInt64(DbCommand command, string name, long value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Int64;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

public sealed record PostgreSqlExpectedAdvisoryLock(string Resource, bool Shared);

public sealed class PostgreSqlPipelineCoordinationLease : IAsyncDisposable
{
    private readonly DbConnection connection;
    private readonly IReadOnlyList<PostgreSqlExpectedAdvisoryLock> locksInReleaseOrder;
    private readonly CancellationTokenSource heartbeatStop = new();
    private readonly CancellationTokenSource lost = new();
    private readonly Task heartbeatTask;
    private int disposed;
    private int lostState;

    private PostgreSqlPipelineCoordinationLease(
        DbConnection connection,
        string ownerName,
        int sessionId,
        IReadOnlyList<PostgreSqlExpectedAdvisoryLock> locksInReleaseOrder,
        TimeSpan heartbeatInterval)
    {
        this.connection = connection;
        OwnerName = ownerName;
        SessionId = sessionId;
        this.locksInReleaseOrder = locksInReleaseOrder;
        heartbeatTask = HeartbeatLoopAsync(heartbeatInterval, heartbeatStop.Token);
    }

    internal static async Task<PostgreSqlPipelineCoordinationLease> CreateAsync(
        DbConnection connection,
        string ownerName,
        IReadOnlyList<PostgreSqlExpectedAdvisoryLock> locksInReleaseOrder,
        TimeSpan heartbeatInterval,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_backend_pid();";
        command.CommandTimeout = 30;
        var sessionId = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        return new PostgreSqlPipelineCoordinationLease(connection, ownerName, sessionId, locksInReleaseOrder, heartbeatInterval);
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
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT pg_backend_pid(), COUNT(*)::int
                    FROM pg_locks
                    WHERE locktype='advisory' AND pid=pg_backend_pid()
                    GROUP BY pg_backend_pid();
                    """;
                command.CommandTimeout = Math.Clamp((int)Math.Ceiling(interval.TotalSeconds * 2), 2, 30);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException($"Heartbeat sem advisory locks; PID esperado={SessionId}.");
                var observedSessionId = reader.GetInt32(0);
                var advisoryLockCount = reader.GetInt32(1);
                if (observedSessionId != SessionId)
                    throw new InvalidOperationException(
                        $"Sessão coordenadora PostgreSQL mudou: PID esperado={SessionId}, observado={observedSessionId}.");
                if (advisoryLockCount < locksInReleaseOrder.Count)
                    throw new InvalidOperationException(
                        $"Quantidade de advisory locks inferior ao esperado: esperado>={locksInReleaseOrder.Count}, observado={advisoryLockCount}, PID={SessionId}.");
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
                try
                {
                    await PostgreSqlPipelineCoordinator.ReleaseAsync(
                        connection,
                        expected.Resource,
                        expected.Shared,
                        CancellationToken.None);
                }
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

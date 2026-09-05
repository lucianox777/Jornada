using System.Data;
using System.Diagnostics;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Jornada.Bronze.Maintenance.Worker;

public sealed record BronzeMaintenanceOptions
{
    public bool Enabled { get; init; } = true;
    public int ScanIntervalMinutes { get; init; } = 60;
    public int OrphanGraceHours { get; init; } = 24;
    public int MaxObjectsPerCycle { get; init; } = 1000;
    public int ObjectLockTimeoutSeconds { get; init; } = 5;
}

internal sealed record BronzeMaintenanceCursor(int Bucket, string? AfterObjectKey);
internal sealed record BronzeMaintenanceCycleResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int StartBucket,
    string? StartAfterObjectKey,
    int NextBucket,
    string? NextAfterObjectKey,
    int Scanned,
    int CanonicalDeleted,
    int TemporaryDeleted,
    int LockMisses,
    int StorageErrors,
    DateTimeOffset? OldestOrphanSeen);

internal sealed class BronzeMaintenanceRepository(IOperationalSqlAdapter operationalSql)
{

    public async Task<BronzeMaintenanceCursor> GetCursorAsync(CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT bucket_cursor,after_object_key FROM controle.bronze_manutencao_estado WHERE estado_id=1;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new BronzeMaintenanceCursor(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : new BronzeMaintenanceCursor(0, null);
    }

    public async Task SaveCycleAsync(BronzeMaintenanceCycleResult cycle, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            UPDATE controle.bronze_manutencao_estado
               SET bucket_cursor=@bucket,after_object_key=@after,atualizado_em=SYSUTCDATETIME()
             WHERE estado_id=1;
            IF @@ROWCOUNT=0
                INSERT controle.bronze_manutencao_estado(estado_id,bucket_cursor,after_object_key,atualizado_em)
                VALUES(1,@bucket,@after,SYSUTCDATETIME());

            INSERT controle.bronze_manutencao_ciclo(
                iniciado_em,finalizado_em,bucket_inicial,after_inicial,bucket_proximo,after_proximo,
                objetos_examinados,orfaos_removidos,temporarios_removidos,locks_nao_adquiridos,
                falhas_storage,orfao_mais_antigo_em)
            VALUES(@ini,@fim,@bini,@aini,@bprox,@aprox,@scan,@del,@tmp,@locks,@storage,@oldest);
            """;
        command.Parameters.AddWithValue("@bucket", cycle.NextBucket);
        command.Parameters.Add(new SqlParameter("@after", SqlDbType.NVarChar, 1024) { Value = (object?)cycle.NextAfterObjectKey ?? DBNull.Value });
        command.Parameters.AddWithValue("@ini", cycle.StartedAt);
        command.Parameters.AddWithValue("@fim", cycle.FinishedAt);
        command.Parameters.AddWithValue("@bini", cycle.StartBucket);
        command.Parameters.Add(new SqlParameter("@aini", SqlDbType.NVarChar, 1024) { Value = (object?)cycle.StartAfterObjectKey ?? DBNull.Value });
        command.Parameters.AddWithValue("@bprox", cycle.NextBucket);
        command.Parameters.Add(new SqlParameter("@aprox", SqlDbType.NVarChar, 1024) { Value = (object?)cycle.NextAfterObjectKey ?? DBNull.Value });
        command.Parameters.AddWithValue("@scan", cycle.Scanned);
        command.Parameters.AddWithValue("@del", cycle.CanonicalDeleted);
        command.Parameters.AddWithValue("@tmp", cycle.TemporaryDeleted);
        command.Parameters.AddWithValue("@locks", cycle.LockMisses);
        command.Parameters.AddWithValue("@storage", cycle.StorageErrors);
        command.Parameters.Add(new SqlParameter("@oldest", SqlDbType.DateTimeOffset) { Value = (object?)cycle.OldestOrphanSeen ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<(bool Deleted, bool LockMiss)> DeleteIfStillUnreferencedAsync(
        BronzeStoredObject candidate,
        IBronzeObjectMaintenanceStore store,
        int lockTimeoutSeconds,
        CancellationToken ct)
    {
        var hash = BronzeObjectCoordination.Sha256FromObjectKey(candidate.ObjectKey);
        var resource = BronzeObjectCoordination.LockResourceForSha256(hash);
        await using var connection = await operationalSql.OpenDedicatedSessionAsync(ct);

        var lockHeld = await AcquireAsync(connection, resource, Math.Max(0, lockTimeoutSeconds) * 1000, ct);
        if (!lockHeld) return (false, true);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT_BIG(*) FROM bronze.entrega_arquivo WHERE payload_sha256=@sha AND objeto_chave=@chave AND estado_armazenamento='DISPONIVEL';";
            command.Parameters.Add(new SqlParameter("@sha", SqlDbType.Char, 64) { Value = hash });
            command.Parameters.Add(new SqlParameter("@chave", SqlDbType.NVarChar, 1024) { Value = candidate.ObjectKey });
            var references = Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            if (references != 0) return (false, false);
            return (await store.DeleteIfExistsAsync(candidate.ObjectKey, ct), false);
        }
        finally
        {
            await ReleaseAsync(connection, resource);
        }
    }

    private static async Task<bool> AcquireAsync(SqlConnection connection, string resource, int timeoutMs, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @rc int;
            EXEC @rc=sys.sp_getapplock
                @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=@timeout;
            SELECT @rc;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.AddWithValue("@timeout", timeoutMs);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private static async Task ReleaseAsync(SqlConnection connection, string resource)
    {
        if (connection.State != ConnectionState.Open) return;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            SqlConnection.ClearPool(connection);
            try { connection.Close(); } catch { }
        }
    }
}

internal sealed class BronzeMaintenanceWorker(
    IBronzeObjectMaintenanceStore store,
    BronzeMaintenanceRepository repository,
    IOptions<BronzeMaintenanceOptions> optionsAccessor,
    ILogger<BronzeMaintenanceWorker> logger) : BackgroundService
{
    private BronzeMaintenanceOptions Options => optionsAccessor.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Options.Enabled) await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo de manutenção da Bronze; nenhum objeto referenciado deve ser removido.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, Options.ScanIntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    internal async Task RunCycleAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var cursor = await repository.GetCursorAsync(ct);
        var cutoff = started.AddHours(-Math.Max(1, Options.OrphanGraceHours));
        var tempDeleted = 0;
        var canonicalDeleted = 0;
        var scanned = 0;
        var lockMisses = 0;
        var storageErrors = 0;
        DateTimeOffset? oldestOrphan = null;
        var bucket = Math.Clamp(cursor.Bucket, 0, 0xffff);
        var after = cursor.AfterObjectKey;
        var nextBucket = bucket;
        string? nextAfter = after;

        try { tempDeleted = await store.DeleteStaleTemporaryFilesAsync(cutoff, ct); }
        catch (BronzeStorageUnavailableException) { storageErrors++; throw; }

        var max = Math.Max(1, Options.MaxObjectsPerCycle);
        var bucketsVisited = 0;
        while (scanned < max && bucketsVisited < 65536)
        {
            var yielded = false;
            await foreach (var candidate in store.EnumerateCanonicalObjectsInBucketAsync(bucket, after, cutoff, ct))
            {
                yielded = true;
                scanned++;
                nextBucket = bucket;
                nextAfter = candidate.ObjectKey;
                try
                {
                    var outcome = await repository.DeleteIfStillUnreferencedAsync(candidate, store, Options.ObjectLockTimeoutSeconds, ct);
                    if (outcome.LockMiss) lockMisses++;
                    if (outcome.Deleted)
                    {
                        canonicalDeleted++;
                        if (!oldestOrphan.HasValue || candidate.LastWriteUtc < oldestOrphan.Value)
                            oldestOrphan = candidate.LastWriteUtc;
                    }
                }
                catch (BronzeStorageUnavailableException) { storageErrors++; throw; }
                if (scanned >= max) break;
            }

            if (scanned >= max && yielded) break;

            // Bucket concluído (inclusive vazio): avança. Ao chegar a ffff, volta a 0000.
            bucket = (bucket + 1) & 0xffff;
            after = null;
            nextBucket = bucket;
            nextAfter = null;
            bucketsVisited++;
        }

        var finished = DateTimeOffset.UtcNow;
        await repository.SaveCycleAsync(new BronzeMaintenanceCycleResult(
            started, finished, cursor.Bucket, cursor.AfterObjectKey, nextBucket, nextAfter,
            scanned, canonicalDeleted, tempDeleted, lockMisses, storageErrors, oldestOrphan), ct);
        JornadaTelemetry.RecordBronzeMaintenance(scanned, canonicalDeleted, lockMisses, storageErrors);

        logger.LogInformation(
            "Manutenção Bronze concluída. Temporários={Temporarios}; órfãos={Orfaos}; examinados={Examinados}; locks_não_adquiridos={Locks}; próximo_bucket={Bucket:x4}; próximo_after={After}.",
            tempDeleted, canonicalDeleted, scanned, lockMisses, nextBucket, nextAfter);
    }
}

using System.Data;
using Jornada.Bronze.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Jornada.Operations.Maintenance.Worker;

public sealed record DeliveryBronzeRetentionOptions
{
    public bool Enabled { get; init; }
    public int RetentionDays { get; init; }
    public int MaxRowsPerCycle { get; init; } = 500;
    public int IntervalMinutes { get; init; } = 60;
    public int ObjectLockTimeoutSeconds { get; init; } = 5;
}

internal sealed record DeliveryRetentionCandidate(Guid EntregaId, string ObjectKey, string Sha256, string StorageState);
internal sealed record DeliveryRetentionOutcome(bool ReferenceExpired, bool ObjectDeleted, bool SharedObjectPreserved, bool LockMiss, bool StorageError);

/// <summary>
/// Expira a disponibilidade do payload Bronze sem apagar a Entrega nem sua linhagem SQL.
/// O objeto físico content-addressed só é removido quando nenhuma referência ainda DISPONIVEL o protege.
/// O mesmo app lock por SHA usado pela API/GC fecha a corrida com escrita, deduplicação e manutenção.
/// </summary>
public sealed class DeliveryBronzeRetentionWorker(
    IConfiguration configuration,
    IBronzeObjectMaintenanceStore store,
    IOptions<DeliveryBronzeRetentionOptions> options,
    ILogger<DeliveryBronzeRetentionWorker> logger) : BackgroundService
{
    private readonly string connectionString = configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled) await RunCycleAsync(options.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo de retenção de Entregas/Bronze.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    public async Task RunCycleAsync(DeliveryBronzeRetentionOptions cfg, CancellationToken ct)
    {
        if (cfg.RetentionDays <= 0) throw new InvalidOperationException("RetentionDays deve ser > 0.");
        var started = DateTimeOffset.UtcNow;
        var cutoff = started.AddDays(-cfg.RetentionDays);
        var candidates = await LoadCandidatesAsync(cutoff, Math.Max(1, cfg.MaxRowsPerCycle), ct);
        var expired = 0; var deleted = 0; var shared = 0; var lockMisses = 0; var storageErrors = 0;

        foreach (var candidate in candidates)
        {
            var outcome = await ProcessCandidateAsync(candidate, cutoff, cfg.ObjectLockTimeoutSeconds, ct);
            if (outcome.ReferenceExpired) expired++;
            if (outcome.ObjectDeleted) deleted++;
            if (outcome.SharedObjectPreserved) shared++;
            if (outcome.LockMiss) lockMisses++;
            if (outcome.StorageError) storageErrors++;
        }

        await SaveCycleAsync(started, DateTimeOffset.UtcNow, candidates.Count, expired, deleted, shared, lockMisses, storageErrors, ct);
        logger.LogInformation("Retenção Bronze: candidatos={Candidates}; referências expurgadas={Expired}; objetos removidos={Deleted}; compartilhados preservados={Shared}; locks={Locks}; falhas storage={StorageErrors}.",
            candidates.Count, expired, deleted, shared, lockMisses, storageErrors);
    }

    private async Task<List<DeliveryRetentionCandidate>> LoadCandidatesAsync(DateTimeOffset cutoff, int maxRows, CancellationToken ct)
    {
        var result = new List<DeliveryRetentionCandidate>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(@max) b.entrega_id,b.objeto_chave,b.payload_sha256,b.estado_armazenamento
            FROM bronze.entrega_arquivo b
            JOIN ingestao.entrega e ON e.entrega_id=b.entrega_id
            WHERE b.estado_armazenamento IN('DISPONIVEL','EXPURGO_PENDENTE')
              AND e.status IN('PROCESSADA','REJEITADA')
              AND e.recebido_em<@cutoff
            ORDER BY e.recebido_em,b.entrega_id;
            """;
        command.Parameters.AddWithValue("@max", maxRows);
        command.Parameters.AddWithValue("@cutoff", cutoff);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new DeliveryRetentionCandidate(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return result;
    }

    private async Task<DeliveryRetentionOutcome> ProcessCandidateAsync(DeliveryRetentionCandidate candidate, DateTimeOffset cutoff, int timeoutSeconds, CancellationToken ct)
    {
        var resource = BronzeObjectCoordination.LockResourceForSha256(candidate.Sha256);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        if (!await AcquireAsync(connection, resource, Math.Max(0, timeoutSeconds) * 1000, ct))
            return new DeliveryRetentionOutcome(false,false,false,true,false);

        try
        {
            var shouldContinue = false;
            await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                await using var mark = connection.CreateCommand();
                mark.Transaction = tx;
                mark.CommandText = """
                    DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
                    UPDATE b
                       SET estado_armazenamento=CASE WHEN b.estado_armazenamento='DISPONIVEL' THEN 'EXPURGO_PENDENTE' ELSE b.estado_armazenamento END,
                           expurgo_iniciado_em=COALESCE(b.expurgo_iniciado_em,@agora),
                           retencao_motivo=COALESCE(b.retencao_motivo,'PRAZO_RETENCAO_EXPIRADO')
                    FROM bronze.entrega_arquivo b WITH(UPDLOCK,HOLDLOCK)
                    JOIN ingestao.entrega e ON e.entrega_id=b.entrega_id
                    WHERE b.entrega_id=@entrega
                      AND b.estado_armazenamento IN('DISPONIVEL','EXPURGO_PENDENTE')
                      AND e.status IN('PROCESSADA','REJEITADA')
                      AND e.recebido_em<@cutoff;
                    SELECT @@ROWCOUNT;
                    """;
                mark.Parameters.AddWithValue("@entrega", candidate.EntregaId);
                mark.Parameters.AddWithValue("@cutoff", cutoff);
                shouldContinue = Convert.ToInt32(await mark.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
                await tx.CommitAsync(ct);
            }
            if (!shouldContinue) return new DeliveryRetentionOutcome(false,false,false,false,false);

            long liveReferences;
            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT_BIG(*) FROM bronze.entrega_arquivo WHERE payload_sha256=@sha AND objeto_chave=@chave AND estado_armazenamento='DISPONIVEL';";
                count.Parameters.Add(new SqlParameter("@sha", SqlDbType.Char, 64) { Value = candidate.Sha256 });
                count.Parameters.Add(new SqlParameter("@chave", SqlDbType.NVarChar, 1024) { Value = candidate.ObjectKey });
                liveReferences = Convert.ToInt64(await count.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }

            var objectDeleted = false;
            var storageError = false;
            if (liveReferences == 0)
            {
                try { objectDeleted = await store.DeleteIfExistsAsync(candidate.ObjectKey, ct); }
                catch (BronzeStorageUnavailableException) { storageError = true; }
                if (storageError) return new DeliveryRetentionOutcome(false,false,false,false,true);
            }

            await using (var finish = connection.CreateCommand())
            {
                finish.CommandText = """
                    UPDATE bronze.entrega_arquivo
                       SET estado_armazenamento='EXPURGADO',expurgado_em=SYSUTCDATETIME(),
                           expurgo_iniciado_em=COALESCE(expurgo_iniciado_em,SYSUTCDATETIME()),
                           retencao_motivo=COALESCE(retencao_motivo,'PRAZO_RETENCAO_EXPIRADO')
                     WHERE entrega_id=@entrega AND estado_armazenamento='EXPURGO_PENDENTE';
                    """;
                finish.Parameters.AddWithValue("@entrega", candidate.EntregaId);
                await finish.ExecuteNonQueryAsync(ct);
            }
            return new DeliveryRetentionOutcome(true,objectDeleted,liveReferences>0,false,false);
        }
        finally { await ReleaseAsync(connection, resource); }
    }

    private async Task SaveCycleAsync(DateTimeOffset started, DateTimeOffset finished, int candidates, int expired, int deleted, int shared, int locks, int storageErrors, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT controle.entrega_retencao_ciclo(iniciado_em,finalizado_em,candidatos,referencias_expurgadas,objetos_fisicos_removidos,objetos_compartilhados_preservados,locks_nao_adquiridos,falhas_storage)
            VALUES(@i,@f,@c,@e,@d,@s,@l,@x);
            """;
        command.Parameters.AddWithValue("@i", started); command.Parameters.AddWithValue("@f", finished);
        command.Parameters.AddWithValue("@c", candidates); command.Parameters.AddWithValue("@e", expired);
        command.Parameters.AddWithValue("@d", deleted); command.Parameters.AddWithValue("@s", shared);
        command.Parameters.AddWithValue("@l", locks); command.Parameters.AddWithValue("@x", storageErrors);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> AcquireAsync(SqlConnection connection, string resource, int timeoutMs, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @rc int;
            EXEC @rc=sys.sp_getapplock @Resource=@r,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=@t;
            SELECT @rc;
            """;
        command.Parameters.Add(new SqlParameter("@r", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.AddWithValue("@t", timeoutMs);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private static async Task ReleaseAsync(SqlConnection connection, string resource)
    {
        if (connection.State != ConnectionState.Open) return;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@r,@LockOwner='Session';";
            command.Parameters.Add(new SqlParameter("@r", SqlDbType.NVarChar, 255) { Value = resource });
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch { SqlConnection.ClearPool(connection); }
    }
}

using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal enum ProcessingFailureOutcome { RetryScheduled, Poison }

internal sealed partial class SqlProcessorRepository
{
    public async Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
                DECLARE @afetados TABLE(entrega_id UNIQUEIDENTIFIER);

                UPDATE l
                   SET status=CASE WHEN l.tentativa_count>=@max_attempts THEN 'POISON' ELSE 'PENDENTE' END,
                       erro_codigo=CASE WHEN l.tentativa_count>=@max_attempts THEN 'LEASE_EXPIRADO_MAX_TENTATIVAS' ELSE 'RECUPERADO_LEASE_EXPIRADO' END,
                       recuperacao_count=l.recuperacao_count+1,
                       lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                       proxima_tentativa_em=CASE WHEN l.tentativa_count>=@max_attempts THEN NULL ELSE @agora END,
                       poison_em=CASE WHEN l.tentativa_count>=@max_attempts THEN COALESCE(l.poison_em,@agora) ELSE l.poison_em END,
                       atualizado_em=@agora
                OUTPUT INSERTED.entrega_id INTO @afetados(entrega_id)
                FROM ingestao.lote l WITH (UPDLOCK,READPAST,ROWLOCK)
                WHERE l.status IN('VALIDANDO','PROCESSANDO')
                  AND l.lease_expira_em IS NOT NULL
                  AND l.lease_expira_em < @agora;

                DECLARE @entrega UNIQUEIDENTIFIER;
                DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT entrega_id FROM @afetados;
                OPEN c; FETCH NEXT FROM c INTO @entrega;
                WHILE @@FETCH_STATUS=0
                BEGIN
                    EXEC ingestao.sp_recalcular_entrega @entrega_id=@entrega;
                    FETCH NEXT FROM c INTO @entrega;
                END
                CLOSE c; DEALLOCATE c;

                SELECT COUNT(*) FROM @afetados;
                """;
            command.Parameters.AddWithValue("@max_attempts", Math.Max(1, maxAttempts));
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            await tx.CommitAsync(ct);
            return count;
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<ReservedBatch?> ReserveNextAsync(CancellationToken ct) =>
        ReserveNextAsync($"compat:{Environment.ProcessId}:{Guid.NewGuid():N}", TimeSpan.FromMinutes(2), ct);

    public async Task<ReservedBatch?> ReserveNextAsync(string leaseOwner, TimeSpan leaseDuration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner)) throw new ArgumentException("leaseOwner obrigatório.", nameof(leaseOwner));
        var leaseId = Guid.NewGuid();
        Guid? loteId = null;
        int attemptNumber = 0;
        var leaseSeconds = Math.Max(30, (int)Math.Ceiling(leaseDuration.TotalSeconds));

        await using (var connection = await connections.OpenAsync(ct))
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
                    DECLARE @reservado TABLE(lote_id UNIQUEIDENTIFIER,entrega_id UNIQUEIDENTIFIER,tentativa_count INT);
                    ;WITH candidato AS (
                        SELECT TOP(1) l.lote_id
                        FROM ingestao.lote l WITH (UPDLOCK,READPAST,ROWLOCK)
                        JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                        WHERE l.status='PENDENTE'
                          AND (l.proxima_tentativa_em IS NULL OR l.proxima_tentativa_em<=@agora)
                          AND e.status IN('RECEBIDA','VALIDANDO','PROCESSANDO')
                        ORDER BY l.proxima_tentativa_em,l.criado_em,l.lote_seq,l.lote_id
                    )
                    UPDATE l
                       SET status='VALIDANDO',erro_codigo=NULL,
                           tentativa_count=tentativa_count+1,ultima_tentativa_em=@agora,
                           lease_id=@lease_id,lease_owner=@lease_owner,lease_adquirido_em=@agora,
                           heartbeat_em=@agora,lease_expira_em=DATEADD(SECOND,@lease_seconds,@agora),
                           proxima_tentativa_em=NULL,atualizado_em=@agora
                    OUTPUT INSERTED.lote_id,INSERTED.entrega_id,INSERTED.tentativa_count
                      INTO @reservado(lote_id,entrega_id,tentativa_count)
                    FROM ingestao.lote l JOIN candidato c ON c.lote_id=l.lote_id;

                    UPDATE e SET status='VALIDANDO',ultima_atualizacao=@agora
                    FROM ingestao.entrega e JOIN @reservado r ON r.entrega_id=e.entrega_id;

                    SELECT lote_id,tentativa_count FROM @reservado;
                    """;
                command.Parameters.AddWithValue("@lease_id", leaseId);
                command.Parameters.Add(new SqlParameter("@lease_owner", SqlDbType.NVarChar, 200) { Value = leaseOwner[..Math.Min(200, leaseOwner.Length)] });
                command.Parameters.AddWithValue("@lease_seconds", leaseSeconds);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    loteId = reader.GetGuid(0);
                    attemptNumber = reader.GetInt32(1);
                }
                await reader.DisposeAsync();
                await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        if (loteId is null) return null;

        await using var load = await connections.OpenAsync(ct);
        await using var commandLoad = load.CreateCommand();
        commandLoad.CommandText = """
            SELECT l.lote_id,e.entrega_id,g.codigo,e.gestor_id,so.sistema_origem_id,so.codigo sistema_origem,
                   e.gestor_pessoa_versao_id,e.natureza,
                   e.tipo_registro_id,e.tipo_registro_versao_id,tr.codigo,trv.versao,gpv.versao,e.data_referencia,
                   e.payload_sha256,b.nome_arquivo,b.objeto_chave,b.tamanho_bytes,gpv.pessoa_schema_ref,gpv.pessoa_schema_sha256,
                   trv.schema_registro_ref,trv.schema_registro_sha256,trv.qc_status,trv.origina_endereco_casa_abrigo_sigilosa,
                   trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,trv.regime_vigencia
            FROM ingestao.lote l
            JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
            JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_pessoa_versao_id=e.gestor_pessoa_versao_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id AND b.estado_armazenamento='DISPONIVEL'
            LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
            -- Reprocessamento/replay técnico é determinístico: usa a versão do Tipo gravada na Entrega.
            -- Mudança normativa posterior exige reavaliação governada explícita; não troca o contrato do lote em silêncio.
            LEFT JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=e.tipo_registro_versao_id
            WHERE l.lote_id=@lote_id AND l.lease_id=@lease_id AND l.lease_owner=@lease_owner;
            """;
        commandLoad.Parameters.AddWithValue("@lote_id", loteId.Value);
        commandLoad.Parameters.AddWithValue("@lease_id", leaseId);
        commandLoad.Parameters.Add(new SqlParameter("@lease_owner", SqlDbType.NVarChar, 200) { Value = leaseOwner[..Math.Min(200, leaseOwner.Length)] });
        await using var batchReader = await commandLoad.ExecuteReaderAsync(ct);
        if (!await batchReader.ReadAsync(ct))
            throw new InvalidOperationException("Lote reservado não pôde ser recarregado ou lease foi perdido.");

        IntegrationNature? nature = batchReader.IsDBNull(7) ? null : Enum.Parse<IntegrationNature>(batchReader.GetString(7), ignoreCase: true);
        return new ReservedBatch(
            batchReader.GetGuid(0), batchReader.GetGuid(1), leaseId, leaseOwner, attemptNumber,
            batchReader.GetString(2), batchReader.GetInt64(3), batchReader.GetInt64(4), batchReader.GetString(5), batchReader.GetInt64(6), nature,
            batchReader.IsDBNull(8) ? null : batchReader.GetInt64(8),
            batchReader.IsDBNull(9) ? null : batchReader.GetInt64(9),
            batchReader.IsDBNull(10) ? null : batchReader.GetString(10),
            batchReader.IsDBNull(11) ? null : batchReader.GetInt32(11),
            batchReader.GetInt32(12), batchReader.GetDateTimeOffset(13), batchReader.GetString(14), batchReader.GetString(15), batchReader.GetString(16), batchReader.GetInt64(17),
            batchReader.GetString(18), batchReader.IsDBNull(19) ? throw new InvalidDataException("Contrato cadastral ativo sem SHA-256 aprovado.") : (byte[])batchReader.GetValue(19),
            batchReader.IsDBNull(20) ? null : batchReader.GetString(20),
            batchReader.IsDBNull(21) ? null : (byte[])batchReader.GetValue(21),
            batchReader.IsDBNull(22) ? null : batchReader.GetString(22),
            !batchReader.IsDBNull(23) && batchReader.GetBoolean(23),
            batchReader.IsDBNull(24) ? null : DateOnly.FromDateTime(batchReader.GetDateTime(24)),
            batchReader.IsDBNull(25) ? null : DateOnly.FromDateTime(batchReader.GetDateTime(25)),
            batchReader.IsDBNull(26) ? null : batchReader.GetString(26));
    }

    public async Task<bool> HeartbeatAsync(ReservedBatch batch, TimeSpan leaseDuration, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
            UPDATE ingestao.lote
               SET heartbeat_em=@agora,lease_expira_em=DATEADD(SECOND,@lease_seconds,@agora),atualizado_em=@agora
             WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner
               AND status IN('VALIDANDO','PROCESSANDO') AND lease_expira_em>=@agora;
            SELECT @@ROWCOUNT;
            """;
        command.Parameters.AddWithValue("@lease_seconds", Math.Max(30, (int)Math.Ceiling(leaseDuration.TotalSeconds)));
        AddLeaseParameters(command, batch);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    public Task MarkRejectedAsync(ReservedBatch batch, string errorCode, CancellationToken ct) =>
        MarkFailedAsync(batch, "REJEITADO", errorCode, ct);

    public Task MarkQuarantineAsync(ReservedBatch batch, string errorCode, CancellationToken ct) =>
        MarkFailedAsync(batch, "QUARENTENA", errorCode, ct);

    public async Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        ReservedBatch batch, string errorCode, int maxAttempts, TimeSpan retryBase, TimeSpan retryMax, CancellationToken ct)
    {
        if (errorCode.Length > 80) errorCode = errorCode[..80];
        var poison = batch.AttemptNumber >= Math.Max(1, maxAttempts);
        var exponent = Math.Max(0, batch.AttemptNumber - 1);
        var delaySeconds = Math.Min(retryMax.TotalSeconds, retryBase.TotalSeconds * Math.Pow(2, Math.Min(20, exponent)));

        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
            DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
            UPDATE ingestao.lote
               SET status=@status,erro_codigo=@erro,
                   proxima_tentativa_em=CASE WHEN @poison=1 THEN NULL ELSE DATEADD(SECOND,@retry_seconds,@agora) END,
                   poison_em=CASE WHEN @poison=1 THEN COALESCE(poison_em,@agora) ELSE poison_em END,
                   lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                   atualizado_em=@agora
             WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner;
            IF @@ROWCOUNT<>1 THROW 51020,'Lease perdido ao reagendar lote.',1;
            EXEC ingestao.sp_recalcular_entrega @entrega_id=@entrega_id;
            """;
            command.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 40) { Value = poison ? "POISON" : "PENDENTE" });
            command.Parameters.Add(new SqlParameter("@erro", SqlDbType.NVarChar, 80) { Value = errorCode });
            command.Parameters.AddWithValue("@poison", poison);
            command.Parameters.AddWithValue("@retry_seconds", Math.Max(1, (int)Math.Ceiling(delaySeconds)));
            AddLeaseParameters(command, batch);
            command.Parameters.AddWithValue("@entrega_id", batch.EntregaId);
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return poison ? ProcessingFailureOutcome.Poison : ProcessingFailureOutcome.RetryScheduled;
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task MarkFailedAsync(ReservedBatch batch, string status, string errorCode, CancellationToken ct)
    {
        if (errorCode.Length > 80) errorCode = errorCode[..80];
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
            UPDATE ingestao.lote
               SET status=@status,erro_codigo=@erro,lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner;
            IF @@ROWCOUNT<>1 THROW 51021,'Lease perdido ao finalizar lote com falha.',1;
            EXEC ingestao.sp_recalcular_entrega @entrega_id=@entrega_id;
            """;
            command.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 40) { Value = status });
            command.Parameters.Add(new SqlParameter("@erro", SqlDbType.NVarChar, 80) { Value = errorCode });
            AddLeaseParameters(command, batch);
            command.Parameters.AddWithValue("@entrega_id", batch.EntregaId);
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task SetProcessingAsync(SqlConnection connection, SqlTransaction tx, ReservedBatch batch, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            UPDATE ingestao.lote SET status='PROCESSANDO',atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote_id AND status='VALIDANDO' AND lease_id=@lease_id AND lease_owner=@lease_owner AND lease_expira_em>=SYSUTCDATETIME();
            IF @@ROWCOUNT<>1 THROW 51000,'Lote não está reservado em VALIDANDO ou lease expirou.',1;
            UPDATE ingestao.entrega SET status='PROCESSANDO',ultima_atualizacao=SYSUTCDATETIME() WHERE entrega_id=@entrega_id;
            """;
        AddLeaseParameters(command, batch);
        command.Parameters.AddWithValue("@entrega_id", batch.EntregaId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddLeaseParameters(SqlCommand command, ReservedBatch batch)
    {
        command.Parameters.AddWithValue("@lote_id", batch.LoteId);
        command.Parameters.AddWithValue("@lease_id", batch.LeaseId);
        command.Parameters.Add(new SqlParameter("@lease_owner", SqlDbType.NVarChar, 200) { Value = batch.LeaseOwner });
    }
}

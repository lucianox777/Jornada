using System.Data;
using System.Data.Common;

namespace Jornada.Operational.Sql;

public enum PostgreSqlProcessingFailureOutcome { RetryScheduled, Poison }

public sealed record PostgreSqlProcessorLease(
    Guid LoteId,
    Guid EntregaId,
    Guid LeaseId,
    string LeaseOwner,
    int AttemptNumber,
    string GestorCodigo,
    long GestorId,
    long SistemaOrigemId,
    string CodigoSistemaOrigem,
    long GestorPessoaVersaoId,
    int PessoaSchemaVersao,
    string? Natureza,
    long? TipoRegistroId,
    long? TipoRegistroVersaoId,
    string? CodigoTipo,
    int? TipoVersao,
    DateTimeOffset DataReferencia,
    string PayloadSha256,
    string NomeArquivo,
    string ObjetoChave,
    long TamanhoBytes,
    string? PessoaSchemaRef,
    byte[]? PessoaSchemaSha256,
    string? RegistroSchemaRef,
    byte[]? RegistroSchemaSha256,
    string? QcStatus,
    bool OriginaEnderecoCasaAbrigoSigilosa,
    DateOnly? DataInicioPermitidaConcessao,
    DateOnly? DataFimPermitidaConcessao,
    string? RegimeVigencia);

/// <summary>
/// Reserva concorrente do Processor para PostgreSQL. Usa FOR UPDATE SKIP LOCKED como equivalente
/// semântico de UPDLOCK+READPAST e mantém fencing explícito por lease_id + lease_owner.
/// </summary>
public sealed class PostgreSqlProcessorLeaseStore
{
    private readonly IOperationalDatabaseAdapter database;

    public PostgreSqlProcessorLeaseStore(IOperationalDatabaseAdapter database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        if (!string.Equals(database.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("PostgreSqlProcessorLeaseStore exige provider PostgreSql.", nameof(database));
    }

    public async Task<PostgreSqlProcessorLease?> ReserveNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner)) throw new ArgumentException("leaseOwner obrigatório.", nameof(leaseOwner));
        var owner = leaseOwner[..Math.Min(200, leaseOwner.Length)];
        var leaseId = Guid.NewGuid();
        var seconds = Math.Max(30, (int)Math.Ceiling(leaseDuration.TotalSeconds));

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            Guid? loteId = null;
            Guid? entregaId = null;
            var attempt = 0;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    WITH candidato AS (
                        SELECT l.lote_id
                          FROM ingestao.lote l
                          JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                         WHERE l.status='PENDENTE'
                           AND (l.proxima_tentativa_em IS NULL OR l.proxima_tentativa_em<=CURRENT_TIMESTAMP)
                           AND e.status IN ('RECEBIDA','VALIDANDO','PROCESSANDO')
                         ORDER BY l.proxima_tentativa_em NULLS FIRST,l.criado_em,l.lote_seq,l.lote_id
                         FOR UPDATE OF l SKIP LOCKED
                         LIMIT 1
                    )
                    UPDATE ingestao.lote l
                       SET status='VALIDANDO',erro_codigo=NULL,
                           tentativa_count=l.tentativa_count+1,ultima_tentativa_em=CURRENT_TIMESTAMP,
                           lease_id=@lease_id,lease_owner=@lease_owner,lease_adquirido_em=CURRENT_TIMESTAMP,
                           heartbeat_em=CURRENT_TIMESTAMP,
                           lease_expira_em=CURRENT_TIMESTAMP + (@lease_seconds * INTERVAL '1 second'),
                           proxima_tentativa_em=NULL,atualizado_em=CURRENT_TIMESTAMP
                      FROM candidato c
                     WHERE l.lote_id=c.lote_id
                    RETURNING l.lote_id,l.entrega_id,l.tentativa_count;
                    """;
                Add(command, "@lease_id", leaseId);
                Add(command, "@lease_owner", owner);
                Add(command, "@lease_seconds", seconds);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    loteId = reader.GetGuid(0);
                    entregaId = reader.GetGuid(1);
                    attempt = reader.GetInt32(2);
                }
            }

            if (loteId is null || entregaId is null)
            {
                await transaction.CommitAsync(ct);
                return null;
            }

            await using (var updateDelivery = connection.CreateCommand())
            {
                updateDelivery.Transaction = transaction;
                updateDelivery.CommandText = "UPDATE ingestao.entrega SET status='VALIDANDO',ultima_atualizacao=CURRENT_TIMESTAMP WHERE entrega_id=@entrega_id;";
                Add(updateDelivery, "@entrega_id", entregaId.Value);
                await updateDelivery.ExecuteNonQueryAsync(ct);
            }

            var lease = await LoadLeaseAsync(connection, transaction, loteId.Value, leaseId, owner, attempt, ct)
                ?? throw new InvalidOperationException("Lote reservado não pôde ser recarregado ou lease foi perdido.");
            await transaction.CommitAsync(ct);
            return lease;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task<bool> HeartbeatAsync(
        PostgreSqlProcessorLease lease,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingestao.lote
               SET heartbeat_em=CURRENT_TIMESTAMP,
                   lease_expira_em=CURRENT_TIMESTAMP + (@lease_seconds * INTERVAL '1 second'),
                   atualizado_em=CURRENT_TIMESTAMP
             WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner
               AND status IN ('VALIDANDO','PROCESSANDO') AND lease_expira_em>=CURRENT_TIMESTAMP;
            """;
        Add(command, "@lease_seconds", Math.Max(30, (int)Math.Ceiling(leaseDuration.TotalSeconds)));
        AddLease(command, lease);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var deliveryIds = new HashSet<Guid>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE ingestao.lote
                       SET status=CASE WHEN tentativa_count>=@max_attempts THEN 'POISON' ELSE 'PENDENTE' END,
                           erro_codigo=CASE WHEN tentativa_count>=@max_attempts THEN 'LEASE_EXPIRADO_MAX_TENTATIVAS' ELSE 'RECUPERADO_LEASE_EXPIRADO' END,
                           recuperacao_count=recuperacao_count+1,
                           lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                           proxima_tentativa_em=CASE WHEN tentativa_count>=@max_attempts THEN NULL ELSE CURRENT_TIMESTAMP END,
                           poison_em=CASE WHEN tentativa_count>=@max_attempts THEN COALESCE(poison_em,CURRENT_TIMESTAMP) ELSE poison_em END,
                           atualizado_em=CURRENT_TIMESTAMP
                     WHERE status IN ('VALIDANDO','PROCESSANDO')
                       AND lease_expira_em IS NOT NULL
                       AND lease_expira_em<CURRENT_TIMESTAMP
                    RETURNING entrega_id;
                    """;
                Add(command, "@max_attempts", Math.Max(1, maxAttempts));
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) deliveryIds.Add(reader.GetGuid(0));
            }

            foreach (var deliveryId in deliveryIds)
                await RecalculateDeliveryAsync(connection, transaction, deliveryId, ct);

            await transaction.CommitAsync(ct);
            return deliveryIds.Count;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task<PostgreSqlProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        PostgreSqlProcessorLease lease,
        string errorCode,
        int maxAttempts,
        TimeSpan retryBase,
        TimeSpan retryMax,
        CancellationToken ct = default)
    {
        var error = errorCode[..Math.Min(80, errorCode.Length)];
        var poison = lease.AttemptNumber >= Math.Max(1, maxAttempts);
        var exponent = Math.Max(0, lease.AttemptNumber - 1);
        var delaySeconds = Math.Min(retryMax.TotalSeconds, retryBase.TotalSeconds * Math.Pow(2, Math.Min(20, exponent)));

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ingestao.lote
                   SET status=@status,erro_codigo=@erro,
                       proxima_tentativa_em=CASE WHEN @poison THEN NULL ELSE CURRENT_TIMESTAMP + (@retry_seconds * INTERVAL '1 second') END,
                       poison_em=CASE WHEN @poison THEN COALESCE(poison_em,CURRENT_TIMESTAMP) ELSE poison_em END,
                       lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                       atualizado_em=CURRENT_TIMESTAMP
                 WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner;
                """;
            Add(command, "@status", poison ? "POISON" : "PENDENTE");
            Add(command, "@erro", error);
            Add(command, "@poison", poison);
            Add(command, "@retry_seconds", Math.Max(1, (int)Math.Ceiling(delaySeconds)));
            AddLease(command, lease);
            if (await command.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("Lease perdido ao reagendar lote PostgreSQL.");
            await RecalculateDeliveryAsync(connection, transaction, lease.EntregaId, ct);
            await transaction.CommitAsync(ct);
            return poison ? PostgreSqlProcessingFailureOutcome.Poison : PostgreSqlProcessingFailureOutcome.RetryScheduled;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task MarkFailedAsync(
        PostgreSqlProcessorLease lease,
        string status,
        string errorCode,
        CancellationToken ct = default)
    {
        if (status is not ("REJEITADO" or "QUARENTENA"))
            throw new ArgumentOutOfRangeException(nameof(status));
        var error = errorCode[..Math.Min(80, errorCode.Length)];

        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ingestao.lote
                   SET status=@status,erro_codigo=@erro,
                       lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                       atualizado_em=CURRENT_TIMESTAMP
                 WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner;
                """;
            Add(command, "@status", status);
            Add(command, "@erro", error);
            AddLease(command, lease);
            if (await command.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException("Lease perdido ao finalizar lote PostgreSQL.");
            await RecalculateDeliveryAsync(connection, transaction, lease.EntregaId, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static async Task<PostgreSqlProcessorLease?> LoadLeaseAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid loteId,
        Guid leaseId,
        string leaseOwner,
        int attempt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.lote_id,e.entrega_id,g.codigo,e.gestor_id,so.sistema_origem_id,so.codigo,
                   e.gestor_pessoa_versao_id,gpv.versao,e.natureza,
                   e.tipo_registro_id,e.tipo_registro_versao_id,tr.codigo,trv.versao,e.data_referencia,
                   e.payload_sha256,b.nome_arquivo,b.objeto_chave,b.tamanho_bytes,
                   gpv.pessoa_schema_ref,gpv.pessoa_schema_sha256,
                   trv.schema_registro_ref,trv.schema_registro_sha256,trv.qc_status,
                   COALESCE(trv.origina_endereco_casa_abrigo_sigilosa,FALSE),
                   trv.data_inicio_permitida_concessao,trv.data_fim_permitida_concessao,trv.regime_vigencia
              FROM ingestao.lote l
              JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
              JOIN ref.gestor g ON g.gestor_id=e.gestor_id
              JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
              JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_pessoa_versao_id=e.gestor_pessoa_versao_id
              JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id AND b.estado_armazenamento='DISPONIVEL'
              LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
              LEFT JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=e.tipo_registro_versao_id
             WHERE l.lote_id=@lote_id AND l.lease_id=@lease_id AND l.lease_owner=@lease_owner;
            """;
        Add(command, "@lote_id", loteId);
        Add(command, "@lease_id", leaseId);
        Add(command, "@lease_owner", leaseOwner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PostgreSqlProcessorLease(
            reader.GetGuid(0),reader.GetGuid(1),leaseId,leaseOwner,attempt,
            reader.GetString(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetString(5),
            reader.GetInt64(6),reader.GetInt32(7),reader.IsDBNull(8)?null:reader.GetString(8),
            reader.IsDBNull(9)?null:reader.GetInt64(9),reader.IsDBNull(10)?null:reader.GetInt64(10),
            reader.IsDBNull(11)?null:reader.GetString(11),reader.IsDBNull(12)?null:reader.GetInt32(12),
            ReadTimestamp(reader,13),reader.GetString(14),reader.GetString(15),reader.GetString(16),reader.GetInt64(17),
            reader.IsDBNull(18)?null:reader.GetString(18),reader.IsDBNull(19)?null:(byte[])reader.GetValue(19),
            reader.IsDBNull(20)?null:reader.GetString(20),reader.IsDBNull(21)?null:(byte[])reader.GetValue(21),
            reader.IsDBNull(22)?null:reader.GetString(22),reader.GetBoolean(23),
            reader.IsDBNull(24)?null:DateOnly.FromDateTime(reader.GetDateTime(24)),
            reader.IsDBNull(25)?null:DateOnly.FromDateTime(reader.GetDateTime(25)),
            reader.IsDBNull(26)?null:reader.GetString(26));
    }

    private static async Task RecalculateDeliveryAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid entregaId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ingestao.recalcular_entrega(@entrega_id);";
        Add(command, "@entrega_id", entregaId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddLease(DbCommand command, PostgreSqlProcessorLease lease)
    {
        Add(command, "@lote_id", lease.LoteId);
        Add(command, "@lease_id", lease.LeaseId);
        Add(command, "@lease_owner", lease.LeaseOwner);
    }

    private static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Valor temporal PostgreSQL inesperado: {value.GetType().FullName}.")
        };
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

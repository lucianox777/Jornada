using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Processor.Worker;

/// <summary>Referência inicial e atribuição legada; não constitui uma resolução probabilística.</summary>
public sealed record ProgressiveOriginRegistration(
    long SourceId, Guid InitialUuid, Guid? LegacyCanonicalUuid,
    ProgressiveIdentityStatus Status, long Version);

/// <summary>Armazenamento progressivo; criação e origem participam da transação do chamador.</summary>
public sealed class ProgressiveIdentityOriginStore
{
    private readonly IOperationalDatabaseAdapter database;
    private readonly bool postgres;

    public ProgressiveIdentityOriginStore(IOperationalDatabaseAdapter database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<ProgressiveOriginRegistration> EnsureInitialAsync(long sourceId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await EnsureInitialAsync(connection, tx, sourceId, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception) { /* Preserva a exceção original. */ }
            throw;
        }
    }

    public async Task<ProgressiveOriginRegistration> EnsureInitialAsync(
        DbConnection connection, DbTransaction tx, long sourceId, CancellationToken ct = default)
    {
        ValidateTransaction(connection, tx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = postgres
            ? "SELECT identidade.assegurar_origem_progressiva(@source_id);"
            : "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source_id;";
        Add(command, "@source_id", DbType.Int64, sourceId);
        if (postgres)
        {
            var value = await command.ExecuteScalarAsync(ct);
            if (value is not Guid uuid || uuid==Guid.Empty)
                throw new InvalidOperationException("Criação progressiva não devolveu UUID válido.");
        }
        else await command.ExecuteNonQueryAsync(ct);
        return await ReadInTransactionAsync(connection, tx, sourceId, ct)
            ?? throw new InvalidOperationException("Criação progressiva não persistiu a referência.");
    }

    public async Task<ProgressiveOriginRegistration?> ReadAsync(long sourceId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        await using var connection = await database.OpenAsync(ct);
        return await ReadInTransactionAsync(connection, null, sourceId, ct);
    }

    public async Task<int> BackfillPageAsync(int maxSources, CancellationToken ct = default)
    {
        if (maxSources is <1 or >1000) throw new ArgumentOutOfRangeException(nameof(maxSources));
        var ids = new List<long>();
        await using (var connection = await database.OpenAsync(ct))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = postgres
                ? "SELECT o.pessoa_origem_id FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL ORDER BY o.pessoa_origem_id LIMIT @max_sources;"
                : "SELECT TOP (@max_sources) o.pessoa_origem_id FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL ORDER BY o.pessoa_origem_id;";
            Add(command, "@max_sources", DbType.Int32, maxSources);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt64(0));
        }
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await EnsureInitialAsync(id, ct);
        }
        return ids.Count;
    }

    /// <summary>Compatibilidade de leitura V1. Não aceita estados desconhecidos nem altera o banco.</summary>
    public static ProgressiveIdentityStatus ParseStatus(string value) => value switch
    {
        "PROVISORIA" => ProgressiveIdentityStatus.PROVISORIA,
        "RESOLVIDA" or "REFERENCIA" => ProgressiveIdentityStatus.REFERENCIA,
        "INDEFINIDA" => ProgressiveIdentityStatus.INDEFINIDA,
        _ => throw new InvalidOperationException("Estado progressivo desconhecido no armazenamento.")
    };

    private static async Task<ProgressiveOriginRegistration?> ReadInTransactionAsync(
        DbConnection connection, DbTransaction? tx, long sourceId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT initial_uuid,legacy_pessoa_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source_id;";
        Add(command, "@source_id", DbType.Int64, sourceId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var uuid = reader.GetGuid(0);
        if (uuid==Guid.Empty) throw new InvalidOperationException("UUID inicial inválido no armazenamento.");
        var legacy = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var status = ParseStatus(reader.GetString(2));
        var version = reader.GetInt64(3);
        if (version<0) throw new InvalidOperationException("Versão progressiva inválida.");
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Origem possui referências progressivas duplicadas.");
        return new ProgressiveOriginRegistration(sourceId, uuid, legacy, status, version);
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tx);
        if (!ReferenceEquals(tx.Connection, connection) || connection.State!=ConnectionState.Open)
            throw new InvalidOperationException("É necessária uma transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName=name;
        parameter.DbType=type;
        parameter.Value=value;
        command.Parameters.Add(parameter);
    }
}

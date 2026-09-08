using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Processor.Worker;

/// <summary>Referência inicial e atribuição legada; não constitui uma resolução probabilística.</summary>
public sealed record ProgressiveOriginRegistration(
    long SourceId, Guid InitialUuid, Guid? LegacyCanonicalUuid,
    ProgressiveIdentityStatus Status, long Version);

/// <summary>
/// Armazenamento V1, opt-in. A criação exige uma origem Silver já persistida e
/// participa da transação do chamador. O lock da origem serializa criações;
/// o chamador de uma transação serializável deve reiniciá-la após falha de serialização.
/// Não publica decisões, vínculos ou fatos.
/// </summary>
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
            // Não mascarar a causa original caso o provider já tenha abortado a transação.
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception) { /* Preserva a exceção da operação original. */ }
            throw;
        }
    }

    /// <summary>Não abre ou confirma transação. O Processor futuro chamará após criar a origem.</summary>
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
        // O SQL Server devolve uma linha de registro; PostgreSQL devolve o UUID.
        if (postgres)
        {
            var value = await command.ExecuteScalarAsync(ct);
            if (value is not Guid uuid || uuid==Guid.Empty)
                throw new InvalidOperationException("Criação progressiva não devolveu UUID válido.");
        }
        else
        {
            // ExecuteNonQuery não depende do número de linhas afetadas pela procedure.
            await command.ExecuteNonQueryAsync(ct);
        }
        return await ReadInTransactionAsync(connection, tx, sourceId, ct)
            ?? throw new InvalidOperationException("Criação progressiva não persistiu a referência.");
    }

    public async Task<ProgressiveOriginRegistration?> ReadAsync(long sourceId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        await using var connection = await database.OpenAsync(ct);
        return await ReadInTransactionAsync(connection, null, sourceId, ct);
    }

    /// <summary>Backfill limitado e retomável. Cada origem é confirmada em sua própria transação.</summary>
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
        var statusText = reader.GetString(2);
        if (!Enum.TryParse<ProgressiveIdentityStatus>(statusText, false, out var status) ||
            !Enum.IsDefined(status))
            throw new InvalidOperationException("Estado progressivo desconhecido no armazenamento.");
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

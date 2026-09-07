using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Read-only PostgreSQL capture for an externally attested source frame. The selected
/// candidate IDs are restricted evidence, not identity labels or operational decisions.
/// </summary>
public sealed class PostgreSqlCandidateSampler
{
    private readonly IOperationalDatabaseAdapter database;

    public PostgreSqlCandidateSampler(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Provider != OperationalDatabaseProviders.PostgreSql)
            throw new ArgumentException("A captura exige PostgreSql.", nameof(database));
        this.database = database;
    }

    public async Task<CandidateSamplingCapture> CaptureAsync(CandidateSamplingFrame frame,
        CandidateSamplingOptions options, byte[] seed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(seed);
        options.Validate();
        if (seed.Length != 32) throw new ArgumentException("A chave deve ter 256 bits.", nameof(seed));
        var key = seed.ToArray();
        try
        {
            await using var connection = await database.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            try
            {
                await using (var readOnly = Command(connection, tx, "SET TRANSACTION READ ONLY;", options.CommandTimeoutSeconds))
                    await readOnly.ExecuteNonQueryAsync(ct);
                string snapshot;
                DateTimeOffset capturedAt;
                await using (var command = Command(connection, tx,
                    "SELECT txid_current_snapshot()::text,clock_timestamp();", options.CommandTimeoutSeconds))
                await using (var reader = await command.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Snapshot indisponível.");
                    snapshot = reader.GetString(0);
                    capturedAt = reader.GetFieldValue<DateTimeOffset>(1);
                }
                var result = await CandidateSamplingEngine.CaptureAsync(frame, options, key, snapshot, capturedAt,
                    (source, token) => EnumerateAsync(connection, tx, source, options, token), ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task<IReadOnlyList<CandidateSamplingCandidate>> EnumerateAsync(
        DbConnection connection, DbTransaction tx, CandidateUniverseSource source,
        CandidateSamplingOptions options, CancellationToken ct)
    {
        var plan = BirthBlockingPlan.Create(source.BirthDate, source.Name, source.Mother,
            options.UseComponents, options.YearTolerance);
        await using var command = Command(connection, tx, string.Empty, options.CommandTimeoutSeconds);
        var query = PostgreSqlBirthBlockingQuery.Build(command, plan);
        var limit = command.CreateParameter();
        limit.ParameterName = "@limit";
        limit.DbType = DbType.Int32;
        limit.Value = options.MaxCandidatesPerSource + 1;
        command.Parameters.Add(limit);
        command.CommandText = $"""
            SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae,{query.PassMaskExpression}
            FROM gold.pessoa g WHERE {query.Predicate}
            ORDER BY g.pessoa_uuid LIMIT @limit;
            """;
        var result = new List<CandidateSamplingCandidate>();
        var seen = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (result.Count >= options.MaxCandidatesPerSource)
                throw new InvalidOperationException("Bloco excede limite; nenhuma amostra parcial será retornada.");
            var id = reader.GetGuid(0);
            var date = DateOnly.FromDateTime(reader.GetDateTime(2));
            var mask = (BirthBlockingPass)reader.GetInt32(4);
            if (id == Guid.Empty || !seen.Add(id) || mask == BirthBlockingPass.None ||
                mask != plan.Match(date, reader.GetString(1), reader.GetString(3)))
                throw new InvalidOperationException("Divergência entre blocking SQL e plano compartilhado.");
            result.Add(new CandidateSamplingCandidate(id, mask));
        }
        return result;
    }

    private static DbCommand Command(DbConnection connection, DbTransaction tx, string sql, int timeout)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.CommandTimeout = timeout;
        return command;
    }
}

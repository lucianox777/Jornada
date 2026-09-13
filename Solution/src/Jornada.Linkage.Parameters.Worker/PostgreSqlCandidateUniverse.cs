using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>A source supplied by a separately governed sampling/labeling process.</summary>
public sealed record CandidateUniverseSource(Guid SourceId, DateOnly BirthDate,
    string Name, string? Mother, Guid? KnownPessoaUuid = null);

/// <summary>Explicit resource bounds; exceeding any bound aborts the capture rather than truncating it.</summary>
public sealed record CandidateUniverseOptions(bool UseComponents, int YearTolerance,
    int MaxSources, int MaxCandidatesPerSource, long MaxPairs, int CommandTimeoutSeconds)
{
    public void Validate()
    {
        if (YearTolerance is < 0 or > 2 || MaxSources is < 1 or > 100_000 ||
            MaxCandidatesPerSource is < 1 or > 1_000_000 || MaxPairs is < 1 or > 5_000_000 ||
            CommandTimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(MaxPairs), "Limites de captura inválidos.");
    }
}

public sealed record CandidateUniversePassCount(BirthBlockingPass Pass, long Members, long Primary);

/// <summary>
/// Aggregate evidence only. Different UUIDs are not negative labels, and a known UUID
/// is not proof of a match unless the independent labeling process establishes it.
/// </summary>
public sealed record CandidateUniverseCapture(string BlockingVersion, string SnapshotToken,
    DateTimeOffset CapturedAt, string ConfigurationFingerprint, string PairFingerprint,
    int SourceCount, int ZeroCandidateSources, long PairCount, long DistinctCandidateCount,
    long SameKnownUuidCount, long OverlapCount, IReadOnlyList<CandidateUniversePassCount> PassCounts);

/// <summary>
/// Read-only diagnostic for the exact candidate universe of an explicitly supplied source sample.
/// It does not select a statistically representative source sample, infer negative labels,
/// estimate m/u, write a model, or authorize validation/activation. All rows are read in one
/// MVCC snapshot and only aggregate counts/fingerprints leave this component.
/// </summary>
public sealed class PostgreSqlCandidateUniverse
{
    private readonly IOperationalDatabaseAdapter database;

    public PostgreSqlCandidateUniverse(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Provider != OperationalDatabaseProviders.PostgreSql)
            throw new ArgumentException("A captura exige PostgreSql.", nameof(database));
        this.database = database;
    }

    public async Task<CandidateUniverseCapture> CaptureAsync(IReadOnlyList<CandidateUniverseSource> sources,
        CandidateUniverseOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (sources.Count == 0 || sources.Count > options.MaxSources)
            throw new ArgumentOutOfRangeException(nameof(sources), "Quantidade de fontes fora dos limites explícitos.");
        var ordered = sources.OrderBy(x => x.SourceId.ToString("D"), StringComparer.Ordinal).ToArray();
        if (ordered.Any(x => x.SourceId == Guid.Empty || x.Name is null || x.KnownPessoaUuid == Guid.Empty) ||
            ordered.Select(x => x.SourceId).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Fontes exigem IDs únicos, nome não nulo e UUID conhecido válido.", nameof(sources));

        var config = HashText(string.Join("|", BirthBlockingPlan.Version, options.UseComponents ? "1" : "0",
            options.YearTolerance.ToString(CultureInfo.InvariantCulture),
            options.MaxSources.ToString(CultureInfo.InvariantCulture),
            options.MaxCandidatesPerSource.ToString(CultureInfo.InvariantCulture),
            options.MaxPairs.ToString(CultureInfo.InvariantCulture)) + "\n");
        var membership = new long[5];
        var primary = new long[5];
        var distinct = new HashSet<Guid>();
        long pairs = 0, sameKnown = 0, overlaps = 0;
        var zeroSources = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, config + "\n");
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
            foreach (var source in ordered)
            {
                ct.ThrowIfCancellationRequested();
                var plan = BirthBlockingPlan.Create(source.BirthDate, source.Name, source.Mother,
                    options.UseComponents, options.YearTolerance);
                Append(hash, source.SourceId.ToString("D") + "|" +
                    source.KnownPessoaUuid?.ToString("D") + "|" + plan.ConfigurationFingerprint() + "\n");
                var sourcePairs = 0;
                var seen = new HashSet<Guid>();
                await using var command = Command(connection, tx, string.Empty, options.CommandTimeoutSeconds);
                var query = PostgreSqlBirthBlockingQuery.Build(command, plan);
                Add(command, "@limit", DbType.Int32, options.MaxCandidatesPerSource + 1);
                command.CommandText = $"""
                    SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae,{query.PassMaskExpression}
                    FROM gold.pessoa g WHERE {query.Predicate}
                    ORDER BY g.pessoa_uuid LIMIT @limit;
                    """;
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var candidateId = reader.GetGuid(0);
                    var candidateDate = DateOnly.FromDateTime(reader.GetDateTime(2));
                    var mask = (BirthBlockingPass)reader.GetInt32(4);
                    if (mask == BirthBlockingPass.None || mask != plan.Match(candidateDate, reader.GetString(1), reader.IsDBNull(3) ? null : reader.GetString(3)))
                        throw new InvalidOperationException("Divergência entre blocking SQL e plano compartilhado; captura interrompida.");
                    if (!seen.Add(candidateId))
                        throw new InvalidOperationException("UUID duplicado no universo de candidatos.");
                    sourcePairs++;
                    if (sourcePairs > options.MaxCandidatesPerSource || pairs >= options.MaxPairs)
                        throw new InvalidOperationException("Universo excede os limites explícitos; nenhuma amostra será truncada.");
                    pairs++;
                    distinct.Add(candidateId);
                    if (source.KnownPessoaUuid == candidateId) sameKnown++;
                    if (BitOperations.PopCount((uint)mask) > 1) overlaps++;
                    for (var i = 0; i < BirthBlockingPlan.OrderedPasses.Count; i++)
                    {
                        var pass = BirthBlockingPlan.OrderedPasses[i];
                        if ((mask & pass) != 0) membership[i]++;
                        if (BirthBlockingPlan.PrimaryPass(mask) == pass) primary[i]++;
                    }
                    Append(hash, candidateId.ToString("D") + "|" + ((int)mask).ToString(CultureInfo.InvariantCulture) + "\n");
                }
                if (sourcePairs == 0) zeroSources++;
            }
            await tx.CommitAsync(ct);
            var counts = BirthBlockingPlan.OrderedPasses.Select((pass, i) =>
                new CandidateUniversePassCount(pass, membership[i], primary[i])).ToArray();
            return new CandidateUniverseCapture(BirthBlockingPlan.Version, snapshot, capturedAt, config,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), ordered.Length, zeroSources,
                pairs, distinct.Count, sameKnown, overlaps, counts);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static DbCommand Command(DbConnection connection, DbTransaction tx, string sql, int timeout)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.CommandTimeout = timeout;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void Append(IncrementalHash hash, string text) => hash.AppendData(Encoding.UTF8.GetBytes(text));
}

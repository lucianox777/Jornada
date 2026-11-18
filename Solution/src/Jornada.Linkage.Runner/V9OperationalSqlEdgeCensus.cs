using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Separate, read-only SQL edge-census path. Does not consume the preview or
/// its edges. Shares the pinned V9 planner, so physical independence and
/// correctness of the planner still require an external oracle.
/// </summary>
public static class V9OperationalSqlEdgeCensus
{
    public static async Task<V9OperationalCandidateGraphIntegrity.Census> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(pinnedRuntime);
        ArgumentNullException.ThrowIfNull(frozenCohort);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");
        if (frozenCohort.Count == 0)
            throw new InvalidDataException("RUNNER_V9_COHORT_EMPTY");

        var lookup = new Dictionary<Guid, V9OperationalCohortGraphPreview.Source>();
        var observationIds = new HashSet<long>();
        foreach (var source in frozenCohort)
        {
            if (source is null || source.Observation is null
                || !string.IsNullOrWhiteSpace(source.Observation.Cpf)
                || source.ObservationId <= 0 || source.GoldUuid == Guid.Empty
                || string.IsNullOrWhiteSpace(source.Manager)
                || !lookup.TryAdd(source.GoldUuid, source)
                || !observationIds.Add(source.ObservationId))
                throw new InvalidDataException("RUNNER_V9_COHORT_INVALID_OR_DUPLICATE");
        }

        // SQL is queried separately for every frozen source, without calling
        // V9OperationalCandidateRead or V9OperationalCohortGraphPreview.
        var directed = new Dictionary<(long A, long B), byte>();
        foreach (var source in frozenCohort.OrderBy(s => s.ObservationId))
        {
            ct.ThrowIfCancellationRequested();
            await using var command = new SqlCommand {
                Connection = connection, Transaction = snapshotTransaction, CommandTimeout = 0
            };
            var candidateSql = pinnedRuntime.BuildCandidateUuidQuery(command,
                source.Observation, pinnedRuntime.ProjectionSchemaVersion,
                pinnedRuntime.ProjectionFingerprintSha256);
            command.CommandText = "SELECT g.pessoa_uuid FROM (" + candidateSql +
                ") c INNER JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid" +
                " WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var uniqueCandidates = new HashSet<Guid>();
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (reader.IsDBNull(0))
                    throw new InvalidDataException("RUNNER_V9_CENSUS_NULL_CANDIDATE");
                var candidate = reader.GetGuid(0);
                if (!uniqueCandidates.Add(candidate))
                    throw new InvalidDataException("RUNNER_V9_CENSUS_DUPLICATE_CANDIDATE");
                if (!lookup.TryGetValue(candidate, out var target))
                    throw new InvalidDataException("RUNNER_V9_CENSUS_OUTSIDE_COHORT");
                if (target.ObservationId == source.ObservationId
                    || string.Equals(source.Manager, target.Manager, StringComparison.Ordinal))
                    continue;
                var a = Math.Min(source.ObservationId, target.ObservationId);
                var b = Math.Max(source.ObservationId, target.ObservationId);
                var key = (A: a, B: b);
                var bit = (byte)(source.ObservationId == a ? 1 : 2);
                directed.TryGetValue(key, out var previous);
                if ((previous & bit) != 0)
                    throw new InvalidDataException("RUNNER_V9_CENSUS_DUPLICATE_DIRECTION");
                directed[key] = (byte)(previous | bit);
            }
        }
        if (directed.Values.Any(bits => bits != 3))
            throw new InvalidDataException("RUNNER_V9_CENSUS_ASYMMETRIC");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (a, b) in directed.Keys.OrderBy(e => e.A).ThenBy(e => e.B))
            sha.AppendData(Encoding.UTF8.GetBytes(
                a.ToString(CultureInfo.InvariantCulture) + ":" +
                b.ToString(CultureInfo.InvariantCulture) + "\n"));
        return new V9OperationalCandidateGraphIntegrity.Census(
            observationIds.Count, directed.Count,
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }
}

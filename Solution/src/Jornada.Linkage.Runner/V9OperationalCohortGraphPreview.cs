using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Nonpublishing cohort-wide candidate graph preview using the operational V9
/// blocking query. Caller must supply a separately frozen, complete cohort and
/// own the SNAPSHOT transaction. This is NOT productive certification.
/// </summary>
public static class V9OperationalCohortGraphPreview
{
    public sealed record Source(long ObservationId, Guid GoldUuid,
        string Manager, IdentityObservation Observation);
    public sealed record Receipt(long ObservationCount, long DirectedCandidateCount,
        long UndirectedEdgeCount, string CanonicalEdgesSha256,
        IReadOnlyList<(long A, long B)> Edges);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime,
        IReadOnlyCollection<Source> frozenCohort, CancellationToken ct)
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

        var byUuid = new Dictionary<Guid, Source>();
        var byId = new HashSet<long>();
        foreach (var source in frozenCohort)
        {
            if (source is null || source.ObservationId <= 0
                || source.GoldUuid == Guid.Empty || string.IsNullOrWhiteSpace(source.Manager)
                || source.Observation is null || !string.IsNullOrWhiteSpace(source.Observation.Cpf)
                || !byUuid.TryAdd(source.GoldUuid, source)
                || !byId.Add(source.ObservationId))
                throw new InvalidDataException("RUNNER_V9_COHORT_INVALID_OR_DUPLICATE");
        }

        // The Gold UUID-to-observation mapping is an explicit external input:
        // never infer membership from candidate hits or silently discard an
        // outside-cohort candidate. A separate physical freeze is still needed.
        var directions = new Dictionary<(long A, long B), byte>();
        long directed = 0;
        foreach (var source in frozenCohort.OrderBy(s => s.ObservationId))
        {
            ct.ThrowIfCancellationRequested();
            var result = await V9OperationalCandidateRead.ReadAsync(
                connection, snapshotTransaction, pinnedRuntime, source.Observation, ct);
            foreach (var candidateUuid in result.CandidateUuids)
            {
                if (!byUuid.TryGetValue(candidateUuid, out var target))
                    throw new InvalidDataException("RUNNER_V9_CANDIDATE_OUTSIDE_FROZEN_COHORT");
                if (target.ObservationId == source.ObservationId
                    || string.Equals(target.Manager, source.Manager, StringComparison.Ordinal))
                    continue;
                directed = checked(directed + 1);
                var a = Math.Min(source.ObservationId, target.ObservationId);
                var b = Math.Max(source.ObservationId, target.ObservationId);
                var edge = (A: a, B: b);
                var bit = (byte)(source.ObservationId == a ? 1 : 2);
                directions.TryGetValue(edge, out var existing);
                if ((existing & bit) != 0)
                    throw new InvalidDataException("RUNNER_V9_GRAPH_DUPLICATE_DIRECTION");
                directions[edge] = (byte)(existing | bit);
            }
        }
        if (directions.Values.Any(bits => bits != 3))
            throw new InvalidDataException("RUNNER_V9_GRAPH_ASYMMETRIC");

        var edges = directions.Keys.OrderBy(e => e.A).ThenBy(e => e.B).ToArray();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (a, b) in edges)
            sha.AppendData(Encoding.UTF8.GetBytes(
                a.ToString(CultureInfo.InvariantCulture) + ":" +
                b.ToString(CultureInfo.InvariantCulture) + "\n"));
        if (directed != checked((long)edges.Length * 2))
            throw new InvalidDataException("RUNNER_V9_GRAPH_DIRECTION_COUNT_MISMATCH");
        return new Receipt(frozenCohort.Count, directed, edges.LongLength,
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            Array.AsReadOnly(edges));
    }
}

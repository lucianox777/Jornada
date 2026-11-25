using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only retrieval of the COMPLETE Gold candidate UUID union for ONE observation.
/// Uses the unchanged V9 production query builder; this is not a global cohort graph,
/// productive scoring or certification. Caller owns the SNAPSHOT transaction.
/// </summary>
public static class V9OperationalCandidateRead
{
    public sealed record Receipt(long CandidateCount, string CanonicalUuidsSha256,
        IReadOnlyList<Guid> CandidateUuids);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime, IdentityObservation observation,
        CancellationToken ct, bool useIsolatedProjection = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(pinnedRuntime);
        ArgumentNullException.ThrowIfNull(observation);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        // The same parameterized INTERSECT/UNION, temporal and projection
        // semantics used by the existing operational blocking planner.
        await using var cmd = new SqlCommand { Connection = connection,
            Transaction = snapshotTransaction, CommandTimeout = 0 };
        var candidateSql = useIsolatedProjection
            ? pinnedRuntime.BuildIsolatedCandidateUuidQuery(cmd, observation)
            : pinnedRuntime.BuildCandidateUuidQuery(cmd, observation,
                pinnedRuntime.ProjectionSchemaVersion, pinnedRuntime.ProjectionFingerprintSha256);
        var source = " FROM (" + candidateSql + ") c INNER JOIN gold.pessoa g" +
            " ON g.pessoa_uuid=c.pessoa_uuid WHERE g.estado_identidade=N'REFERENCIA'";
        cmd.CommandText = "SELECT g.pessoa_uuid" + source + " ORDER BY g.pessoa_uuid;";
        var ids = new List<Guid>();
        var unique = new HashSet<Guid>();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (reader.IsDBNull(0))
                    throw new InvalidDataException("RUNNER_V9_CANDIDATE_NULL_UUID");
                var id = reader.GetGuid(0);
                if (!unique.Add(id))
                    throw new InvalidDataException("RUNNER_V9_CANDIDATE_DUPLICATE");
                ids.Add(id);
                sha.AppendData(Encoding.UTF8.GetBytes(id.ToString("D") + "\n"));
            }
        }
        // Repeat the same full candidate query within the same SNAPSHOT to
        // reconcile streamed cardinality, without a TOP or synthetic limit.
        cmd.CommandText = "SELECT COUNT_BIG(*) FROM (" +
            "SELECT g.pessoa_uuid" + source + ") all_candidates;";
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct),
            System.Globalization.CultureInfo.InvariantCulture);
        if (count != ids.Count)
            throw new InvalidDataException("RUNNER_V9_CANDIDATE_COUNT_MISMATCH");
        return new Receipt(count, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            ids.AsReadOnly());
    }
}

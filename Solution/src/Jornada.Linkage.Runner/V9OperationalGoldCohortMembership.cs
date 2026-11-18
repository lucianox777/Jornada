using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only comparison of the caller's frozen Gold UUID membership with the
/// COMPLETE operational Gold reference population in one SNAPSHOT transaction.
/// Does not authenticate observation attributes, managers or graph edges.
/// </summary>
public static class V9OperationalGoldCohortMembership
{
    public sealed record Receipt(long GoldReferenceCount, string CanonicalGoldUuidsSha256);

    public static async Task<Receipt> VerifyAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(frozenCohort);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        var expected = new HashSet<Guid>();
        foreach (var source in frozenCohort)
        {
            if (source is null || source.GoldUuid == Guid.Empty)
                throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_DUPLICATE_OR_INVALID");
            expected.Add(source.GoldUuid);
        }

        long count;
        await using (var census = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';",
            connection, snapshotTransaction) { CommandTimeout = 120 })
            count = Convert.ToInt64(await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (count < 0 || count != expected.Count)
            throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_COUNT_MISMATCH");

        await using var query = new SqlCommand(
            "SELECT pessoa_uuid FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA' ORDER BY pessoa_uuid;",
            connection, snapshotTransaction) { CommandTimeout = 0 };
        var seen = new HashSet<Guid>();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var reader = await query.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsDBNull(0))
                throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_NULL_UUID");
            var id = reader.GetGuid(0);
            if (!seen.Add(id) || !expected.Contains(id))
                throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_MEMBERSHIP_MISMATCH");
            sha.AppendData(Encoding.UTF8.GetBytes(id.ToString("D") + "\n"));
            if (seen.Count > count)
                throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_COUNT_MISMATCH");
        }
        if (seen.Count != count || seen.Count != expected.Count)
            throw new InvalidDataException("RUNNER_V9_GOLD_COHORT_COUNT_MISMATCH");
        return new Receipt(count, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }
}

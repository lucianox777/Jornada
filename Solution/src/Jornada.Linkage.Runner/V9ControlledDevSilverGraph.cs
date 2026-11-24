using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Physical Silver-to-Silver graph for the FULL frozen DEV run, inside the SAME
/// read-only operational SNAPSHOT as Silver/Gold authentication. Uses the frozen
/// V9 blocking planner, a SQL DISTINCT signature join and complete set/census
/// verification; never treats a pending observation as an established Gold target.
/// No TEST-only 512-record cap, no supplied edges and no production mutation.
/// </summary>
public static class V9ControlledDevSilverGraph
{
    public sealed record Receipt(IReadOnlyList<(int A, int B)> Edges,
        int SignatureCount, string CanonicalEdgesSha256);

    public static async Task<Receipt> ReadAsync(SqlConnection connection,
        SqlTransaction snapshot, V9FittedSnapshot winner,
        IReadOnlyList<V9FrozenRunObservation> frozen,
        IReadOnlyDictionary<long, string> managers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(managers);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshot.Connection, connection)
            || snapshot.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_DEV_GRAPH_SNAPSHOT_REQUIRED");
        if (managers.Count != frozen.Count
            || frozen.Count > int.MaxValue
            || frozen.Any(x => x is null || x.PessoaObservacaoId <= 0
                || x.Observation is null || x.Observation.Cpf is not null
                || !managers.TryGetValue(x.PessoaObservacaoId, out var manager)
                || string.IsNullOrWhiteSpace(manager)))
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_UNIVERSE_INVALID");

        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        if (planner.MaximumPrefixLength is null
            || planner.ProjectionSchemaVersion != V9CalibrationBlockingProjection.SchemaVersion
            || !string.Equals(planner.ProjectionFingerprintSha256,
                winner.Payload.ProjectionFingerprintSha256, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_MODEL_MISMATCH");

        var signatures = new HashSet<(int Id, string Manager, string Pass, string Hash)>();
        for (var i = 0; i < frozen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = frozen[i];
            var manager = managers[row.PessoaObservacaoId];
            if (manager.Length > 120 || manager.EndsWith(' '))
                throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_MANAGER_INVALID");
            foreach (var pass in planner.Plan(row.Observation))
            {
                if (string.IsNullOrWhiteSpace(pass.PassId)
                    || pass.PassId.Length > 120 || pass.PassId.EndsWith(' ')
                    || pass.Clauses.Any(x => x.Values.Count == 0))
                    throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_PASS_INVALID");
                var values = new string[pass.Clauses.Count];
                Enumerate(0);
                void Enumerate(int position)
                {
                    if (position == values.Length)
                    {
                        var signature = pass.PassId + "\u001e" + string.Join("\u001f", values);
                        var hash = Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant();
                        signatures.Add((i + 1, manager, pass.PassId, hash));
                        return;
                    }
                    foreach (var value in pass.Clauses[position].Values)
                    {
                        ct.ThrowIfCancellationRequested();
                        values[position] = value;
                        Enumerate(position + 1);
                    }
                }
            }
        }

        // Temp tables belong solely to the physically guarded DEV connection.
        await using (var setup = new SqlCommand("""
            CREATE TABLE #v9_dev_silver_sig(
              obs_id int NOT NULL,
              gestor nvarchar(120) COLLATE Latin1_General_100_BIN2 NOT NULL,
              pass_id nvarchar(120) COLLATE Latin1_General_100_BIN2 NOT NULL,
              signature char(64) COLLATE Latin1_General_100_BIN2 NOT NULL
            );
            CREATE UNIQUE INDEX UX_v9_dev_silver_sig
              ON #v9_dev_silver_sig(obs_id,pass_id,signature);
            CREATE INDEX IX_v9_dev_silver_sig_join
              ON #v9_dev_silver_sig(pass_id,signature,obs_id);
            """, connection, snapshot))
            await setup.ExecuteNonQueryAsync(ct);

        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("gestor", typeof(string));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("signature", typeof(string));
        foreach (var signature in signatures)
            table.Rows.Add(signature.Id, signature.Manager, signature.Pass, signature.Hash);

        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, snapshot)
            { DestinationTableName = "#v9_dev_silver_sig", BatchSize = 2048, BulkCopyTimeout = 0 })
        {
            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        var observed = new HashSet<(int Id, string Manager, string Pass, string Hash)>();
        await using (var verify = new SqlCommand(
            "SELECT obs_id,gestor,pass_id,signature FROM #v9_dev_silver_sig;",
            connection, snapshot) { CommandTimeout = 0 })
        {
            await using var reader = await verify.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = (reader.GetInt32(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3));
                if (!observed.Add(row) || !signatures.Contains(row))
                    throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_SIGNATURE_DRIFT");
            }
        }
        if (!observed.SetEquals(signatures))
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_SIGNATURE_TRUNCATED");

        long expectedEdges;
        await using (var census = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM (
              SELECT DISTINCT a.obs_id AS src,b.obs_id AS dst
              FROM #v9_dev_silver_sig a
              JOIN #v9_dev_silver_sig b
                ON a.pass_id=b.pass_id AND a.signature=b.signature
               AND a.obs_id<b.obs_id AND a.gestor<>b.gestor
            ) c;
            """, connection, snapshot) { CommandTimeout = 0 })
            expectedEdges = Convert.ToInt64(await census.ExecuteScalarAsync(ct),
                CultureInfo.InvariantCulture);
        if (expectedEdges < 0 || expectedEdges > int.MaxValue)
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_UNBOUNDED");

        var edges = new List<(int A, int B)>();
        await using (var query = new SqlCommand("""
            SELECT DISTINCT a.obs_id,b.obs_id
            FROM #v9_dev_silver_sig a
            JOIN #v9_dev_silver_sig b
              ON a.pass_id=b.pass_id AND a.signature=b.signature
             AND a.obs_id<b.obs_id AND a.gestor<>b.gestor
            ORDER BY a.obs_id,b.obs_id;
            """, connection, snapshot) { CommandTimeout = 0 })
        {
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var a = reader.GetInt32(0);
                var b = reader.GetInt32(1);
                if (a < 1 || a >= b || b > frozen.Count
                    || string.Equals(
                        managers[frozen[a - 1].PessoaObservacaoId],
                        managers[frozen[b - 1].PessoaObservacaoId],
                        StringComparison.Ordinal)
                    || edges.Count >= expectedEdges
                    || (edges.Count > 0 && (edges[^1].A > a
                        || (edges[^1].A == a && edges[^1].B >= b))))
                    throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_EDGE_INVALID");
                edges.Add((a, b));
            }
        }
        if (edges.Count != expectedEdges)
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_EDGE_TRUNCATED");
        var canonical = string.Concat(edges.Select(x =>
            x.A.ToString(CultureInfo.InvariantCulture) + ":" +
            x.B.ToString(CultureInfo.InvariantCulture) + "\n"));
        return new Receipt(edges.AsReadOnly(), signatures.Count,
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
    }
}

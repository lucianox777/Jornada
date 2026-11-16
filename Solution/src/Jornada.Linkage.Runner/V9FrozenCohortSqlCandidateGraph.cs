using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Disposable-only physical SQL signature join over the entire frozen Silver
/// cohort. Uses the exact frozen V9 C# blocking planner and canonical
/// passId + U+001E + joined U+001F values hash, then a SQL DISTINCT join.
/// Not the productive identidade.blocking_chave SQL, Gold or TEST 100k.
/// </summary>
public static class V9FrozenCohortSqlCandidateGraph
{
    public const int MaximumObservations = 512;
    public const int MaximumSignatures = 8192;
    public const int MaximumPairIncidences = 25_000;

    public sealed record Receipt(
        IReadOnlyList<(int A, int B)> Edges,
        int SignatureRows,
        string CanonicalEdgesSha256,
        bool ProductionCandidateParityCertified = false);

    private sealed record Signature(int Observation, string Manager, string Pass, string Hash);

    public static async Task<Receipt> ReadAsync(SqlConnection connection,
        V9FittedSnapshot winner,
        IReadOnlyList<V9FrozenRunObservation> frozenRows,
        IReadOnlyDictionary<long, string> managers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(frozenRows);
        ArgumentNullException.ThrowIfNull(managers);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Censo físico V9 exige conexão SQL aberta.");
        await using (var db = new SqlCommand("SELECT DB_NAME();", connection))
            if (!string.Equals((string?)await db.ExecuteScalarAsync(ct), "JornadaHarness",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Grafo SQL V9 apenas no JornadaHarness descartável.");

        if (frozenRows.Count is < 2 or > MaximumObservations
            || managers.Count != frozenRows.Count
            || frozenRows.Any(row => row.Observation is null || row.Observation.Cpf is not null
                || row.PessoaObservacaoId <= 0
                || !managers.TryGetValue(row.PessoaObservacaoId, out var manager)
                || string.IsNullOrWhiteSpace(manager)))
            throw new InvalidDataException("Grafo SQL V9 exige coorte Silver completa e gestores válidos.");

        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        if (planner.MaximumPrefixLength is null
            || planner.ProjectionSchemaVersion != V9CalibrationBlockingProjection.SchemaVersion
            || planner.ProjectionFingerprintSha256 != winner.Payload.ProjectionFingerprintSha256)
            throw new InvalidDataException("Grafo SQL V9 exige a mesma projeção prefixada imutável.");

        var signatures = new List<Signature>();
        var unique = new HashSet<(int Obs, string Pass, string Sha)>();
        for (var i = 0; i < frozenRows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = frozenRows[i];
            var manager = managers[row.PessoaObservacaoId];
            foreach (var pass in planner.Plan(row.Observation))
            {
                if (pass.Clauses.Any(clause => clause.Values.Count == 0))
                    throw new InvalidDataException("Plano de blocking possui cláusula vazia.");
                var values = new string[pass.Clauses.Count];
                Enumerate(0);
                void Enumerate(int index)
                {
                    if (index == values.Length)
                    {
                        var canonical = pass.PassId + "\u001e" + string.Join("\u001f", values);
                        var hash = Convert.ToHexString(SHA256.HashData(
                            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                        if (!unique.Add((i + 1, pass.PassId, hash)))
                            return;
                        if (signatures.Count >= MaximumSignatures)
                            throw new InvalidDataException("Excesso de assinaturas V9: nunca truncar.");
                        signatures.Add(new Signature(i + 1, manager, pass.PassId, hash));
                        return;
                    }
                    foreach (var value in pass.Clauses[index].Values)
                    {
                        values[index] = value;
                        Enumerate(index + 1);
                    }
                }
            }
        }

        // Count ALL cross-manager incidences per signature without a quadratic
        // nested pair scan. For each bucket, C(n,2) - sum(C(n_manager,2))
        // counts precisely the cross-manager pairs, including repeated pairs
        // across distinct passes. Never confuse incidences with DISTINCT edges.
        long incidences = 0;
        foreach (var group in signatures.GroupBy(row => (row.Pass, row.Hash)))
        {
            ct.ThrowIfCancellationRequested();
            var n = (long)group.Count();
            var sameManager = group.GroupBy(row => row.Manager, StringComparer.Ordinal)
                .Sum(managerRows =>
                {
                    var count = (long)managerRows.Count();
                    return checked(count * (count - 1) / 2);
                });
            var crossManager = checked(n * (n - 1) / 2 - sameManager);
            incidences = checked(incidences + crossManager);
            if (incidences > MaximumPairIncidences)
                throw new InvalidDataException("Censo pré-SQL de pares V9 excedeu o limite.");
        }

        // Tables are LOCAL SQL temp tables scoped to this disposable connection.
        // Never INSERT/UPDATE any operational, Gold or Serving table.
        await using (var setup = new SqlCommand("""
            IF OBJECT_ID('tempdb..#v9_run_sig') IS NOT NULL DROP TABLE #v9_run_sig;
            CREATE TABLE #v9_run_sig(
                obs_id int NOT NULL,
                gestor nvarchar(120) NOT NULL,
                pass_id nvarchar(120) NOT NULL,
                assinatura char(64) NOT NULL
            );
            CREATE INDEX IX_v9_run_sig ON #v9_run_sig(pass_id,assinatura,obs_id);
            """, connection))
            await setup.ExecuteNonQueryAsync(ct);

        // Keep SQL bulk transfer bounded independently of the total signature
        // count. A SqlBulkCopy.BatchSize alone does NOT bound the DataTable
        // retained in client memory.
        const int SignatureTransferBatchSize = 2048;
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("gestor", typeof(string));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("assinatura", typeof(string));
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = "#v9_run_sig",
            BatchSize = SignatureTransferBatchSize,
            BulkCopyTimeout = 60
        })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            foreach (var row in signatures)
            {
                ct.ThrowIfCancellationRequested();
                table.Rows.Add(row.Observation, row.Manager, row.Pass, row.Hash);
                if (table.Rows.Count < SignatureTransferBatchSize)
                    continue;
                await bulk.WriteToServerAsync(table, ct);
                table.Clear();
            }
            if (table.Rows.Count > 0)
                await bulk.WriteToServerAsync(table, ct);
        }

        // Preflight the SQL DISTINCT result itself before transferring any
        // edges to the client. Signature incidence counts can exceed DISTINCT
        // counts, but neither is a substitute for a hard output bound.
        // COUNT_BIG is evaluated by SQL over the entire frozen graph.
        await using (var countQuery = new SqlCommand("""
            SELECT COUNT_BIG(*)
              FROM (
                SELECT DISTINCT a.obs_id,b.obs_id
                  FROM #v9_run_sig a
                  JOIN #v9_run_sig b
                    ON a.pass_id=b.pass_id AND a.assinatura=b.assinatura
                   AND a.obs_id<b.obs_id AND a.gestor<>b.gestor
              ) AS candidate_edges;
            """, connection) { CommandTimeout = 120 })
        {
            var distinctEdgeCount = Convert.ToInt64(
                await countQuery.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (distinctEdgeCount > MaximumPairIncidences)
                throw new InvalidDataException(
                    "Grafo SQL V9 excedeu o limite antes da transferência de arestas.");
        }

        var edges = new List<(int A, int B)>();
        // Hash while reading the SQL-ordered DISTINCT stream. Retain edges
        // only for the bounded complete-link replay; never scan them twice.
        using var edgeDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var previousA = 0;
        var previousB = 0;
        await using (var query = new SqlCommand("""
            SELECT DISTINCT a.obs_id,b.obs_id
              FROM #v9_run_sig a
              JOIN #v9_run_sig b
                ON a.pass_id=b.pass_id AND a.assinatura=b.assinatura
               AND a.obs_id<b.obs_id AND a.gestor<>b.gestor
             ORDER BY a.obs_id,b.obs_id;
            """, connection) { CommandTimeout = 120 })
        {
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (edges.Count >= MaximumPairIncidences)
                    throw new InvalidDataException("União SQL V9 excedeu a capacidade: não truncar.");
                var a = reader.GetInt32(0);
                var b = reader.GetInt32(1);
                if (a < 1 || a >= b || b > frozenRows.Count)
                    throw new InvalidDataException("Aresta física V9 fora da coorte congelada.");
                if (a < previousA || (a == previousA && b <= previousB))
                    throw new InvalidDataException("Grafo SQL V9 não está estritamente ordenado e distinto.");
                previousA = a;
                previousB = b;
                var line = a.ToString(CultureInfo.InvariantCulture) + ":" +
                    b.ToString(CultureInfo.InvariantCulture) + "\n";
                edgeDigest.AppendData(Encoding.UTF8.GetBytes(line));
                edges.Add((a, b));
            }
        }
        var digest = Convert.ToHexString(edgeDigest.GetHashAndReset()).ToLowerInvariant();
        return new(edges.AsReadOnly(), signatures.Count, digest,
            ProductionCandidateParityCertified: false);
    }
}

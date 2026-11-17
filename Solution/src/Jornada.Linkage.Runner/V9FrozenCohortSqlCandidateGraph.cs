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
        bool ProductionCandidateParityCertified = false,
        string CanonicalSignaturesSha256 = "");

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

        // SQL uses nvarchar(120) for both manager and pass identifiers.
        // Reject lossy SQL normalization/truncation before materializing any row.
        // Trailing U+0020 is ignored by SQL Server string equality and would
        // make the operational join differ from the ordinal C# identity.
        foreach (var manager in managers.Values)
            if (manager.Length > 120 || manager.EndsWith(' '))
                throw new InvalidDataException(
                    "Identificador de gestor V9 incompatível com nvarchar(120)/comparação SQL.");

        var signatures = new List<Signature>();
        var unique = new HashSet<(int Obs, string Pass, string Sha)>();
        for (var i = 0; i < frozenRows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = frozenRows[i];
            var manager = managers[row.PessoaObservacaoId];
            foreach (var pass in planner.Plan(row.Observation))
            {
                if (string.IsNullOrWhiteSpace(pass.PassId) ||
                    pass.PassId.Length > 120 || pass.PassId.EndsWith(' '))
                    throw new InvalidDataException(
                        "Identificador de passe V9 incompatível com nvarchar(120)/comparação SQL.");
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

        // Count ALL candidate incidences before any SQL materialization.
        long incidences = 0;
        foreach (var group in signatures.GroupBy(row => (row.Pass, row.Hash)))
        {
            var groupRows = group.ToArray();
            for (var i = 0; i < groupRows.Length; i++)
                for (var j = i + 1; j < groupRows.Length; j++)
                    if (!string.Equals(groupRows[i].Manager, groupRows[j].Manager,
                            StringComparison.Ordinal))
                        if (++incidences > MaximumPairIncidences)
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
            CREATE UNIQUE INDEX UX_v9_run_sig ON #v9_run_sig(obs_id,pass_id,assinatura);
            CREATE INDEX IX_v9_run_sig_join ON #v9_run_sig(pass_id,assinatura,obs_id);
            """, connection))
            await setup.ExecuteNonQueryAsync(ct);

        var table = new DataTable();
        table.Locale = CultureInfo.InvariantCulture;
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("gestor", typeof(string));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("assinatura", typeof(string));
        foreach (var row in signatures)
            table.Rows.Add(row.Observation, row.Manager, row.Pass, row.Hash);
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = "#v9_run_sig",
            BatchSize = 2048,
            BulkCopyTimeout = 60
        })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        // SqlBulkCopy may return without a complete logical signature universe.
        // A missing signature can silently remove candidate edges, so fail closed.
        long materializedSignatures;
        await using (var signatureCensus = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM #v9_run_sig;", connection) { CommandTimeout = 120 })
            materializedSignatures = Convert.ToInt64(
                await signatureCensus.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (materializedSignatures != signatures.Count)
            throw new InvalidDataException(
                "Assinaturas SQL V9 incompletas: censo materializado divergiu do universo planejado.");

        // A matching row count alone cannot detect substitutions or stale
        // signatures. Verify the complete materialized signature set, including
        // manager and pass, before deriving any candidate edge.
        var expectedSignatures = new HashSet<(int Obs, string Manager, string Pass, string Hash)>(
            signatures.Select(s => (s.Observation, s.Manager, s.Pass, s.Hash)));
        var observedSignatures = new HashSet<(int Obs, string Manager, string Pass, string Hash)>();
        await using (var verify = new SqlCommand(
            "SELECT obs_id,gestor,pass_id,assinatura FROM #v9_run_sig;", connection)
            { CommandTimeout = 120 })
        {
            await using var reader = await verify.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var signature = (reader.GetInt32(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3));
                if (!observedSignatures.Add(signature)
                    || !expectedSignatures.Contains(signature))
                    throw new InvalidDataException(
                        "Manifesto SQL V9 contém assinatura duplicada ou adulterada.");
            }
        }
        if (!expectedSignatures.SetEquals(observedSignatures))
            throw new InvalidDataException(
                "Manifesto SQL V9 incompleto: assinaturas divergem do plano congelado.");

        // Deterministic signature fingerprint for run/cohort evidence. Only
        // verified SQL rows contribute; no personal names or raw values.
        var canonicalSignatures = string.Concat(observedSignatures
            .OrderBy(s => s.Obs).ThenBy(s => s.Manager, StringComparer.Ordinal)
            .ThenBy(s => s.Pass, StringComparer.Ordinal)
            .ThenBy(s => s.Hash, StringComparer.Ordinal)
            .Select(s => s.Obs.ToString(CultureInfo.InvariantCulture) + ":" +
                s.Manager.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                s.Manager + ":" + s.Pass + ":" + s.Hash + "\n"));
        var signaturesDigest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonicalSignatures))).ToLowerInvariant();

        // Reconcile an independent SQL census against every streamed edge.
        // Never accept a silently incomplete graph.
        long expectedEdges;
        await using (var census = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM (
                SELECT DISTINCT a.obs_id,b.obs_id
                  FROM #v9_run_sig a JOIN #v9_run_sig b
                    ON a.pass_id=b.pass_id AND a.assinatura=b.assinatura
                   AND a.obs_id<b.obs_id AND a.gestor<>b.gestor
            ) complete_graph;
            """, connection) { CommandTimeout = 120 })
            expectedEdges = Convert.ToInt64(
                await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (expectedEdges > MaximumPairIncidences)
            throw new InvalidDataException("Censo SQL V9 excedeu capacidade: grafo não pode ser truncado.");

        var edges = new List<(int A, int B)>();
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
                edges.Add((a, b));
            }
        }
        if (edges.Count != expectedEdges)
            throw new InvalidDataException("Leitura do grafo SQL V9 incompleta: contagem difere do censo.");
        for (var i = 1; i < edges.Count; i++)
            if (edges[i - 1].A > edges[i].A
                || (edges[i - 1].A == edges[i].A && edges[i - 1].B >= edges[i].B))
                throw new InvalidDataException("Grafo SQL V9 duplicado ou fora de ordem.");
        var canonicalEdges = string.Concat(edges.Select(edge =>
            edge.A.ToString(CultureInfo.InvariantCulture) + ":" +
            edge.B.ToString(CultureInfo.InvariantCulture) + "\n"));
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonicalEdges))).ToLowerInvariant();
        return new(edges.AsReadOnly(), signatures.Count, digest,
            ProductionCandidateParityCertified: false,
            CanonicalSignaturesSha256: signaturesDigest);
    }
}

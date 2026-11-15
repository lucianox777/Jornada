using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only operational query proof for a small, disposable, fully frozen V9
/// Silver cohort. Executes the UNMODIFIED production
/// BlockingProjectionCandidateQueryBuilder against identidade.blocking_chave.
/// Caller is responsible for separately seeding only diagnostic references in
/// JornadaHarness and for the independently built physical SQL graph.
/// This does NOT certify real Gold reference semantics or production publication.
/// </summary>
public static class V9OperationalBlockingChaveCohortParity
{
    public sealed record Receipt(
        int DirectedSqlEdges,
        int UndirectedSqlEdges,
        string CanonicalEdgesSha256,
        bool ProductionGoldCertified = false);

    public static async Task<Receipt> VerifyAsync(
        SqlConnection connection,
        V9FittedSnapshot winner,
        IReadOnlyList<V9FrozenRunObservation> frozenRows,
        IReadOnlyDictionary<long, string> managers,
        IReadOnlyList<(int A, int B)> expectedPhysicalSqlEdges,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(frozenRows);
        ArgumentNullException.ThrowIfNull(managers);
        ArgumentNullException.ThrowIfNull(expectedPhysicalSqlEdges);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Consulta operacional V9 requer conexão aberta.");
        await using (var check = new SqlCommand("SELECT DB_NAME();", connection))
            if (!string.Equals((string?)await check.ExecuteScalarAsync(ct),
                    "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Paridade blocking_chave V9 somente em JornadaHarness descartável.");

        if (frozenRows.Count is < 2 or > V9FrozenCohortSqlCandidateGraph.MaximumObservations
            || managers.Count != frozenRows.Count
            || frozenRows.Any(row => row.PessoaObservacaoId <= 0 || row.Observation.Cpf is not null
                || !managers.TryGetValue(row.PessoaObservacaoId, out var manager)
                || string.IsNullOrWhiteSpace(manager)))
            throw new InvalidDataException(
                "Paridade blocking_chave exige universo Silver integral com gestores.");

        var expected = new HashSet<(int A, int B)>();
        foreach (var edge in expectedPhysicalSqlEdges)
            if (edge.A < 1 || edge.A >= edge.B || edge.B > frozenRows.Count
                || !expected.Add(edge))
                throw new InvalidDataException("Grafo físico de referência não canônico.");

        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        if (planner.MaximumPrefixLength is null ||
            planner.ProjectionSchemaVersion != V9CalibrationBlockingProjection.SchemaVersion)
            throw new InvalidDataException("Modelo V9 não tem projeção operacional comprovável.");

        // The GUIDs are private diagnostic keys, never Gold identities in
        // production. One distinct UUID per frozen Silver row is mandatory;
        // never score by mutable names or by a cluster root UUID.
        var byUuid = frozenRows.Select((row, i) =>
                (Uuid: DiagnosticUuid(i + 1), Index: i + 1,
                    Manager: managers[row.PessoaObservacaoId]))
            .ToDictionary(x => x.Uuid);

        // Verify the complete physical projection manifest: exactly the 200
        // synthetic UUIDs and no duplicate or missing key. This rejects
        // truncated, tampered, or stale projection before any positive match.
        var seen = new HashSet<(Guid Id, string Feature, string Value)>();
        await using (var manifest = new SqlCommand("""
            SELECT pessoa_uuid,atributo,valor_normalizado,normalizacao_versao,
                   projection_schema_version,projection_fingerprint_sha256,
                   semantica_temporal,vigencia_fim
              FROM identidade.blocking_chave ORDER BY pessoa_uuid,atributo,valor_normalizado;
            """, connection) { CommandTimeout = 120 })
        {
            await using var reader = await manifest.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                var feature = reader.GetString(1);
                var value = reader.GetString(2);
                if (!byUuid.ContainsKey(uuid)
                    || reader.GetString(3) != IdentityComparison.NormalizationVersion
                    || reader.IsDBNull(4)
                    || reader.GetString(4) != planner.ProjectionSchemaVersion
                    || reader.IsDBNull(5)
                    || !string.Equals(reader.GetString(5),
                        planner.ProjectionFingerprintSha256, StringComparison.OrdinalIgnoreCase)
                    || reader.IsDBNull(6)
                    || reader.IsDBNull(7) == false
                    || !seen.Add((uuid, feature, value)))
                    throw new InvalidDataException(
                        "Manifesto blocking_chave V9 não é exclusivamente da coorte congelada.");
            }
        }
        var expectedManifest = new HashSet<(Guid Id, string Feature, string Value)>();
        for (var i = 0; i < frozenRows.Count; i++)
            foreach (var key in planner.Project(frozenRows[i].Observation))
                expectedManifest.Add((DiagnosticUuid(i + 1), key.Feature, key.Value));
        if (!expectedManifest.SetEquals(seen))
            throw new InvalidDataException(
                "Fingerprint, cobertura ou valores do manifesto blocking_chave V9 divergiram.");

        // The actual production INTERSECT/UNION query is executed for EVERY
        // source, in both directions. Inferred reverse edges are forbidden.
        var directions = new Dictionary<(int A, int B), byte>();
        for (var i = 0; i < frozenRows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = planner.BuildCandidateUuidQuery(cmd,
                frozenRows[i].Observation, planner.ProjectionSchemaVersion,
                planner.ProjectionFingerprintSha256);
            var fromSource = new HashSet<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var candidateUuid = reader.GetGuid(0);
                if (!fromSource.Add(candidateUuid))
                    throw new InvalidDataException("Consulta produtiva V9 duplicou candidato da fonte.");
                if (!byUuid.TryGetValue(candidateUuid, out var candidate))
                    throw new InvalidDataException(
                        "Consulta produtiva V9 alcançou UUID externo à coorte diagnóstica.");
                if (candidate.Index == i + 1
                    || string.Equals(candidate.Manager,
                        managers[frozenRows[i].PessoaObservacaoId], StringComparison.Ordinal))
                    continue;
                var a = Math.Min(i + 1, candidate.Index);
                var b = Math.Max(i + 1, candidate.Index);
                var edge = (A: a, B: b);
                var bit = (byte)(i + 1 == a ? 1 : 2);
                directions.TryGetValue(edge, out var prior);
                if ((prior & bit) != 0)
                    throw new InvalidDataException("Consulta V9 repetiu uma direção SQL.");
                directions[edge] = (byte)(prior | bit);
                if (directions.Count > V9FrozenCohortSqlCandidateGraph.MaximumPairIncidences)
                    throw new InvalidDataException("Consulta V9 excedeu teto sem truncamento.");
            }
        }
        if (directions.Values.Any(value => value != 3))
            throw new InvalidDataException(
                "Consulta produtiva V9 retornou aresta unilateral; não inferir simetria.");
        if (!expected.SetEquals(directions.Keys))
            throw new InvalidDataException(
                "Blocking_chave operacional V9 divergiu da união física SQL por assinatura.");

        var canonical = string.Concat(directions.Keys.OrderBy(x => x.A).ThenBy(x => x.B)
            .Select(x => $"{x.A}:{x.B}\n"));
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(directions.Count * 2, directions.Count, digest, ProductionGoldCertified: false);
    }

    public static Guid DiagnosticUuid(int observationIndex)
    {
        if (observationIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(observationIndex));
        return new Guid(observationIndex, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4);
    }
}

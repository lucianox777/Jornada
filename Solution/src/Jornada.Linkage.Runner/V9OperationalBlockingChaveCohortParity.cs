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
/// Verifies the actual Gold reference rows and their names/dates against the frozen
/// Silver universe before SQL batch persistence. This does NOT certify
/// production Gold lifecycle or scientific TEST 100k.
/// </summary>
public static class V9OperationalBlockingChaveCohortParity
{
    public sealed record Receipt(
        int DirectedSqlEdges,
        int UndirectedSqlEdges,
        string CanonicalEdgesSha256,
        int GoldReferenceRows,
        bool ProductionGoldCertified = false)
    {
        // Canonical, complete edges read through the operational candidate query.
        public IReadOnlyList<(int A, int B)> OperationalEdges { get; init; } = Array.Empty<(int A, int B)>();
    }

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
            || frozenRows.Any(row => row is null || row.PessoaObservacaoId <= 0
                || row.Observation is null || row.Observation.Cpf is not null
                || !managers.TryGetValue(row.PessoaObservacaoId, out var manager)
                || string.IsNullOrWhiteSpace(manager))
            || frozenRows.Select(row => row.PessoaObservacaoId).Distinct().Count() != frozenRows.Count)
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
                    || reader.GetString(6) !=
                        (planner.TemporalSemantics(feature) ==
                            BlockingFeatureTemporalSemantics.StableIdentityDatum
                            ? "STABLE_IDENTITY_DATUM" : "VERSIONED_ALIAS")
                    || !reader.IsDBNull(7)
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

        // A valid blocking key alone must never prove an operational match:
        // its Gold reference may have changed after keys were materialized.
        // Require exactly one live Gold reference for EVERY frozen observation,
        // with the same names and birth date. Any stale/missing/extra Gold row
        // is rejected BEFORE the first linkage_resultado batch is persisted.
        var goldCount = 0;
        await using (var gold = new SqlCommand("""
            SELECT pessoa_uuid,cpf,nome_completo,data_nascimento,nome_mae,
                   estado_identidade
              FROM gold.pessoa ORDER BY pessoa_uuid;
            """, connection) { CommandTimeout = 120 })
        {
            var found = new HashSet<Guid>();
            await using var reader = await gold.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                if (!found.Add(uuid) || !byUuid.TryGetValue(uuid, out var candidate)
                    || !reader.IsDBNull(1) || reader.IsDBNull(5)
                    || reader.GetString(5) != "REFERENCIA")
                    throw new InvalidDataException(
                        "Gold V9 contém UUID externo, duplicado, CPF ou referência inválida.");
                var frozen = frozenRows[candidate.Index - 1].Observation;
                if ((reader.IsDBNull(2) ? null : reader.GetString(2)) != frozen.NomeCompleto
                    || (reader.IsDBNull(3) ? (DateOnly?)null
                        : DateOnly.FromDateTime(reader.GetDateTime(3))) != frozen.DataNascimento
                    || (reader.IsDBNull(4) ? null : reader.GetString(4)) != frozen.NomeMae)
                    throw new InvalidDataException(
                        "Gold V9 divergiu do conteúdo Silver congelado; bloquear scoring com índice obsoleto.");
                goldCount++;
            }
        }
        if (goldCount != frozenRows.Count)
            throw new InvalidDataException("Manifesto Gold V9 incompleto para coorte congelada.");

        // The actual production INTERSECT/UNION query is executed for EVERY
        // source, in both directions. Inferred reverse edges are forbidden.
        var directions = new Dictionary<(int A, int B), byte>();
        for (var i = 0; i < frozenRows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;
            var candidateQuery = planner.BuildCandidateUuidQuery(cmd,
                frozenRows[i].Observation, planner.ProjectionSchemaVersion,
                planner.ProjectionFingerprintSha256);
            // Same production-shaped candidate-to-Gold read used by the
            // Runner; raw blocking UUID equality alone is not sufficient.
            cmd.CommandText = "SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae " +
                "FROM (" + candidateQuery + ") c JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid " +
                "WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;";
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
                var goldObservation = frozenRows[candidate.Index - 1].Observation;
                if ((reader.IsDBNull(1) ? null : reader.GetString(1)) != goldObservation.NomeCompleto
                    || (reader.IsDBNull(2) ? (DateOnly?)null
                        : DateOnly.FromDateTime(reader.GetDateTime(2))) != goldObservation.DataNascimento
                    || (reader.IsDBNull(3) ? null : reader.GetString(3)) != goldObservation.NomeMae)
                    throw new InvalidDataException("Consulta candidata SQL retornou conteúdo Gold adulterado.");
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
        return new(directions.Count * 2, directions.Count, digest, goldCount,
            ProductionGoldCertified: false)
        {
            OperationalEdges = Array.AsReadOnly(directions.Keys.OrderBy(x => x.A).ThenBy(x => x.B).ToArray())
        };
    }

    public static Guid DiagnosticUuid(int observationIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(observationIndex);
        return new Guid(observationIndex, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4);
    }
}

using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

public sealed record V9OperationalCandidateGraphParityReceipt(
    string WinnerSha256,
    string ProjectionSchemaVersion,
    string ProjectionFingerprintSha256,
    int ObservationCount,
    int PhysicalSqlEdgeCount,
    int OperationalSqlEdgeCount,
    string CanonicalEdgesSha256,
    bool ProductionBatchPipelineCertified = false);

/// <summary>
/// Read-only, small-cohort evidence for the actual production projection
/// candidate SQL (BlockingProjectionCandidateQueryBuilder) versus the Worker
/// physical TEST signature-join graph. Never executes on production databases,
/// never retrieves Gold, and never changes a model or an identity. This is
/// NOT an entire production batching/publication parity certification.
/// </summary>
public static class V9OperationalCandidateGraphParityAudit
{
    public const int MaximumAuditObservations = 128;

    public static async Task<V9OperationalCandidateGraphParityReceipt> CompareTestAsync(
        DbConnection connection,
        V9OperationalPinnedCohortRuntime operationalRuntime,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        IReadOnlyList<(int A, int B)> physicalSqlEdges,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(operationalRuntime);
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(physicalSqlEdges);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Prova SQL exige conexão aberta.");
        // The production blocking_chave table may contain real citizens and is
        // never queried by this private TEST-only probe.
        await using (var database = connection.CreateCommand())
        {
            database.CommandText = "SELECT DB_NAME();";
            if (!string.Equals((string?)await database.ExecuteScalarAsync(ct),
                    "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Auditoria de paridade V9 permitida apenas em SQL descartável JornadaHarness.");
        }

        if (test.Count is < 2 or > MaximumAuditObservations
            || test.Any(row => row is null || row.Partition != "TEST"
                || row.ObservationId <= 0 || string.IsNullOrWhiteSpace(row.Gestor))
            || test.Select(row => row.ObservationId).Distinct().Count() != test.Count)
            throw new InvalidDataException(
                "Auditoria TEST exige coorte sintética única e pequena, sem duplicidades.");

        var byGuid = test.ToDictionary(
            row => V9FrozenSourceResolveReplay.ObservationGuid(row.ObservationId));
        var byId = test.ToDictionary(row => row.ObservationId);
        var expected = new HashSet<(int A, int B)>();
        foreach (var edge in physicalSqlEdges)
        {
            if (edge.A >= edge.B || !byId.TryGetValue(edge.A, out var a)
                || !byId.TryGetValue(edge.B, out var b)
                || string.Equals(a.Gestor, b.Gestor, StringComparison.Ordinal)
                || !expected.Add(edge))
                throw new InvalidDataException(
                    "Grafo físico TEST inválido, repetido ou intragestor.");
        }

        var frozen = operationalRuntime.FrozenWinner;
        if (operationalRuntime.ModelId
                != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(frozen.ContentSha256)
            || operationalRuntime.WinnerSha256 != frozen.ContentSha256)
            throw new InvalidDataException("Modelo operacional não corresponde ao vencedor pinado.");
        var planner = V9FittedSnapshotExecutionRuntime.Load(
            frozen.SerializeUtf8(), frozen.ContentSha256);

        // A bitset distinguishes a->b from b->a. Both directions must come
        // from actual SQL; never repair, infer, sample or truncate missing edges.
        var actualDirections = new Dictionary<(int A, int B), byte>();
        foreach (var row in test.OrderBy(x => x.ObservationId))
        {
            ct.ThrowIfCancellationRequested();
            var observation = new IdentityObservation(
                null, "SEM_CPF", row.PersonName, row.BirthDate, row.MotherName);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 60;
            command.CommandText = planner.BuildCandidateUuidQuery(
                command, observation, planner.ProjectionSchemaVersion,
                planner.ProjectionFingerprintSha256);
            var perSource = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                if (!perSource.Add(uuid))
                    throw new InvalidDataException(
                        "SQL operacional retornou candidato duplicado por fonte.");
                if (!byGuid.TryGetValue(uuid, out var candidate))
                    throw new InvalidDataException(
                        "SQL operacional retornou pessoa fora da coorte TEST pinada.");
                if (candidate.ObservationId == row.ObservationId
                    || string.Equals(candidate.Gestor, row.Gestor, StringComparison.Ordinal))
                    continue;

                var a = Math.Min(row.ObservationId, candidate.ObservationId);
                var b = Math.Max(row.ObservationId, candidate.ObservationId);
                var key = (a, b);
                var direction = (byte)(row.ObservationId == a ? 1 : 2);
                actualDirections.TryGetValue(key, out var prior);
                if ((prior & direction) != 0)
                    throw new InvalidDataException("SQL operacional repetiu a mesma direção.");
                actualDirections[key] = (byte)(prior | direction);
            }
        }
        if (actualDirections.Values.Any(v => v != 3))
            throw new InvalidDataException(
                "Grafo SQL operacional não é simétrico; aresta unilateral não pode ser aceita.");
        var actual = actualDirections.Keys.ToHashSet();
        if (!expected.SetEquals(actual))
            throw new InvalidDataException(
                $"Paridade de candidatos V9 falhou: físico={expected.Count}, operacional={actual.Count}; diferença simétrica de {expected.Except(actual).Count() + actual.Except(expected).Count()} arestas.");

        var canonical = string.Concat(actual.OrderBy(e => e.A).ThenBy(e => e.B)
            .Select(e => FormattableString.Invariant($"{e.A}:{e.B}\n")));
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(operationalRuntime.WinnerSha256, planner.ProjectionSchemaVersion,
            planner.ProjectionFingerprintSha256, test.Count, expected.Count, actual.Count,
            digest, ProductionBatchPipelineCertified: false);
    }
}

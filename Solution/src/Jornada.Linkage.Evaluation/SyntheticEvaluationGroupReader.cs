using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public sealed record SyntheticEvaluationGroupDispersion(
    string Scope,
    string? Dimension,
    string Metric,
    string Unit,
    int SampleCount,
    decimal Minimum,
    decimal Mean,
    decimal Maximum,
    decimal PopulationStandardDeviation);

public sealed record SyntheticEvaluationGroupReport(
    Guid RunGroupId,
    string Status,
    IReadOnlyList<ulong> ExpectedSeeds,
    IReadOnlyList<ulong> CompletedSeeds,
    IReadOnlyList<ulong> MissingSeeds,
    IReadOnlyList<ulong> UnexpectedSeeds,
    IReadOnlyList<ulong> DuplicateSeeds,
    IReadOnlyList<SyntheticEvaluationGroupDispersion> Dispersion);

public sealed class SyntheticEvaluationGroupReader(
    SqlConnection connection,
    int commandTimeoutSeconds)
{
    public async Task<SyntheticEvaluationGroupReport> ReadAsync(
        Guid runGroupId,
        CancellationToken cancellationToken = default)
    {
        if (runGroupId == Guid.Empty)
            throw new ArgumentException("RunGroupId obrigatório.", nameof(runGroupId));

        var evaluations = await ReadEvaluationsAsync(runGroupId, cancellationToken);
        if (evaluations.Count == 0)
        {
            return new SyntheticEvaluationGroupReport(
                runGroupId,
                "INCOMPLETO",
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<SyntheticEvaluationGroupDispersion>());
        }

        var expectedByEvaluation = await ReadExpectedSeedsAsync(runGroupId, cancellationToken);
        var firstExpected = expectedByEvaluation.TryGetValue(evaluations[0].EvaluationId, out var first)
            ? first
            : new SortedSet<ulong>();

        var membershipConsistent =
            firstExpected.Count > 0 &&
            evaluations.All(item =>
                expectedByEvaluation.TryGetValue(item.EvaluationId, out var expected) &&
                expected.SetEquals(firstExpected));

        var completed = evaluations
            .Select(static x => x.Seed)
            .Distinct()
            .Order()
            .ToArray();
        var duplicates = evaluations
            .GroupBy(static x => x.Seed)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .Order()
            .ToArray();
        var expectedSeeds = firstExpected.Order().ToArray();
        var missing = expectedSeeds.Except(completed).Order().ToArray();
        var unexpected = completed.Except(expectedSeeds).Order().ToArray();

        var complete =
            membershipConsistent &&
            duplicates.Length == 0 &&
            unexpected.Length == 0 &&
            missing.Length == 0 &&
            completed.Length == expectedSeeds.Length;

        var status = complete
            ? "CONCLUIDO"
            : membershipConsistent ? "INCOMPLETO" : "INCONSISTENTE";
        var dispersion = complete
            ? await ReadDispersionAsync(runGroupId, expectedSeeds.Length, cancellationToken)
            : Array.Empty<SyntheticEvaluationGroupDispersion>();

        return new SyntheticEvaluationGroupReport(
            runGroupId,
            status,
            expectedSeeds,
            completed,
            missing,
            unexpected,
            duplicates,
            dispersion);
    }

    private async Task<IReadOnlyList<EvaluationSeed>> ReadEvaluationsAsync(
        Guid runGroupId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT avaliacao_id,gerador_seed
            FROM auditoria.v_linkage_avaliacao_sintetica
            WHERE grupo_execucao_id=@grupo_execucao_id
              AND status=N'CONCLUIDA'
            ORDER BY linkage_avaliacao_sintetica_id;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@grupo_execucao_id", System.Data.SqlDbType.UniqueIdentifier).Value = runGroupId;

        var result = new List<EvaluationSeed>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var seedDecimal = reader.GetDecimal(1);
            if (seedDecimal != decimal.Truncate(seedDecimal)
                || seedDecimal < 0m
                || seedDecimal > ulong.MaxValue)
            {
                throw new InvalidDataException("Ledger sintético contém gerador_seed inválido.");
            }
            result.Add(new EvaluationSeed(reader.GetGuid(0), decimal.ToUInt64(seedDecimal)));
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, SortedSet<ulong>>> ReadExpectedSeedsAsync(
        Guid runGroupId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT e.avaliacao_id,m.dimensao
            FROM auditoria.v_linkage_avaliacao_sintetica e
            JOIN auditoria.v_linkage_avaliacao_sintetica_metrica m
              ON m.avaliacao_id=e.avaliacao_id
            WHERE e.grupo_execucao_id=@grupo_execucao_id
              AND m.escopo=N'MULTI_SEED_EXPECTED'
              AND m.metrica=N'EXPECTED'
              AND m.valor=1
            ORDER BY e.linkage_avaliacao_sintetica_id,m.dimensao;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@grupo_execucao_id", System.Data.SqlDbType.UniqueIdentifier).Value = runGroupId;

        var result = new Dictionary<Guid, SortedSet<ulong>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var raw = reader.GetString(1);
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                throw new InvalidDataException("Ledger sintético contém dimensão de seed inválida.");
            if (!result.TryGetValue(id, out var set))
            {
                set = [];
                result.Add(id, set);
            }
            set.Add(seed);
        }
        return result;
    }

    private async Task<SyntheticEvaluationGroupDispersion[]> ReadDispersionAsync(
        Guid runGroupId,
        int expectedSamples,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT m.escopo,m.dimensao,m.metrica,m.unidade,m.valor
            FROM auditoria.v_linkage_avaliacao_sintetica e
            JOIN auditoria.v_linkage_avaliacao_sintetica_metrica m
              ON m.avaliacao_id=e.avaliacao_id
            WHERE e.grupo_execucao_id=@grupo_execucao_id
              AND m.escopo<>N'MULTI_SEED_EXPECTED'
            ORDER BY m.escopo,m.dimensao,m.metrica,e.gerador_seed;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@grupo_execucao_id", System.Data.SqlDbType.UniqueIdentifier).Value = runGroupId;

        var values = new Dictionary<MetricKey, List<decimal>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new MetricKey(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
            if (!values.TryGetValue(key, out var list))
            {
                list = [];
                values.Add(key, list);
            }
            list.Add(reader.GetDecimal(4));
        }

        return values
            .Where(pair => pair.Value.Count == expectedSamples)
            .OrderBy(static pair => pair.Key.Scope, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Dimension, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Metric, StringComparer.Ordinal)
            .Select(pair =>
            {
                var mean = pair.Value.Average();
                var variance = pair.Value
                    .Select(value =>
                    {
                        var delta = (double)(value - mean);
                        return delta * delta;
                    })
                    .Average();
                return new SyntheticEvaluationGroupDispersion(
                    pair.Key.Scope,
                    pair.Key.Dimension,
                    pair.Key.Metric,
                    pair.Key.Unit,
                    pair.Value.Count,
                    pair.Value.Min(),
                    mean,
                    pair.Value.Max(),
                    (decimal)Math.Sqrt(variance));
            })
            .ToArray();
    }

    private sealed record EvaluationSeed(Guid EvaluationId, ulong Seed);
    private sealed record MetricKey(string Scope, string? Dimension, string Metric, string Unit);
}

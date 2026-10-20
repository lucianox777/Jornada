using System.Diagnostics;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9FrozenHoldoutEvaluation(string SnapshotSha256, V9PhysicalSourceResolveMetric Metric,
    double? P95Milliseconds, double FalseDegreeP99, string LatencyProtocol,
    IReadOnlyList<double> SamplesMilliseconds, bool Refitted = false);

public static partial class V9PhysicalSqlAdaptiveCalibrationProbe
{
    /// <summary>Only physical retrieval and shared frozen scorer/Resolve. NEVER fits m/u.</summary>
    public static async Task<V9FrozenHoldoutEvaluation> EvaluateFrozenHoldoutAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> validation, V9FittedSnapshot snapshot,
        V9PhysicalAdaptiveExecution execution, int maximumAcceptedPerIdentity, int comparedConfigurations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(execution);
        execution.Validate(true, true);
        if (maximumAcceptedPerIdentity < 1 || comparedConfigurations < 1 || comparedConfigurations > 3
            || validation.Count < 4 || validation.Any(x => x is null || x.Partition != "VALIDATION"
                || x.ObservationId <= 0 || string.IsNullOrWhiteSpace(x.BasePersonId) || string.IsNullOrWhiteSpace(x.Gestor))
            || validation.Select(x => x.ObservationId).Distinct().Count() != validation.Count
            || validation.GroupBy(x => x.BasePersonId, StringComparer.Ordinal).Any(g => g.Count() > maximumAcceptedPerIdentity))
            throw new InvalidDataException("Avaliação congelada exige VALIDATION válida, cap e até três finalistas.");
        var expectedProjection = execution.MaximumPrefixLength == 0 ? PersonResolutionProjectionContract.FingerprintSha256
            : V9CalibrationBlockingProjection.ComputeSchemaFingerprint(execution.MaximumPrefixLength);
        if (snapshot.Payload.ProjectionFingerprintSha256 != expectedProjection || snapshot.Payload.TfWeight != 0)
            throw new InvalidDataException("Snapshot, projeção e TF0 do protocolo devem coincidir.");
        await RequireHarnessAsync(connection, cancellationToken);
        var byId = validation.ToDictionary(x => x.ObservationId);
        var eligible = validation.GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Gestor).Distinct(StringComparer.Ordinal).Skip(1).Any()).Sum(g => (long)g.Count());
        if (eligible == 0) throw new InvalidDataException("Holdout sem fontes elegíveis pré-blocking.");
        var samples = new List<double>();
        V9PhysicalSourceResolveMetric? metric = null;
        string? outcome = null;
        double fanout = 0;
        for (var iteration = 0; iteration < Math.Max(1, execution.EndToEndRepetitions); iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            var signatures = BuildSignatures(validation, snapshot.Payload.BlockingPasses,
                execution.MaximumPrefixLength, execution.MaximumProjectedSignatures);
            if (CountPairIncidences(signatures, validation, execution.MaximumCandidatePairIncidences) > execution.MaximumCandidatePairIncidences)
                throw new InvalidDataException("Holdout excede recursos pré-declarados; não truncar candidatos.");
            await CreateTablesAsync(connection, cancellationToken);
            await BulkObservationsAsync(connection, validation, cancellationToken);
            await BulkSignaturesAsync(connection, signatures, cancellationToken);
            var pairCount = await MaterializePairsAsync(connection, cancellationToken);
            var edges = await ReadValidationPairsAsync(connection, cancellationToken);
            if (pairCount != edges.Count) throw new InvalidDataException("Censo do holdout não reconciliou.");
            var trueSources = edges.Where(e => byId[e.A].BasePersonId == byId[e.B].BasePersonId)
                .SelectMany(e => new[] { e.A, e.B }).Distinct().LongCount();
            var replay = V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges);
            var baseline = new V9PhysicalSourceResolveMetric(snapshot.ContentSha256, snapshot.Payload.Threshold, 0,
                snapshot.Payload.ConflictMargin, "BLIND_FS_COMPLETE_LINK_CANDIDATES", validation.Count,
                eligible, trueSources, 0, 0, 0, validation.Count, (double)trueSources / eligible,
                0, null, null, validation.Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal).Count(),
                new Dictionary<string, long>(), false, false, 0,
                BonferroniComparedConfigurations: comparedConfigurations);
            metric = SummarizeFrozenReplay(replay, validation, baseline, maximumAcceptedPerIdentity, includeIdentityOutcomes: true);
            samples.Add(timer.Elapsed.TotalMilliseconds);
            var currentOutcome = OutcomeSha(metric);
            if (outcome is not null && currentOutcome != outcome)
                throw new InvalidDataException("Replay do snapshot congelado mudou entre repetições.");
            outcome = currentOutcome;
            if (iteration == 0)
            {
                var degrees = validation.ToDictionary(x => x.ObservationId, _ => 0);
                foreach (var (a, b) in edges)
                    if (byId[a].BasePersonId != byId[b].BasePersonId) { degrees[a]++; degrees[b]++; }
                var ordered = degrees.Values.OrderBy(x => x).ToArray();
                fanout = ordered[Math.Max(0, (int)Math.Ceiling(.99 * ordered.Length) - 1)];
            }
        }
        return new(snapshot.ContentSha256, metric!, execution.EndToEndRepetitions >= 20
            ? V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(samples).P95Milliseconds : null,
            fanout, "V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1", samples.AsReadOnly());
    }
}

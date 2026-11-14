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
        => await EvaluateFrozenPartitionAsync(connection, validation, snapshot, execution,
            maximumAcceptedPerIdentity, comparedConfigurations, "VALIDATION", cancellationToken);

    /// <summary>Independent TEST: one frozen model, one sealed TEST cohort; no fitting or selection.</summary>
    public static async Task<V9FrozenHoldoutEvaluation> EvaluateFrozenTestAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test, V9FittedSnapshot winner,
        V9OperationalPinnedCohortRuntime operationalRuntime,
        V9PhysicalAdaptiveExecution execution, int maximumAcceptedPerIdentity,
        CancellationToken cancellationToken)
        => await EvaluateFrozenPartitionAsync(connection, test, winner, execution,
            maximumAcceptedPerIdentity, 1, "TEST", cancellationToken, operationalRuntime);

    private static async Task<V9FrozenHoldoutEvaluation> EvaluateFrozenPartitionAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> validation, V9FittedSnapshot snapshot,
        V9PhysicalAdaptiveExecution execution, int maximumAcceptedPerIdentity,
        int comparedConfigurations, string purpose, CancellationToken cancellationToken,
        V9OperationalPinnedCohortRuntime? operationalRuntime = null)
    {
        if (purpose is not ("VALIDATION" or "TEST"))
            throw new InvalidDataException("Finalidade de avaliação inválida.");
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(execution);
        // TEST always executes the SQL-pinned operational Runner cohort kernel.
        // VALIDATION keeps the original replay path without any runtime change.
        if (purpose == "TEST" && (operationalRuntime is null
            || operationalRuntime.WinnerSha256 != snapshot.ContentSha256
            || operationalRuntime.ModelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(snapshot.ContentSha256)
            || !operationalRuntime.FrozenWinner.SerializeUtf8().AsSpan().SequenceEqual(snapshot.SerializeUtf8())))
            throw new InvalidDataException("TEST exige runtime operacional SQL do vencedor único sem divergência.");
        if (purpose == "VALIDATION" && operationalRuntime is not null)
            throw new InvalidDataException("Runtime de TEST nunca é injetado na VALIDATION.");
        execution.Validate(true, true);
        if (maximumAcceptedPerIdentity < 1 || comparedConfigurations < 1 || comparedConfigurations > 3
            || validation.Count < 4 || validation.Any(x => x is null || x.Partition != purpose
                || x.ObservationId <= 0 || string.IsNullOrWhiteSpace(x.BasePersonId) || string.IsNullOrWhiteSpace(x.Gestor))
            || validation.Select(x => x.ObservationId).Distinct().Count() != validation.Count
            || validation.GroupBy(x => x.BasePersonId, StringComparer.Ordinal).Any(g => g.Count() > maximumAcceptedPerIdentity))
            throw new InvalidDataException("Avaliação congelada exige coorte da finalidade, cap e 1..3 modelos na VALIDATION/1 no TEST.");
        var expectedProjection = execution.MaximumPrefixLength == 0 ? PersonResolutionProjectionContract.FingerprintSha256
            : V9CalibrationBlockingProjection.ComputeSchemaFingerprint(execution.MaximumPrefixLength);
        // Frozen replay uses the snapshot's embedded TRAIN-fitted U and pinned
        // published IBGE frequencies for TF-positive options. No reference SQL
        // lookup, re-fit, or implicit TF0 substitution is permitted here.
        if (snapshot.Payload.ProjectionFingerprintSha256 != expectedProjection)
            throw new InvalidDataException("Snapshot e projeção do protocolo devem coincidir.");
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
            var edges = await PrepareFrozenValidationPairsAsync(
                connection, validation, snapshot, execution, purpose, cancellationToken);
            var trueSources = edges.Where(e => byId[e.A].BasePersonId == byId[e.B].BasePersonId)
                .SelectMany(e => new[] { e.A, e.B }).Distinct().LongCount();
            // Physical SQL pairCount == edges.Count has already been checked
            // inside PrepareFrozenValidationPairsAsync in the same iteration.
            var replay = purpose == "TEST"
                ? operationalRuntime!.ReplayTest(validation, edges)
                : V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges);
            var baseline = new V9PhysicalSourceResolveMetric(snapshot.ContentSha256, snapshot.Payload.Threshold,
                snapshot.Payload.TfWeight,
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
            fanout, purpose == "TEST"
            ? "V9_INDEPENDENT_TEST_ONE_FROZEN_MODEL_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1"
            : "V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1", samples.AsReadOnly());
    }
    /// <summary>
    /// TEST-only, small synthetic SQL parity fixture. This invokes the existing
    /// physical Worker signature/candidate SQL unchanged, without scoring, refit,
    /// reading TEST truth, or touching TRAIN/VALIDATION paths. Not a 100k test.
    /// </summary>
    public static async Task<IReadOnlyList<(int A, int B)>> ReadFrozenTestCandidateEdgesForParityAsync(
        SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        V9FittedSnapshot winner,
        V9PhysicalAdaptiveExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(execution);
        if (test.Count is < 2 or > V9OperationalCandidateGraphParityAudit.MaximumAuditObservations
            || test.Any(row => row.Partition != "TEST" || row.ObservationId <= 0)
            || test.Select(row => row.ObservationId).Distinct().Count() != test.Count)
            throw new InvalidDataException("Prova física de candidatos exige coorte TEST sintética pequena.");
        execution.Validate(true, true);
        var fingerprint = execution.MaximumPrefixLength == 0
            ? PersonResolutionProjectionContract.FingerprintSha256
            : V9CalibrationBlockingProjection.ComputeSchemaFingerprint(execution.MaximumPrefixLength);
        if (winner.Payload.ProjectionFingerprintSha256 != fingerprint)
            throw new InvalidDataException("Prova física exige projeção exata do vencedor.");
        await RequireHarnessAsync(connection, cancellationToken);
        return await PrepareFrozenValidationPairsAsync(
            connection, test, winner, execution, "TEST", cancellationToken);
    }

    // Extraction only: keep the full physical SQL preparation inside the timed
    // replay iteration. Do not cache or bypass this path for certified P95.
    private static async Task<IReadOnlyList<(int A, int B)>> PrepareFrozenValidationPairsAsync(
        SqlConnection connection, IReadOnlyCollection<V9PhysicalCalibrationObservation> validation,
        V9FittedSnapshot snapshot, V9PhysicalAdaptiveExecution execution,
        string purpose, CancellationToken cancellationToken)
    {
        var signatures = BuildSignatures(validation, snapshot.Payload.BlockingPasses,
            execution.MaximumPrefixLength, execution.MaximumProjectedSignatures);
        if (CountPairIncidences(signatures, validation, execution.MaximumCandidatePairIncidences)
            > execution.MaximumCandidatePairIncidences)
            throw new InvalidDataException("Holdout excede recursos pré-declarados; não truncar candidatos.");
        await CreateTablesAsync(connection, cancellationToken);
        await BulkObservationsAsync(connection, validation, cancellationToken);
        await BulkSignaturesAsync(connection, signatures, cancellationToken);
        var pairCount = await MaterializePairsAsync(connection, cancellationToken);
        var edges = await ReadValidationPairsAsync(connection, cancellationToken, purpose);
        if (pairCount != edges.Count)
            throw new InvalidDataException("Censo do holdout não reconciliou.");
        return edges;
    }

}

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9FrozenHoldoutEvaluation(string SnapshotSha256, V9PhysicalSourceResolveMetric Metric,
    double? P95Milliseconds, double FalseDegreeP99, string LatencyProtocol,
    IReadOnlyList<double> SamplesMilliseconds, bool Refitted = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public V9FrozenTestReplayEvidence? TestReplayEvidence { get; init; }
}

public sealed record V9FrozenTestReplayEvidence(int DistinctCandidatePairs, string CandidateEdgesSha256,
    string ObservationDecisionsSha256);

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
        V9FrozenTestReplayEvidence? testReplayEvidence = null;
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
            if (purpose == "TEST")
            {
                var currentEvidence = BuildTestReplayEvidence(edges, replay);
                if (testReplayEvidence is not null && testReplayEvidence != currentEvidence)
                    throw new InvalidDataException("Grafo ou decisões individuais TEST divergiram entre repetições.");
                testReplayEvidence = currentEvidence;
            }
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
            : "V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1", samples.AsReadOnly())
        { TestReplayEvidence = testReplayEvidence };
    }

    private static V9FrozenTestReplayEvidence BuildTestReplayEvidence(
        IReadOnlyList<(int A, int B)> edges, V9FrozenSourceReplayResult replay)
    {
        // SQL returns edges ordered by (a,b). Fixed-width encoding avoids ambiguity.
        using var graph = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[2 * sizeof(int)];
        foreach (var (a, b) in edges)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes, a);
            BinaryPrimitives.WriteInt32BigEndian(bytes[sizeof(int)..], b);
            graph.AppendData(bytes);
        }
        // Compare partitions and ranked targets by the first member of each group.
        var groupRepresentative = replay.IdentityByObservation.GroupBy(x => x.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Key));
        int? Group(Guid? id) => id is Guid value ? groupRepresentative[value] : null;
        var decisions = replay.Decisions.OrderBy(x => x.ObservationId).Select(x => new
        {
            x.ObservationId,
            Group = Group(replay.IdentityByObservation[x.ObservationId]),
            x.Decision.Status,
            Resolved = Group(x.Decision.PessoaUuidResolvido),
            First = Group(x.Decision.MelhorCandidatoUuid),
            Second = Group(x.Decision.SegundoCandidatoUuid),
            x.Decision.MelhorScore,
            x.Decision.SegundoScore,
            x.Decision.Margem,
            x.Decision.Motivo,
        });
        return new(edges.Count, Convert.ToHexStringLower(graph.GetHashAndReset()),
            Sha(JsonSerializer.Serialize(decisions)));
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
        if (test.Count is < 2 or > V9OperationalPinnedCohortRuntime.MaximumAuditObservations
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
        {
            // Aggregate diagnostic only. Counts are censored at cap+1.
            // Never change blocking, winner, or scientific resource cap.
            var byPass = signatures.GroupBy(x => x.PassId, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var count = CountCrossManagerPairIncidences(
                        group.Select(x => (x.ObservationId, x.Partition, x.PassId, x.Signature)).ToArray(),
                        validation, execution.MaximumCandidatePairIncidences);
                    return new
                    {
                        Pass = group.Key,
                        SignatureRows = group.Count(),
                        IncidencesCapped = count,
                        ExceedsCap = count > execution.MaximumCandidatePairIncidences
                    };
                }).ToArray();
            // Diagnostic-only exact distinct-pair census, bounded to the declared cap + 1.
            // It never substitutes for the conservative incidence admission guard.
            var gestorById = validation.ToDictionary(x => x.ObservationId, x => x.Gestor);
            var distinctPairs = new HashSet<(int A, int B)>();
            var distinctByPass = new List<object>();
            var distinctCensored = false;
            foreach (var pass in signatures.GroupBy(x => x.PassId, StringComparer.Ordinal)
                         .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                long newPairs = 0, repeatedPairs = 0;
                foreach (var bucket in pass.GroupBy(x => (x.Partition, x.Signature)))
                {
                    var ids = bucket.Select(x => x.ObservationId).Distinct().OrderBy(x => x).ToArray();
                    for (var i = 0; i < ids.Length && !distinctCensored; i++)
                    for (var j = i + 1; j < ids.Length; j++)
                    {
                        if (string.Equals(gestorById[ids[i]], gestorById[ids[j]], StringComparison.Ordinal))
                            continue;
                        if (distinctPairs.Add((ids[i], ids[j]))) newPairs++;
                        else repeatedPairs++;
                        if (distinctPairs.Count > execution.MaximumCandidatePairIncidences)
                        {
                            distinctCensored = true;
                            break;
                        }
                    }
                    if (distinctCensored) break;
                }
                distinctByPass.Add(new { Pass = pass.Key, NewDistinctPairs = newPairs,
                    RepeatedIncidencesAgainstEarlierPasses = repeatedPairs,
                    Complete = !distinctCensored });
                if (distinctCensored) break;
            }
            Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                eventType = "V9_TEST_PAIR_CAP_DIAGNOSTIC_NON_SCIENTIFIC",
                purpose,
                observationCount = validation.Count,
                projectedSignatureRows = signatures.Count,
                declaredPairIncidenceCap = execution.MaximumCandidatePairIncidences,
                declaredSignatureCap = execution.MaximumProjectedSignatures,
                aggregateIncidencesCapped = (long)execution.MaximumCandidatePairIncidences + 1,
                perPass = byPass,
                distinctPairsCapped = distinctPairs.Count,
                distinctPairCensusComplete = !distinctCensored,
                distinctPairOverlapByPass = distinctByPass,
                distinctPairCensusDiagnosticOnly = true,
                countSemantics = "cross-manager incidences, duplicate pass hits counted, censored at cap+1",
                candidatePairsMaterialized = false,
                winnerModified = false
            }));
            throw new InvalidDataException("Holdout excede recursos pré-declarados; não truncar candidatos.");
        }
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

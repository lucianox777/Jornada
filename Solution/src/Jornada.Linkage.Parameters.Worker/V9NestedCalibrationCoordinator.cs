using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9NestedCalibrationPlan(V9AdaptiveCalibrationPlan Calibration, string SplitSeed,
    int MaximumBatches, int MaximumSearchConfigurations, int BootstrapRepetitions, int BootstrapSeed,
    long MinimumObservedMPerCell, long MinimumObservedUPerCell,
    decimal InitialThresholdStep, decimal InitialMarginStep,
    decimal AdjustmentFraction = .7m, double MinimumRecallImprovement = .0001,
    double MinimumFdrImprovement = .00001, string Purpose = "BOOTSTRAP_CANDIDATE_SELECTION",
    decimal ClassMassCoverage = .99m);

public sealed record V9NestedCandidateEvaluation(string RequestSha256, int Batch,
    V9TrainingCandidate? Candidate, IReadOnlyList<string> Rejections);

public sealed record V9NestedCalibrationSearchResult(string Version, string PlanSha256,
    V9InternalTrainingSplit.Manifest Split, IReadOnlyList<V9NestedCandidateEvaluation> Evaluations,
    IReadOnlyList<V9TrainingBatchProgress> Progress, IReadOnlyList<string> FrontierSnapshotShas,
    IReadOnlyList<string> FinalistSnapshotShas, string StopReason,
    string FinalistRule = "BEST_RECALL_WITHIN_EPSILON_THEN_SAFER_BAND_THEN_NORMALIZED_IDEAL_DISTANCE_V1",
    string Scope = "TRAIN_SELECTION_ONLY_HEURISTIC_FRONTIER_NOT_GLOBAL_OPTIMUM",
    bool ExternalValidationUsed = false, bool TestUsed = false, bool Activated = false,
    string SnapshotEvaluationSlotPurpose = "TRAIN_SELECTION_NOT_EXTERNAL_VALIDATION");

/// <summary>Selection stage of the single C# calibrator; external holdouts are absent from this API.</summary>
public static class V9NestedCalibrationCoordinator
{
    public const string Version = "V9_INTERNAL_TRAIN_BATCH_PARETO_FINALISTS_V1";
    private sealed record Request(IReadOnlyList<LinkageBlockingPass> Passes, V9FsAdaptiveOptionConfiguration Option, decimal Margin)
    {
        internal string Sha => V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(new
        {
            Passes = Passes.OrderBy(p => p.PassId, StringComparer.Ordinal),
            Alpha = Option.SmoothingAlpha.ToString("G29", CultureInfo.InvariantCulture),
            Threshold = Option.DecisionThreshold.ToString("G29", CultureInfo.InvariantCulture),
            Margin = Margin.ToString("G29", CultureInfo.InvariantCulture)
        }));
    }

    public static void ValidatePlan(V9NestedCalibrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        V9AdaptiveCalibrationCoordinator.ValidatePlan(plan.Calibration);
        V9JointSupportEstimation.ValidatePolicy(new(plan.ClassMassCoverage, plan.MinimumObservedMPerCell, plan.MinimumObservedUPerCell));
        if (string.IsNullOrWhiteSpace(plan.SplitSeed) || plan.AdjustmentFraction is <= 0 or >= 1
            || plan.MaximumBatches < 1 || plan.MaximumSearchConfigurations < 1
            || plan.BootstrapRepetitions < 100 || plan.MinimumObservedMPerCell < 0 || plan.MinimumObservedUPerCell < 0
            || plan.InitialThresholdStep is <= 0 or >= 1 || plan.InitialMarginStep <= 0
            || !double.IsFinite(plan.MinimumRecallImprovement) || plan.MinimumRecallImprovement <= 0
            || !double.IsFinite(plan.MinimumFdrImprovement) || plan.MinimumFdrImprovement <= 0
            || plan.Calibration.ModelOptions.Any(x => x.TfWeight != 0)
            || plan.Calibration.RiskCeilings is not { Count: > 0 }
            || plan.Calibration.MaximumAcceptedPerBaseIdentity != SyntheticObservationMultiplicityContract.MaximumObservationsPerBaseIdentity
            || plan.Calibration.ShardCount != 1 || plan.Calibration.Execution.EndToEndRepetitions < 20)
            throw new InvalidDataException("Busca interna exige política, custos, suporte, TF0, faixas e orçamento explícitos.");
        if (plan.Purpose is not ("BOOTSTRAP_CANDIDATE_SELECTION" or "PLUMBING_FIXTURE_NOT_CERTIFICATION")
            || plan.Purpose == "BOOTSTRAP_CANDIDATE_SELECTION" && (plan.MinimumObservedMPerCell < 1 || plan.MinimumObservedUPerCell < 1))
            throw new InvalidDataException("Bootstrap exige suporte observado positivo no núcleo de massa de cada classe; piso zero é exclusivo da fixture sem certificação.");
    }

    public static async Task<V9NestedCalibrationSearchResult> SearchAsync(
        Func<CancellationToken, Task<SqlConnection>> openIndependentConnection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> originalTrain,
        V9NestedCalibrationPlan plan, CancellationToken cancellationToken,
        Action<V9TrainingBatchProgress>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(openIndependentConnection);
        ValidatePlan(plan);
        originalTrain = Array.AsReadOnly(originalTrain.ToArray());
        // Deep freeze before asynchronous execution. Search never receives a VALIDATION/TEST cohort.
        plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(JsonSerializer.Serialize(plan))!;
        var split = V9InternalTrainingSplit.Create(originalTrain, plan.SplitSeed, plan.AdjustmentFraction);
        var calibration = plan.Calibration;
        if (originalTrain.GroupBy(x => x.BasePersonId, StringComparer.Ordinal).Any(g => g.Count() > calibration.MaximumAcceptedPerBaseIdentity))
            throw new InvalidDataException("TRAIN excede cap pré-declarado por identidade.");
        // Existing physical adapter names its evaluation slot VALIDATION. Here that
        // slot contains ONLY TRAIN_SELECTION, proven by the outer split manifest.
        var frame = split.Adjustment.Concat(split.Selection.Select(x => x with { Partition = "VALIDATION" })).ToArray();
        IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> ruleSets;
        if (calibration.DeclaredSearchSpace is { } declared)
        {
            if (declared.Count() > calibration.MaximumPhysicalRuleSets)
                throw new InvalidDataException("Catálogo inicial excede orçamento; declarar outra estratégia, nunca truncar silenciosamente.");
            ruleSets = V9AdaptiveCalibrationCoordinator.CombineCandidateRuleSets(declared.Enumerate(), calibration.BaselineRuleSets);
        }
        else
        {
            var discovered = V9TrainBlockingCandidateDiscovery.Discover(split.Adjustment, calibration.Discovery);
            ruleSets = V9AdaptiveCalibrationCoordinator.CombineCandidateRuleSets(discovered.Candidates, calibration.BaselineRuleSets);
        }
        var catalogue = (calibration.DeclaredSearchSpace?.CanonicalCatalogue() ?? ruleSets.SelectMany(x => x)
            .GroupBy(x => x.PassId, StringComparer.Ordinal).Select(g => g.First()).OrderBy(x => x.PassId, StringComparer.Ordinal).ToArray());
        if (ruleSets.SelectMany(x => x).Concat(catalogue).GroupBy(x => x.PassId, StringComparer.Ordinal)
            .Any(g => g.Select(p => string.Join('|', p.Fields)).Distinct(StringComparer.Ordinal).Count() > 1))
            throw new InvalidDataException("PassId não pode representar predicados diferentes entre lotes.");
        var requests = ruleSets.SelectMany(passes => calibration.ModelOptions.SelectMany(option => calibration.ConflictMargins
            .Select(margin => new Request(passes, option with { OptionId = OptionId(option.SmoothingAlpha, option.DecisionThreshold) }, margin))))
            .DistinctBy(x => x.Sha).OrderBy(x => x.Sha, StringComparer.Ordinal).ToArray();
        if (requests.Length > plan.MaximumSearchConfigurations)
            throw new InvalidDataException("Lote inicial excede orçamento declarado; nenhuma configuração foi eliminada por resultado.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var evaluations = new List<V9NestedCandidateEvaluation>();
        var progress = new List<V9TrainingBatchProgress>();
        IReadOnlyList<V9TrainingCandidate> frontier = [];
        var stagnant = 0;
        var stop = "BATCH_BUDGET_EXHAUSTED_NOT_OPTIMALITY";
        var active = new HashSet<SqlConnection>(ReferenceEqualityComparer.Instance);
        for (var batch = 1; batch <= plan.MaximumBatches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = frontier;
            var batchIndex = batch;
            foreach (var request in requests) seen.Add(request.Sha);
            var groups = requests.GroupBy(r => JsonSerializer.Serialize(new { r.Passes,
                    Margin = r.Margin.ToString("G29", CultureInfo.InvariantCulture) }))
                .SelectMany(g => g.Chunk(64)).ToArray();
            var results = await V9ParallelWork.MapAsync(groups, calibration.MaximumParallelRuleSets, async (group, ct) =>
            {
                var connection = await openIndependentConnection(ct);
                lock (active) if (!active.Add(connection)) throw new InvalidDataException("Workers exigem conexões SQL independentes.");
                try { return await EvaluateAsync(connection, frame, plan, group, batchIndex, ct); }
                finally { lock (active) active.Remove(connection); await connection.DisposeAsync(); }
            }, cancellationToken);
            evaluations.AddRange(results.SelectMany(x => x).OrderBy(x => x.RequestSha256, StringComparer.Ordinal));
            frontier = V9TrainingParetoSelection.Frontier(evaluations.Where(x => x.Candidate is not null).Select(x => x.Candidate!));
            var currentBands = V9TrainingParetoSelection.Bands(frontier, calibration.RiskCeilings!);
            var previousBands = V9TrainingParetoSelection.Bands(previous, calibration.RiskCeilings!);
            double recallGain = 0, fdrGain = 0, deltaR = plan.MinimumRecallImprovement, deltaF = plan.MinimumFdrImprovement;
            double normalizedGain = 0;
            var material = previous.Count == 0 && frontier.Count > 0;
            foreach (var point in frontier)
            {
                var old = previous.Where(x => x.Metric.FdrAfterResolve >= point.Metric.FdrAfterResolve)
                    .OrderByDescending(x => x.Metric.GlobalSourceRecall).FirstOrDefault();
                var resolution = V9TrainingParetoSelection.PairedBootstrapResolution(point, old,
                    plan.BootstrapRepetitions, plan.BootstrapSeed, plan.MinimumRecallImprovement, plan.MinimumFdrImprovement);
                deltaR = Math.Max(deltaR, resolution.Recall); deltaF = Math.Max(deltaF, resolution.Fdr);
                var gain = old is null ? 0 : point.Metric.GlobalSourceRecall - old.Metric.GlobalSourceRecall;
                recallGain = Math.Max(recallGain, gain);
                material |= gain > resolution.Recall;
                normalizedGain = Math.Max(normalizedGain, Math.Max(0, gain / resolution.Recall));
                var oldAtRecall = previous.Where(x => x.Metric.GlobalSourceRecall <= point.Metric.GlobalSourceRecall)
                    .OrderBy(x => x.Metric.FdrAfterResolve).FirstOrDefault();
                if (oldAtRecall is not null)
                {
                    var fResolution = V9TrainingParetoSelection.PairedBootstrapResolution(point, oldAtRecall,
                        plan.BootstrapRepetitions, plan.BootstrapSeed, plan.MinimumRecallImprovement, plan.MinimumFdrImprovement);
                    var improvement = oldAtRecall.Metric.FdrAfterResolve!.Value - point.Metric.FdrAfterResolve!.Value;
                    fdrGain = Math.Max(fdrGain, improvement); deltaF = Math.Max(deltaF, fResolution.Fdr);
                    material |= improvement > fResolution.Fdr;
                    normalizedGain = Math.Max(normalizedGain, Math.Max(0, improvement / fResolution.Fdr));
                }
            }
            for (var i = 0; i < currentBands.Count; i++)
            {
                if (currentBands[i].SnapshotSha256 is not { } currentSha) continue;
                if (previousBands[i].SnapshotSha256 is not { } previousSha) { material = true; continue; }
                var point = frontier.Single(x => x.Snapshot.ContentSha256 == currentSha);
                var old = previous.Single(x => x.Snapshot.ContentSha256 == previousSha);
                var resolution = V9TrainingParetoSelection.PairedBootstrapResolution(point, old,
                    plan.BootstrapRepetitions, plan.BootstrapSeed, plan.MinimumRecallImprovement, plan.MinimumFdrImprovement);
                var gain = point.Metric.GlobalSourceRecall - old.Metric.GlobalSourceRecall;
                recallGain = Math.Max(recallGain, gain); deltaR = Math.Max(deltaR, resolution.Recall);
                material |= gain > resolution.Recall;
                normalizedGain = Math.Max(normalizedGain, Math.Max(0, gain / resolution.Recall));
            }
            stagnant = material || !double.IsFinite(deltaR) || !double.IsFinite(deltaF) ? 0 : stagnant + 1;
            progress.Add(new(batch, requests.Length, evaluations.Count, currentBands, recallGain, fdrGain,
                double.IsFinite(deltaR) ? deltaR : null, double.IsFinite(deltaF) ? deltaF : null, material, stagnant,
                MaximumGainInResolutionUnits: normalizedGain));
            reportProgress?.Invoke(progress[^1]);
            if (frontier.Count == 0) { stop = "NO_FEASIBLE_CONFIGURATION_NO_HOLDOUT_USED"; break; }
            if (stagnant >= 2) { stop = "TWO_STAGNANT_BATCHES_HEURISTIC_NOT_OPTIMALITY"; break; }
            if (evaluations.Count >= plan.MaximumSearchConfigurations) { stop = "COMPUTE_BUDGET_EXHAUSTED_NOT_OPTIMALITY"; break; }
            requests = Refine(frontier, catalogue, plan, batch).Where(x => !seen.Contains(x.Sha)).DistinctBy(x => x.Sha)
                .OrderBy(x => x.Sha, StringComparer.Ordinal).Take(plan.MaximumSearchConfigurations - evaluations.Count).ToArray();
            if (requests.Length == 0) { stop = "DECLARED_NEIGHBOURHOOD_EXHAUSTED_NOT_GLOBAL_OPTIMALITY"; break; }
        }
        var finalists = V9TrainingParetoSelection.Finalists(frontier, calibration.RiskCeilings!, calibration.SelectionPolicy.MaximumAcceptedFdrUpper);
        return new(Version, V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(plan)), split.Manifest,
            evaluations.AsReadOnly(), progress.AsReadOnly(), frontier.Select(x => x.Snapshot.ContentSha256).ToArray(),
            finalists.Select(x => x.Snapshot.ContentSha256).ToArray(), stop);
    }

    private static string OptionId(decimal alpha, decimal threshold) =>
        "A" + alpha.ToString("G29", CultureInfo.InvariantCulture) + "_T"
        + threshold.ToString("G29", CultureInfo.InvariantCulture) + "_TF0";

    private static IEnumerable<Request> Refine(IReadOnlyList<V9TrainingCandidate> frontier,
        IReadOnlyList<LinkageBlockingPass> catalogue, V9NestedCalibrationPlan plan, int batch)
    {
        var divisor = (decimal)Math.Pow(2, Math.Min(batch - 1, 24));
        var dt = plan.InitialThresholdStep / divisor; var dm = plan.InitialMarginStep / divisor;
        foreach (var candidate in frontier)
        {
            var p = candidate.Snapshot.Payload;
            var ruleSets = new List<IReadOnlyList<LinkageBlockingPass>> { p.BlockingPasses };
            foreach (var pass in catalogue)
            {
                var contains = p.BlockingPasses.Any(x => x.PassId == pass.PassId);
                var neighbor = contains ? p.BlockingPasses.Where(x => x.PassId != pass.PassId).ToArray()
                    : p.BlockingPasses.Append(pass).OrderBy(x => x.PassId, StringComparer.Ordinal).ToArray();
                if (neighbor.Length > 0) ruleSets.Add(neighbor);
            }
            foreach (var passes in ruleSets)
            foreach (var threshold in new[] { p.Threshold - dt, p.Threshold, p.Threshold + dt }.Where(x => x is > 0 and < 1))
            foreach (var margin in new[] { p.ConflictMargin - dm, p.ConflictMargin, p.ConflictMargin + dm }.Where(x => x is > 0 and <= 1000))
                yield return new(passes, new(OptionId(candidate.Support.SmoothingAlpha, threshold), candidate.Support.SmoothingAlpha, threshold), margin);
        }
    }

    private static async Task<IReadOnlyList<V9NestedCandidateEvaluation>> EvaluateAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> frame, V9NestedCalibrationPlan plan,
        IReadOnlyList<Request> requests, int batch, CancellationToken cancellationToken)
    {
        var c = plan.Calibration;
        try
        {
            var first = requests[0];
            var run = await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(connection, frame, first.Passes,
                requests.Select(x => x.Option).ToArray(), c.Budget, c.StudyFingerprintSha256, cancellationToken,
                evaluateFinalResolve: true, finalConflictMargin: first.Margin,
                predeclaredMaximumAcceptedPerBaseIdentity: c.MaximumAcceptedPerBaseIdentity,
                includeFsBlindGrouping: true, minimumFsGroupingPosterior: c.MinimumGroupingPosterior,
                adaptiveExecution: c.Execution with { EndToEndRepetitions = 0, TotalComparedConfigurations = plan.MaximumSearchConfigurations },
                includeIdentityOutcomes: true,
                jointSupportPolicy: new(plan.ClassMassCoverage, plan.MinimumObservedMPerCell, plan.MinimumObservedUPerCell));
            var result = new List<V9NestedCandidateEvaluation>();
            foreach (var request in requests)
            {
                var metric = run.SourceResolveValidation!.Single(x => x.OptionId == request.Option.OptionId);
                var fit = run.FrozenConfigurations!.Single(x => x.OptionId == request.Option.OptionId);
                var support = fit.JointSupport!;
                var reasons = new List<string>();
                if (!V9JointSupportEstimation.HasRequiredCoreSupport(fit.Snapshot.Payload.JointSupport!))
                    reasons.Add("SUPORTE_OBSERVADO_NO_NUCLEO_DE_MASSA_INSUFICIENTE");
                var fanout = run.CalibrationReport.Options.Single(x => x.OptionId == request.Option.OptionId).Validation.BlockingFalseDegreeP99;
                if (fanout > c.Budget.MaximumBlockingFalseDegreeP99) reasons.Add("FANOUT_P99_ACIMA_DO_ORCAMENTO");
                if (metric.FdrAfterResolve is null) reasons.Add("FDR_INDEFINIDO_SEM_ACEITOS");
                double? p95 = null;
                if (reasons.Count == 0)
                {
                    var measured = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(connection,
                        frame.Where(x => x.Partition == "VALIDATION").ToArray(), fit.Snapshot,
                        c.Execution with { TotalComparedConfigurations = 1 }, c.MaximumAcceptedPerBaseIdentity, 1, cancellationToken);
                    p95 = measured.P95Milliseconds;
                    if (!p95.HasValue || p95 > c.SelectionPolicy.MaximumP95Milliseconds) reasons.Add("P95_PONTA_A_PONTA_FORA_DO_ORCAMENTO");
                    if (measured.Metric.TrueAcceptedSourceObservations != metric.TrueAcceptedSourceObservations
                        || measured.Metric.FalseAcceptedSourceObservations != metric.FalseAcceptedSourceObservations
                        || measured.Metric.Conflicts != metric.Conflicts || measured.Metric.Unresolved != metric.Unresolved)
                        throw new InvalidDataException("Fit e replay congelado divergem no TRAIN_SELECTION.");
                }
                result.Add(new(request.Sha, batch, new(fit.Snapshot, metric, support, p95, fanout, reasons.AsReadOnly()), reasons.AsReadOnly()));
            }
            return result.AsReadOnly();
        }
        catch (InvalidDataException exception) when (exception.Data[V9PhysicalSqlAdaptiveCalibrationProbe.CandidateRejectionKey] is true)
        { return Array.AsReadOnly(requests.Select(request => new V9NestedCandidateEvaluation(request.Sha, batch, null,
            ["REJEICAO_FISICA: " + exception.Message])).ToArray()); }
    }
}

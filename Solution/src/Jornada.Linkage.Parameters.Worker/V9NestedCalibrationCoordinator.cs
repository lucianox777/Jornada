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
    decimal ClassMassCoverage = .99m,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SupportedTailRule = null,
    long? PinnedIbgeReferenceVersionId = null,
    string? PinnedIbgePublishedSha256 = null,
    string? PinnedIbgeMaterializedFrequencySha256 = null,
    NominalSyntheticIbgeBootstrapV9? PinnedIbgeBootstrap = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<V9MuEstimationMethod>? MuEstimationMethods = null,
    IReadOnlyList<string>? SelectionDuplicatePolicies = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? PredeclaredCandidateCpMultiplicity = null,
    // New optional knobs: omitted => historical C# behavior and old JSON SHA.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    decimal? RecallEquivalenceMargin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? TieBreakOrder = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ClusterUnit = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Estimator = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? ManualReviewSampleSize = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? RecalibrationTriggers = null);

public sealed record V9NestedCandidateEvaluation(string RequestSha256, int Batch,
    V9TrainingCandidate? Candidate, IReadOnlyList<string> Rejections,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? OptionId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    decimal? ConflictMargin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? MuEstimationMethod = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    V9JointSupportPolicy? MuPolicy = null,
    bool? EligibleForSelection = null,
    long? SharedSurnameCandidatePairs = null,
    string? CandidatePairsSha256 = null);

public sealed record V9NestedCalibrationSearchResult(string Version, string PlanSha256,
    V9InternalTrainingSplit.Manifest Split, IReadOnlyList<V9NestedCandidateEvaluation> Evaluations,
    IReadOnlyList<V9TrainingBatchProgress> Progress, IReadOnlyList<string> FrontierSnapshotShas,
    IReadOnlyList<string> FinalistSnapshotShas, string StopReason,
    string FinalistRule = "BEST_RECALL_WITHIN_EPSILON_THEN_SAFER_BAND_THEN_NORMALIZED_IDEAL_DISTANCE_V1",
    string Scope = "TRAIN_SELECTION_ONLY_HEURISTIC_FRONTIER_NOT_GLOBAL_OPTIMUM",
    bool ExternalValidationUsed = false, bool TestUsed = false, bool Activated = false,
    string SnapshotEvaluationSlotPurpose = "TRAIN_SELECTION_NOT_EXTERNAL_VALIDATION",
    IReadOnlyList<V9DuplicatePolicySelection>? DuplicatePolicySelections = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? PredeclaredCandidateCpMultiplicity = null);

public sealed record V9DuplicatePolicySelection(string Policy, int EligibleHypotheses,
    IReadOnlyList<string> FrontierSnapshotShas, IReadOnlyList<string> FinalistSnapshotShas,
    bool IndependentlyCertified = false,
    int PhysicalHypotheses = 0, int DescriptiveMultiplicityHypotheses = 0);

/// <summary>Selection stage of the single C# calibrator; external holdouts are absent from this API.</summary>
public static class V9NestedCalibrationCoordinator
{
    /// <summary>Diagnostic lambda=0 U-mix is not an independent selection option.</summary>
    public static bool IsUZeroWitness(string? method, decimal? weight) =>
        method == "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE" && weight == 0m;

    /// <summary>
    /// J-only operational exclusion: the historically declared 19-pass SQL
    /// rule set is physically measured as diagnostic/witness, NEVER eligible
    /// for Pareto, refinements, finalists or the candidate CP family. The
    /// 3-pass arm remains selectable. Historical #1300 plans that omit
    /// WITNESS+ANCHOR retain their previous behavior and SHA.
    /// </summary>
    public static bool IsNineteenPassDiagnostic(
        IReadOnlyList<V9MuEstimationMethod>? methods, int blockingPassCount) =>
        blockingPassCount == 19 && methods is not null
        && methods.Any(m => m.Method == V9MuEstimationSearchSpace.Default
            && m.Role == V9MuEstimationSearchSpace.Witness && m.Anchor);

    /// <summary>
    /// J official study: 19-pass SQL and margin zero remain diagnostic only.
    /// Never create a selectable *refinement* from these controls. Old plans
    /// lacking an explicit V2 WITNESS+ANCHOR retain their previous neighborhood.
    /// Initial 19-pass witness measurements remain in the declared physical set.
    /// </summary>
    public static bool AllowsRefinement(IReadOnlyList<V9MuEstimationMethod>? methods,
        int blockingPassCount, decimal conflictMargin)
    {
        var anchored = methods?.Any(m => m.Method == V9MuEstimationSearchSpace.Default
            && m.Role == V9MuEstimationSearchSpace.Witness && m.Anchor) == true;
        return !anchored || (conflictMargin > 0m
            && !IsNineteenPassDiagnostic(methods, blockingPassCount));
    }

    public const string Version = "V9_INTERNAL_TRAIN_BATCH_PARETO_FINALISTS_V1";

    /// <summary>
    /// A conservative, predeclared upper bound on the number of SELECTABLE
    /// hypotheses across every adaptive batch. Physical witnesses are run but
    /// cannot enter the CP family; later refinements originate exclusively
    /// from selected candidates. Subtract only actually predeclared INITIAL
    /// witness requests. Unused candidate budget remains conservatively counted.
    /// No statistical independence/FDR certification is inferred.
    /// </summary>
    public static int CandidateCpMultiplicityUpperBudget(int physicalBudget, int initialWitnessRequests)
    {
        if (physicalBudget < 1 || initialWitnessRequests < 0 ||
            initialWitnessRequests >= physicalBudget)
            throw new InvalidDataException("CP candidate budget inválido; testemunhas não podem ocupar tudo.");
        return checked(physicalBudget - initialWitnessRequests);
    }
    // All estimator variants of the same fixed physical SQL frame reuse one
    // materialization and one frozen TRAIN classification. Distinct option
    // fingerprints / snapshot hashes remain separate; this is NOT a result cache.
    private static string PhysicalFrameKey(Request request) =>
        JsonSerializer.Serialize(new
        {
            request.Passes,
            Margin = request.Margin.ToString("G29", CultureInfo.InvariantCulture)
        });
    private sealed record Request(IReadOnlyList<LinkageBlockingPass> Passes, V9FsAdaptiveOptionConfiguration Option, decimal Margin, string RootOptionId, bool DiagnosticOnly = false)
    {
        internal string Sha => V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(new
        {
            Passes = Passes.OrderBy(p => p.PassId, StringComparer.Ordinal),
            OptionFingerprint = (Option with { OptionId = RootOptionId }).FingerprintSha256,
            RootOptionId,
            Margin = Margin.ToString("G29", CultureInfo.InvariantCulture)
        }));
    }

    public static void ValidatePlan(V9NestedCalibrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        V9AdaptiveCalibrationCoordinator.ValidatePlan(plan.Calibration);
        if (plan.SelectionDuplicatePolicies is { } policies &&
            (policies.Count == 0 || policies.Distinct(StringComparer.Ordinal).Count() != policies.Count ||
             policies.Any(x => x is not ("COUNT_ALL" or "COUNT_UNIQUE"))))
            throw new InvalidDataException("SelectionDuplicatePolicies exige array único de COUNT_ALL/COUNT_UNIQUE.");
        if (plan.MuEstimationMethods is { } muMethods)
        {
            var variants = V9MuEstimationSearchSpace.Enumerate(muMethods);
            // Optional new CP declaration is checked when present; when
            // omitted, the existing conservative candidate budget applies.
            if (plan.Calibration.ModelOptions.Any(x => x.JointSupportPolicy is not null))
                throw new InvalidDataException("Plano M/U explícito não aceita JointSupportPolicy simultâneo nas opções-base.");
        }
        else V9JointSupportEstimation.ValidatePolicy(new(plan.ClassMassCoverage, plan.MinimumObservedMPerCell, plan.MinimumObservedUPerCell, plan.SupportedTailRule));
        if (plan.PredeclaredCandidateCpMultiplicity is { } declaredCp
            && (declaredCp < 1 || declaredCp > plan.MaximumSearchConfigurations))
            throw new InvalidDataException("Limite CP do plano fora do orçamento físico.");
        V9OptionalCompatibilityParameters.Validate(plan);
        if (string.IsNullOrWhiteSpace(plan.SplitSeed) || plan.AdjustmentFraction is <= 0 or >= 1
            || plan.MaximumBatches < 1 || plan.MaximumSearchConfigurations < 1
            || plan.BootstrapRepetitions < 100 || plan.MinimumObservedMPerCell < 0 || plan.MinimumObservedUPerCell < 0
            || plan.InitialThresholdStep is <= 0 or >= 1 || plan.InitialMarginStep <= 0
            || !double.IsFinite(plan.MinimumRecallImprovement) || plan.MinimumRecallImprovement <= 0
            || !double.IsFinite(plan.MinimumFdrImprovement) || plan.MinimumFdrImprovement <= 0
            || plan.Calibration.RiskCeilings is not { Count: > 0 }
            || plan.Calibration.MaximumAcceptedPerBaseIdentity != SyntheticObservationMultiplicityContract.MaximumObservationsPerBaseIdentity
            || plan.Calibration.ShardCount != 1 || plan.Calibration.Execution.EndToEndRepetitions < 20)
            throw new InvalidDataException("Busca interna exige política, custos, suporte, faixas e orçamento explícitos.");
        if (plan.Calibration.ModelOptions.Any(V9FellegiSunterAdaptiveCalibrationAdapter.HasPositiveTf)
            && (plan.PinnedIbgeReferenceVersionId is null or <= 0
                || !ValidSha(plan.PinnedIbgePublishedSha256)
                || !ValidSha(plan.PinnedIbgeMaterializedFrequencySha256)
                || plan.PinnedIbgeBootstrap is null))
            throw new InvalidDataException("TF positivo exige versão IBGE publicada, digests e bootstrap congelados no plano.");
        if (plan.Purpose is not ("BOOTSTRAP_CANDIDATE_SELECTION" or "PLUMBING_FIXTURE_NOT_CERTIFICATION")
            || plan.Purpose == "BOOTSTRAP_CANDIDATE_SELECTION" && plan.MuEstimationMethods is null
                && (plan.MinimumObservedMPerCell < 1 || plan.MinimumObservedUPerCell < 1))
            throw new InvalidDataException("Bootstrap exige suporte observado positivo no núcleo de massa de cada classe; piso zero é exclusivo da fixture sem certificação.");
    }

    /// <summary>
    /// Fail closed before TRAIN fitting when the SQL reference is not the exact
    /// version, published/materialized digest AND synthetic bootstrap declared
    /// by the immutable plan. Matching IBGE marginals alone cannot attest the
    /// generator, corpus, seed or hypotheses of the synthetic training frame.
    /// </summary>
    public static void RequirePinnedTfReference(V9NestedCalibrationPlan plan, FrozenIbgeNominalTfV9? reference)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequirePinnedTfReference(plan.PinnedIbgeReferenceVersionId, plan.PinnedIbgePublishedSha256,
            plan.PinnedIbgeMaterializedFrequencySha256, plan.PinnedIbgeBootstrap, reference);
    }

    // One pinned-reference guard for nested and physical adaptive entry points.
    internal static void RequirePinnedTfReference(long? versionId, string? publishedSha256,
        string? materializedSha256, NominalSyntheticIbgeBootstrapV9? bootstrap,
        FrozenIbgeNominalTfV9? reference)
    {
        if (versionId is null or <= 0
            || !ValidSha(publishedSha256) || !ValidSha(materializedSha256)
            || bootstrap is null || reference is null
            || reference.ReferenceVersionId != versionId
            || !string.Equals(reference.PublishedReferenceSha256, publishedSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(reference.MaterializedFrequencySha256, materializedSha256,
                StringComparison.OrdinalIgnoreCase)
            || reference.Bootstrap != bootstrap)
            throw new InvalidDataException(
                "TF positivo sem versão, digests ou bootstrap IBGE idênticos ao plano congelado.");
    }

    private static bool ValidSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static async Task<V9NestedCalibrationSearchResult> SearchAsync(
        Func<CancellationToken, Task<SqlConnection>> openIndependentConnection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> originalTrain,
        V9NestedCalibrationPlan plan, CancellationToken cancellationToken,
        Action<V9TrainingBatchProgress>? reportProgress = null,
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? frozenTfReferenceForRuleset = null,
        int? executionMaximumParallelRuleSets = null,
        Action<string>? reportSafeStage = null,
        V9CalibrationRuntimeScheduler? resourceScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(openIndependentConnection);
        ValidatePlan(plan);
        if (plan.Calibration.ModelOptions.Any(V9FellegiSunterAdaptiveCalibrationAdapter.HasPositiveTf) && frozenTfReferenceForRuleset is null)
            throw new InvalidDataException("TF positivo na busca aninhada exige referência IBGE publicada e congelada por frame SQL.");
        originalTrain = Array.AsReadOnly(originalTrain.ToArray());
        // Deep freeze before asynchronous execution. Search never receives a VALIDATION/TEST cohort.
        plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(JsonSerializer.Serialize(plan))!;
        // Stage callbacks expose fixed names, never exception text, SQL or IDs.
        reportSafeStage?.Invoke("MU_VARIANT_EXPANSION");
        // Bind selection/VALIDATION to the declared plan, not the internally expanded options.
        var declaredPlanSha = V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(plan));
        if (plan.MuEstimationMethods is { } muMethods)
        {
            var expanded = V9MuEstimationSearchSpace.Expand(plan.Calibration.ModelOptions,
                muMethods, plan.MaximumSearchConfigurations);
            plan = plan with { Calibration = plan.Calibration with { ModelOptions = expanded } };
            // Expanded IDs intentionally exceed fixed 45-arm TF diagnostic naming rules;
            // the original option catalogue has already passed its full validation.
            // The expansion is separately validated and fingerprinted before any SQL.
        }
        reportSafeStage?.Invoke("TRAIN_SPLIT");
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
        reportSafeStage?.Invoke("DECLARED_REQUESTS_AND_CP");
        var requests = ruleSets.SelectMany(passes => calibration.ModelOptions.SelectMany(option => calibration.ConflictMargins
            .Select(margin => new Request(passes, option, margin, option.OptionId,
                 IsNineteenPassDiagnostic(plan.MuEstimationMethods, passes.Count)))))
            .DistinctBy(x => x.Sha).OrderBy(x => x.Sha, StringComparer.Ordinal).ToArray();
        if (requests.Length > plan.MaximumSearchConfigurations)
            throw new InvalidDataException("Lote inicial excede orçamento declarado; nenhuma configuração foi eliminada por resultado.");
        // Physical budget counts EVERY run including WITNESS+ANCHOR V2 and
        // numeric lambda=0. The CP multiplicity counts only selectable
        // candidate hypotheses, conservatively including future refinements.
        // In legacy plans with no explicit methods, preserve the old bound.
        int candidateCpMultiplicity = plan.MaximumSearchConfigurations;
        if (plan.MuEstimationMethods is not null)
        {
            var initialWitnesses = requests.Count(x => !x.Option.EligibleForSelection
                || x.Margin == 0m
                || x.Option.JointSupportPolicy?.UBackoffJointMixtureWeight == 0m
                || x.DiagnosticOnly);
            candidateCpMultiplicity = CandidateCpMultiplicityUpperBudget(
                plan.MaximumSearchConfigurations, initialWitnesses);
            if (requests.Length - initialWitnesses > candidateCpMultiplicity)
                throw new InvalidDataException("CP excluiu candidata inicialmente declarada.");
            if (plan.PredeclaredCandidateCpMultiplicity is { } predeclared
                && predeclared != candidateCpMultiplicity)
                throw new InvalidDataException("Multiplicidade CP candidata difere do único plan.json oficial.");
        }
        // The candidate snapshot only stores OptionId. Keep the complete frozen
        // configuration and its root identity so refinements cannot silently
        // reset TF, support policy or other model dimensions.
        var optionTemplates = new Dictionary<string, Request>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var evaluations = new List<V9NestedCandidateEvaluation>();
        var progress = new List<V9TrainingBatchProgress>();
        IReadOnlyList<V9TrainingCandidate> frontier = [];
        var stagnant = 0;
        var stop = "BATCH_BUDGET_EXHAUSTED_NOT_OPTIMALITY";
        var active = new HashSet<SqlConnection>(ReferenceEqualityComparer.Instance);
        using var referenceLease = new SemaphoreSlim(1);
        reportSafeStage?.Invoke("PHYSICAL_SQL_BATCHES");
        for (var batch = 1; batch <= plan.MaximumBatches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = frontier;
            var batchIndex = batch;
            foreach (var request in requests)
            {
                seen.Add(request.Sha);
                if (optionTemplates.TryGetValue(request.Option.OptionId, out var existing))
                {
                    if (existing.Option.FingerprintSha256 != request.Option.FingerprintSha256
                        || existing.RootOptionId != request.RootOptionId)
                        throw new InvalidDataException("OptionId reutilizado para configurações diferentes na busca interna.");
                }
                else optionTemplates.Add(request.Option.OptionId, request);
            }
            // Candidate pairs depend on the blocking passes, margin and pinned
            // TRAIN frame, not on M/U estimation. One RunAsync evaluates every
            // explicit estimator option on the SAME physical SQL candidate set.
            // Never merge distinct passes/margins, or deduplicate model options.
            // Chunk size preserves the model-search 64-option contract.
            // Schedule likely broader candidate-pair SQL frames first. This
            // is a cost HEURISTIC from declared blocking predicates (not
            // SQL SHOWPLAN or observed wall-clock). The scheduler pilots
            // the heaviest estimated frame for RAM admission and overlaps
            // subsequent independent frames up to the governed ceiling.
            // Requests, model options, scientific hashes, CP accounting,
            // refinements and canonical folding remain unchanged.
            Request[][] groups;
            try
            {
                groups = V9PhysicalFrameExecutionOrder.BroadestLikelyFirst(
                    requests.GroupBy(PhysicalFrameKey).SelectMany(g => g.Chunk(64)).ToArray(),
                    static group => group[0].Passes,
                    static group => group[0].Sha).ToArray();
            }
            catch (InvalidDataException exception)
            {
                V9CalibradorSafeFailureCategory.Mark(exception,
                    V9CalibradorSafeFailureCategory.PhysicalFrameOrder);
                throw;
            }
            var results = await V9ParallelWork.MapCalibrationAsync(groups, executionMaximumParallelRuleSets ?? calibration.MaximumParallelRuleSets, async (group, ct) =>
            {
                FrozenIbgeNominalTfV9? frozenReference = null;
                if (group.Any(x => V9FellegiSunterAdaptiveCalibrationAdapter.HasPositiveTf(x.Option)))
                {
                    // Resolve the immutable published IBGE version for this exact
                    // blocking frame, never the mutable currently-active reference.
                    await referenceLease.WaitAsync(ct);
                    try { frozenReference = await frozenTfReferenceForRuleset!(group[0].Passes, ct); }
                    finally { referenceLease.Release(); }
                    try { RequirePinnedTfReference(plan, frozenReference); }
                    catch (InvalidDataException exception)
                    {
                        V9CalibradorSafeFailureCategory.Mark(exception,
                            V9CalibradorSafeFailureCategory.FrozenTfReference);
                        throw;
                    }
                }
                var connection = await openIndependentConnection(ct);
                lock (active) if (!active.Add(connection)) throw new InvalidDataException("Workers exigem conexões SQL independentes.");
                try { return await EvaluateAsync(connection, frame, plan, group, batchIndex,
                    candidateCpMultiplicity, frozenReference, ct); }
                catch (InvalidDataException exception)
                {
                    V9CalibradorSafeFailureCategory.Mark(exception,
                        V9CalibradorSafeFailureCategory.PhysicalSqlFrame);
                    throw;
                }
                finally { lock (active) active.Remove(connection); await connection.DisposeAsync(); }
            }, resourceScheduler, cancellationToken);
            evaluations.AddRange(results.SelectMany(x => x).OrderBy(x => x.RequestSha256, StringComparer.Ordinal));
            // Keep diagnostic SHARED surname results in the audit but never
            // admit them to the Pareto frontier/refinements/finalists.
            frontier = V9TrainingParetoSelection.Frontier(evaluations.Where(x =>
                x.Candidate is not null
                && optionTemplates.TryGetValue(x.Candidate.Snapshot.Payload.OptionId, out var template)
                && (x.EligibleForSelection ?? template.Option.EligibleForSelection)
                && !IsUZeroWitness(x.MuEstimationMethod, x.MuPolicy?.UBackoffJointMixtureWeight)
                // Master #1298: keep zero diagnostic margins out of the
                // TRAIN Pareto frontier, refinements, and finalists.
                && x.Candidate.Metric.ConflictMargin > 0m).Select(x => x.Candidate!));
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
            requests = Refine(frontier, catalogue, plan, batch, optionTemplates).Where(x => !seen.Contains(x.Sha)).DistinctBy(x => x.Sha)
                .OrderBy(x => x.Sha, StringComparer.Ordinal).Take(plan.MaximumSearchConfigurations - evaluations.Count).ToArray();
            if (requests.Length == 0) { stop = "DECLARED_NEIGHBOURHOOD_EXHAUSTED_NOT_GLOBAL_OPTIMALITY"; break; }
        }
        var finalists = V9TrainingParetoSelection.Finalists(frontier, calibration.RiskCeilings!, calibration.SelectionPolicy.MaximumAcceptedFdrUpper);
        // Both policies share the same selection frontier: lambda=0 is a
        // numerical witness, never a separate finalist or refinement source.
        // COUNT_ALL versus COUNT_UNIQUE only changes the *descriptive*
        // hypothesis count; it is not an independent FDR correction.
        // Physical/descriptive count includes physically measured V2 ANCHOR
        // and lambda=0 witnesses in explicit M/U studies; CP and the selectable
        // count never do. Keep historical no-method report semantics unchanged.
        var measuredForAccounting = evaluations.Where(x => x.Candidate is not null
            && optionTemplates.TryGetValue(x.Candidate.Snapshot.Payload.OptionId, out var template)
            && (plan.MuEstimationMethods is not null || template.Option.EligibleForSelection)
            && x.Candidate.Metric.ConflictMargin > 0m).ToArray();
        var selectable = measuredForAccounting.Where(x =>
            optionTemplates.TryGetValue(x.Candidate!.Snapshot.Payload.OptionId, out var template)
            && (x.EligibleForSelection ?? template.Option.EligibleForSelection)
            && !IsUZeroWitness(x.MuEstimationMethod, x.MuPolicy?.UBackoffJointMixtureWeight)).ToArray();
        if (selectable.Length > candidateCpMultiplicity)
            throw new InvalidDataException("CP sobre candidatas excedeu limite pré-declarado.");
        var duplicateSelections = (plan.SelectionDuplicatePolicies ?? new[] { "COUNT_ALL" })
            .Select(policy => new V9DuplicatePolicySelection(policy, selectable.Length,
                frontier.Select(x => x.Snapshot.ContentSha256).ToArray(),
                finalists.Select(x => x.Snapshot.ContentSha256).ToArray(),
                PhysicalHypotheses: measuredForAccounting.Length,
                DescriptiveMultiplicityHypotheses:
                    policy == "COUNT_ALL" ? measuredForAccounting.Length : selectable.Length))
            .ToArray();
        return new(Version, declaredPlanSha, split.Manifest,
            evaluations.AsReadOnly(), progress.AsReadOnly(), frontier.Select(x => x.Snapshot.ContentSha256).ToArray(),
            finalists.Select(x => x.Snapshot.ContentSha256).ToArray(), stop,
            DuplicatePolicySelections: duplicateSelections,
            PredeclaredCandidateCpMultiplicity: plan.MuEstimationMethods is null ? null : candidateCpMultiplicity);
    }

    private static V9FsAdaptiveOptionConfiguration RefinedOption(
        V9FsAdaptiveOptionConfiguration template, string rootOptionId, decimal threshold) =>
        template with
        {
            OptionId = "NESTED_" + V9InternalTrainingSplit.Hash(rootOptionId) + "_A"
                + template.SmoothingAlpha.ToString("G29", CultureInfo.InvariantCulture) + "_T"
                + threshold.ToString("G29", CultureInfo.InvariantCulture),
            DecisionThreshold = threshold
        };

    private static IEnumerable<Request> Refine(IReadOnlyList<V9TrainingCandidate> frontier,
        IReadOnlyList<LinkageBlockingPass> catalogue, V9NestedCalibrationPlan plan, int batch,
        IReadOnlyDictionary<string, Request> optionTemplates)
    {
        var divisor = (decimal)Math.Pow(2, Math.Min(batch - 1, 24));
        var dt = plan.InitialThresholdStep / divisor; var dm = plan.InitialMarginStep / divisor;
        foreach (var candidate in frontier)
        {
            var p = candidate.Snapshot.Payload;
            if (!optionTemplates.TryGetValue(p.OptionId, out var template))
                throw new InvalidDataException("Finalista sem configuração original congelada.");
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
            foreach (var margin in new[] { p.ConflictMargin - dm, p.ConflictMargin, p.ConflictMargin + dm }
                .Where(x => x is >= 0 and <= 1000
                    && AllowsRefinement(plan.MuEstimationMethods, passes.Count, x)))
                yield return new(passes,
                    RefinedOption(template.Option, template.RootOptionId, threshold),
                    margin, template.RootOptionId,
                    IsNineteenPassDiagnostic(plan.MuEstimationMethods, passes.Count));
        }
    }

    private static async Task<IReadOnlyList<V9NestedCandidateEvaluation>> EvaluateAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> frame, V9NestedCalibrationPlan plan,
        IReadOnlyList<Request> requests, int batch, int candidateCpMultiplicity,
        FrozenIbgeNominalTfV9? frozenTfReference,
        CancellationToken cancellationToken)
    {
        var c = plan.Calibration;
        try
        {
            var first = requests[0];
            V9PhysicalCalibrationRunResult run;
            var lastSafeProbeCheckpoint = "PROBE_ADMISSION";
            try
            {
                run = await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(connection, frame, first.Passes,
                    requests.Select(x => x.Option).ToArray(), c.Budget, c.StudyFingerprintSha256, cancellationToken,
                    frozenTfReference: frozenTfReference,
                    evaluateFinalResolve: true, finalConflictMargin: first.Margin,
                    predeclaredMaximumAcceptedPerBaseIdentity: c.MaximumAcceptedPerBaseIdentity,
                    includeFsBlindGrouping: true, minimumFsGroupingPosterior: c.MinimumGroupingPosterior,
                    adaptiveExecution: c.Execution with { EndToEndRepetitions = 0, TotalComparedConfigurations = candidateCpMultiplicity },
                    includeIdentityOutcomes: true,
                    includeSourceRankDiagnostics: V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics(
                        plan.MuEstimationMethods),
                    jointSupportPolicy: plan.MuEstimationMethods is null
                        ? new(plan.ClassMassCoverage, plan.MinimumObservedMPerCell, plan.MinimumObservedUPerCell, plan.SupportedTailRule)
                        : null,
                    reportSafeProbeCheckpoint: checkpoint => lastSafeProbeCheckpoint = checkpoint);
            }
            catch (InvalidDataException exception)
            {
                // Preserve the rejection marker consumed by the outer catch.
                // This is only a constant phase tag; the raw error remains private.
                V9CalibradorSafeFailureCategory.Mark(exception,
                    V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
                V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(exception, lastSafeProbeCheckpoint);
                throw;
            }
            var result = new List<V9NestedCandidateEvaluation>();
            foreach (var request in requests)
            {
                var metric = run.SourceResolveValidation!.Single(x => x.OptionId == request.Option.OptionId);
                var fit = run.FrozenConfigurations!.Single(x => x.OptionId == request.Option.OptionId);
                var support = fit.JointSupport!;
                var reasons = new List<string>();
                if (fit.Snapshot.Payload.JointSupport is { } provenance
                    && !V9JointSupportEstimation.HasRequiredCoreSupport(provenance))
                    reasons.Add("SUPORTE_OBSERVADO_NO_NUCLEO_DE_MASSA_INSUFICIENTE");
                var fanout = run.CalibrationReport.Options.Single(x => x.OptionId == request.Option.OptionId).Validation.BlockingFalseDegreeP99;
                if (fanout > c.Budget.MaximumBlockingFalseDegreeP99) reasons.Add("FANOUT_P99_ACIMA_DO_ORCAMENTO");
                if (metric.FdrAfterResolve is null) reasons.Add("FDR_INDEFINIDO_SEM_ACEITOS");
                double? p95 = null;
                if (reasons.Count == 0)
                {
                    try
                    {
                        var measured = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(connection,
                            frame.Where(x => x.Partition == "VALIDATION").ToArray(), fit.Snapshot,
                            c.Execution with { TotalComparedConfigurations = 1 }, c.MaximumAcceptedPerBaseIdentity, 1, cancellationToken);
                        p95 = measured.P95Milliseconds;
                        if (!p95.HasValue || p95 > c.SelectionPolicy.MaximumP95Milliseconds)
                            reasons.Add("P95_PONTA_A_PONTA_FORA_DO_ORCAMENTO");
                        if (measured.Metric.TrueAcceptedSourceObservations != metric.TrueAcceptedSourceObservations
                            || measured.Metric.FalseAcceptedSourceObservations != metric.FalseAcceptedSourceObservations
                            || measured.Metric.Conflicts != metric.Conflicts || measured.Metric.Unresolved != metric.Unresolved)
                        {
                            var mismatch = new InvalidDataException("Fit e replay congelado divergem no TRAIN_SELECTION.");
                            V9CalibradorSafeFailureCategory.Mark(mismatch,
                                V9CalibradorSafeFailureCategory.PhysicalReplayMismatch);
                            throw mismatch;
                        }
                    }
                    catch (InvalidDataException exception)
                    {
                        V9CalibradorSafeFailureCategory.Mark(exception,
                            V9CalibradorSafeFailureCategory.PhysicalFrozenHoldout);
                        throw;
                    }
                }
                result.Add(new(request.Sha, batch, new(fit.Snapshot, metric, support, p95, fanout,
                    reasons.AsReadOnly(), run.TrainCandidatePriorDiagnostic),
                    reasons.AsReadOnly(),
                    OptionId: plan.MuEstimationMethods is null ? null : request.Option.OptionId,
                    ConflictMargin: plan.MuEstimationMethods is null ? null : request.Margin,
                    MuEstimationMethod: plan.MuEstimationMethods is null ? null
                        : request.Option.JointSupportPolicy is null
                            ? V9MuEstimationSearchSpace.Default : request.Option.JointSupportPolicy.UBackoffJointMixtureWeight is null
                                ? V9MuEstimationSearchSpace.Classwise : V9MuEstimationSearchSpace.ClasswiseMixture,
                    MuPolicy: plan.MuEstimationMethods is null ? null : request.Option.JointSupportPolicy,
                    EligibleForSelection: request.Option.EligibleForSelection && request.Margin > 0m
                        && !request.DiagnosticOnly,
                    SharedSurnameCandidatePairs: run.CalibrationReport.Options.Single(x =>
                        x.OptionId == request.Option.OptionId).Validation.SharedSurnameCandidatePairs,
                    CandidatePairsSha256: run.PhysicalCandidatePairsSha256));
            }
            return result.AsReadOnly();
        }
        catch (InvalidDataException exception) when (exception.Data[V9PhysicalSqlAdaptiveCalibrationProbe.CandidateRejectionKey] is true)
        { return Array.AsReadOnly(requests.Select(request => new V9NestedCandidateEvaluation(request.Sha, batch, null,
            ["REJEICAO_FISICA: " + V9CalibradorLastErrorLog.SafeErrorCode(exception)],
            OptionId: plan.MuEstimationMethods is null ? null : request.Option.OptionId,
            ConflictMargin: plan.MuEstimationMethods is null ? null : request.Margin,
            MuEstimationMethod: plan.MuEstimationMethods is null ? null
                : request.Option.JointSupportPolicy is null
                    ? V9MuEstimationSearchSpace.Default : request.Option.JointSupportPolicy.UBackoffJointMixtureWeight is null
                                ? V9MuEstimationSearchSpace.Classwise : V9MuEstimationSearchSpace.ClasswiseMixture,
            MuPolicy: plan.MuEstimationMethods is null ? null : request.Option.JointSupportPolicy,
            EligibleForSelection: request.Option.EligibleForSelection && request.Margin > 0m && !request.DiagnosticOnly)).ToArray()); }
    }
}

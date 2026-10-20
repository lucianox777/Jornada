using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9AdaptiveCalibrationPlan(
    string StudyFingerprintSha256,
    V9TrainBlockingDiscoveryOptions Discovery,
    IReadOnlyList<V9FsAdaptiveOptionConfiguration> ModelOptions,
    IReadOnlyList<decimal> ConflictMargins,
    AdaptiveCalibrationPreviewBudget Budget,
    V9BootstrapPareto2D.SelectionPolicy SelectionPolicy,
    int MaximumPhysicalRuleSets,
    int MaximumComparedConfigurations,
    V9PhysicalAdaptiveExecution Execution,
    decimal MinimumGroupingPosterior = .99m,
    int MaximumAcceptedPerBaseIdentity = 16,
    int PreviouslyComparedValidationConfigurations = 0,
    IReadOnlyList<IReadOnlyList<LinkageBlockingPass>>? BaselineRuleSets = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    V9SyntheticObservationCapDeclaration? ObservationCapDeclaration = null,
    int MaximumParallelRuleSets = 1,
    IReadOnlyList<double>? RiskCeilings = null,
    V9StatisticalSamplePlan.Design? SampleDesign = null,
    V9DeclaredBlockingSearchSpace? DeclaredSearchSpace = null,
    bool CollapseEquivalentCandidateFrames = false,
    int ShardIndex = 0, int ShardCount = 1);

public sealed record V9AdaptiveCandidateRejection(string RulesetSha256,
    IReadOnlyList<string> ConfigurationIds, string Reason);

public sealed record V9AdaptiveCalibrationStudyResult(string Version, string PlanSha256,
    string TrainInputSha256, string ValidationInputSha256,
    int PredeclaredComparedConfigurations, V9TrainBlockingDiscoveryResult Discovery,
    V9ParetoSourceSelectionReport? SourceSelection, V9BootstrapPareto2D.Result Pareto,
    IReadOnlyList<V9PhysicalSourceResolveMetric> SourceMeasurements,
    IReadOnlyList<V9PhysicalFrozenConfiguration> FrozenConfigurations,
    IReadOnlyList<V9PhysicalEndToEndMeasurement> EndToEndMeasurements,
    IReadOnlyList<V9AdaptiveCandidateRejection> Rejections,
    int TotalCandidateRuleSets = 0,
    V9RiskBandFrontier.Report? RiskFrontier = null,
    V9StatisticalSamplePlan.Report? SamplePlan = null,
    V9CandidateFrameEquivalence.Report? CandidateFrameCoverage = null,
    V9PhysicalCatalogueCensus? CatalogueCensus = null,
    V9IntegralSearchShard? Shard = null);

public sealed record V9IntegralSearchShard(string FamilyPlanSha256, int Index, int Count,
    int TotalRepresentativeRuleSets, int EvaluatedRepresentativeRuleSets,
    bool WholeSearchEvaluated);

/// <summary>
/// One C# calibration path: TRAIN proposal discovery, full physical candidate union,
/// TRAIN-specific m/u and TF, held-out source Rank/Resolve, and global 2D Pareto.
/// Batching does not prune options or reset multiplicity. No TEST, publication or activation.
/// </summary>
public static class V9AdaptiveCalibrationCoordinator
{
    public const string Version = "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1";
    public const string Grouping = "BLIND_FS_COMPLETE_LINK_CANDIDATES";

    /// <summary>
    /// TRAIN_SELECTION-only lower no-match search within the ONE V9 C# calibrator.
    /// It consumes real frozen FS decisions and certified-by-the-caller physical
    /// retrieval metadata, but never changes the fitted scorer, VALIDATION, SQL
    /// publication or the DT-15 activation gate. Training examples are labeled
    /// AFTER the Gold run, not from the model's candidate generation.
    /// </summary>
    public static V9LowerNonMatchTrainCalibrator.Study SearchLowerNonMatchOnTrain(
        V9LowerNonMatchTrainCalibrator.Policy policy,
        IReadOnlyList<V9LowerNonMatchTrainCalibrator.TrainingSource> trainSelection)
        => V9LowerNonMatchTrainCalibrator.Search(policy, trainSelection);

    public static void ValidatePlan(V9AdaptiveCalibrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.StudyFingerprintSha256 is not { Length: 64 }
            || !plan.StudyFingerprintSha256.All(Uri.IsHexDigit)
            || plan.Discovery is null || plan.ModelOptions is null || plan.Execution is null
            || plan.ConflictMargins is null || plan.Budget is null || plan.SelectionPolicy is null
            || plan.MaximumPhysicalRuleSets < 1 || plan.MaximumComparedConfigurations < 1
            || plan.MaximumParallelRuleSets < 1
            || plan.ShardCount < 1 || plan.ShardIndex < 0 || plan.ShardIndex >= plan.ShardCount
            || plan.PreviouslyComparedValidationConfigurations < 0
            || plan.MaximumAcceptedPerBaseIdentity < 1
            || plan.MinimumGroupingPosterior is <= .5m or >= 1m
            || plan.ModelOptions.Count == 0 || plan.ConflictMargins.Count == 0
            || plan.ModelOptions.Any(x => x is null || string.IsNullOrWhiteSpace(x.OptionId)
                || x.SmoothingAlpha is <= 0 or > 1000 || x.DecisionThreshold is <= 0 or >= 1
                || x.TfWeight is < 0 or > 1 || !Enum.IsDefined(x.TfSide)
                || x.TfMinimumFrequency is < 0 or > 1 || x.TfMinimumIndependentSupport < 1)
            || plan.ModelOptions.Select(x => x.OptionId).Distinct(StringComparer.Ordinal).Count() != plan.ModelOptions.Count
            || plan.ConflictMargins.Any(x => x is <= 0 or > 1000)
            || plan.ConflictMargins.Distinct().Count() != plan.ConflictMargins.Count)
            throw new InvalidDataException("Plano V9 incompleto, duplicado ou fora do contrato.");
        var budget = plan.Budget;
        var policy = plan.SelectionPolicy;
        if (budget.Version != AdaptiveCalibrationPreviewBudget.CurrentVersion
            || budget.StudyFingerprintSha256 != plan.StudyFingerprintSha256
            || !double.IsFinite(budget.MinimumGlobalAcceptedRecall) || budget.MinimumGlobalAcceptedRecall is < 0 or > 1
            || !double.IsFinite(budget.MaximumObservedAcceptedFdrPoint) || budget.MaximumObservedAcceptedFdrPoint is < 0 or > .01
            || budget.MinimumValidationIdentityGroups < 2
            || !double.IsFinite(budget.MaximumBlockingFalseDegreeP99) || budget.MaximumBlockingFalseDegreeP99 < 0
            || !double.IsFinite(policy.MinimumGlobalRecall) || policy.MinimumGlobalRecall is < 0 or > 1
            || !double.IsFinite(policy.MaximumAcceptedFdrUpper) || policy.MaximumAcceptedFdrUpper is < 0 or > 1
            || !double.IsFinite(policy.MaximumP95Milliseconds) || policy.MaximumP95Milliseconds <= 0
            || policy.MinimumGlobalRecall < budget.MinimumGlobalAcceptedRecall
            || policy.MaximumAcceptedFdrUpper > budget.MaximumObservedAcceptedFdrPoint
            || policy.MinimumIndependentIdentityGroups < budget.MinimumValidationIdentityGroups
            || plan.Execution.MaximumPrefixLength != plan.Discovery.MaximumPrefixLength
            || !plan.Execution.FsBlindGroupingOnly || plan.Execution.MaximumBlindClusterSize is null)
            throw new InvalidDataException("Plano não pode relaxar risco, trocar projeção ou omitir agrupamento.");
        plan.Execution.Validate(evaluateFinalResolve: true, includeFsBlindGrouping: true);
        if (plan.ObservationCapDeclaration is { } capDeclaration)
            V9SyntheticObservationCapEvidence.ValidateDeclaration(capDeclaration, plan.MaximumAcceptedPerBaseIdentity);
        if (plan.BaselineRuleSets is not null)
            _ = CombineCandidateRuleSets([], plan.BaselineRuleSets);
        if (plan.DeclaredSearchSpace is { } space && space.Count() > plan.MaximumPhysicalRuleSets)
            throw new InvalidDataException("Espaço integral excede orçamento declarado: amplie o orçamento; nenhuma união será descartada.");
        if (plan.CollapseEquivalentCandidateFrames
            && (plan.DeclaredSearchSpace is null || plan.ModelOptions.Any(x => x.TfWeight != 0)))
            throw new InvalidDataException("Equivalência exata exige catálogo declarado e TF zero; TF positiva precisa de prova própria.");
        if (plan.ShardCount != 1 && !plan.CollapseEquivalentCandidateFrames)
            throw new InvalidDataException("Shards exigem família integral e equivalência física fixadas.");
        if (plan.RiskCeilings is { } ceilings && (ceilings.Count == 0
            || ceilings.Any(x => !double.IsFinite(x) || x is <= 0 or >= 1)
            || ceilings.Distinct().Count() != ceilings.Count))
            throw new InvalidDataException("Faixas de risco inválidas ou duplicadas.");
        if (plan.SampleDesign is { } design)
        {
            if (plan.RiskCeilings is null || design.ComparedConfigurations < plan.MaximumComparedConfigurations)
                throw new InvalidDataException("Desenho amostral exige faixas e cobertura da multiplicidade inteira.");
            _ = V9StatisticalSamplePlan.Calculate(design, plan.RiskCeilings);
        }
    }

    /// <summary>Retains declared controls alongside proposals; never prunes using VALIDATION.</summary>
    public static IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> CombineCandidateRuleSets(
        IEnumerable<IReadOnlyList<LinkageBlockingPass>> discovered,
        IEnumerable<IReadOnlyList<LinkageBlockingPass>>? baselines)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        var result = new List<IReadOnlyList<LinkageBlockingPass>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ruleset in discovered.Concat(baselines ?? []))
        {
            if (ruleset is null || ruleset.Count < 1
                || ruleset.Any(p => p is null || p.Fields is null))
                throw new InvalidDataException("Ruleset de controle/proposta inválido.");
            var canonical = ruleset.Select(p => LinkageBlockingPass.Create(p.PassId, p.Fields))
                .OrderBy(p => p.PassId, StringComparer.Ordinal)
                .Select(p => new LinkageBlockingPass(p.PassId, Array.AsReadOnly(p.Fields.ToArray()))).ToArray();
            if (canonical.Select(p => p.PassId).Distinct(StringComparer.Ordinal).Count() != canonical.Length)
                throw new InvalidDataException("Ruleset de controle/proposta com PassId duplicado.");
            if (seen.Add(JsonSerializer.Serialize(canonical))) result.Add(Array.AsReadOnly(canonical));
        }
        return result.AsReadOnly();
    }

    public static int CountComparedConfigurations(V9AdaptiveCalibrationPlan plan, long ruleSets)
    {
        ValidatePlan(plan);
        var count = checked(ruleSets * plan.ModelOptions.Count * plan.ConflictMargins.Count
            + plan.PreviouslyComparedValidationConfigurations);
        if (ruleSets < 1 || ruleSets > plan.MaximumPhysicalRuleSets
            || count > plan.MaximumComparedConfigurations || count > int.MaxValue
            || plan.Execution.TotalComparedConfigurations.HasValue
                && plan.Execution.TotalComparedConfigurations.Value != count)
            throw new InvalidDataException("Busca excede orçamento de computação declarado; nenhuma configuração foi truncada.");
        return checked((int)count);
    }

    public static Task<V9AdaptiveCalibrationStudyResult> RunAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        V9AdaptiveCalibrationPlan plan, CancellationToken cancellationToken,
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? frozenTfReferenceForRuleset = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.MaximumParallelRuleSets != 1)
            throw new InvalidDataException("Paralelismo SQL exige fábrica de conexões independentes.");
        return RunCoreAsync(_ => Task.FromResult(connection), false, observations, plan,
            frozenTfReferenceForRuleset, cancellationToken);
    }

    public static Task<V9AdaptiveCalibrationStudyResult> RunParallelAsync(
        Func<CancellationToken, Task<SqlConnection>> openIndependentConnection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        V9AdaptiveCalibrationPlan plan, CancellationToken cancellationToken,
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? frozenTfReferenceForRuleset = null) =>
        RunCoreAsync(openIndependentConnection, true, observations, plan, frozenTfReferenceForRuleset, cancellationToken);

    private static async Task<V9AdaptiveCalibrationStudyResult> RunCoreAsync(
        Func<CancellationToken, Task<SqlConnection>> openIndependentConnection, bool ownsConnections,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        V9AdaptiveCalibrationPlan plan,
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? frozenTfReferenceForRuleset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openIndependentConnection);
        ArgumentNullException.ThrowIfNull(observations);
        ValidatePlan(plan);
        // Freeze mutable collections before discovery or any asynchronous boundary.
        observations = Array.AsReadOnly(observations.ToArray());
        plan = plan with { ModelOptions = Array.AsReadOnly(plan.ModelOptions.ToArray()),
            ConflictMargins = Array.AsReadOnly(plan.ConflictMargins.ToArray()),
            RiskCeilings = plan.RiskCeilings is null ? null : Array.AsReadOnly(plan.RiskCeilings.ToArray()),
            DeclaredSearchSpace = plan.DeclaredSearchSpace is null ? null : plan.DeclaredSearchSpace with
                { Catalogue = plan.DeclaredSearchSpace.CanonicalCatalogue() },
            BaselineRuleSets = plan.BaselineRuleSets is null ? null
                : CombineCandidateRuleSets([], plan.BaselineRuleSets) };
        if (observations.Any(x => x is null || x.Partition is not ("TRAIN" or "VALIDATION"))
            || observations.Select(x => x.ObservationId).Distinct().Count() != observations.Count)
            throw new InvalidDataException("Coordenador exige TRAIN/VALIDATION sem TEST ou IDs duplicados.");
        if (plan.ObservationCapDeclaration is { } declaration
            && observations.GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
                .Any(g => g.Count() > declaration.MaximumObservationsPerBaseIdentity))
            throw new InvalidDataException("Frame excede o cap populacional pré-declarado.");
        var train = observations.Where(x => x.Partition == "TRAIN").ToArray();
        var validation = observations.Where(x => x.Partition == "VALIDATION").ToArray();
        if (validation.Length < 2 || train.Select(x => x.BasePersonId).Intersect(
                validation.Select(x => x.BasePersonId), StringComparer.Ordinal).Any())
            throw new InvalidDataException("VALIDATION ausente ou vazamento de identidade TRAIN/VALIDATION.");
        if (plan.ModelOptions.Any(x => x.TfWeight > 0) && frozenTfReferenceForRuleset is null)
            throw new InvalidDataException("Busca TF positiva exige referência imutável por frame SQL.");
        var discovery = V9TrainBlockingCandidateDiscovery.Discover(train, plan.Discovery);
        V9CandidateFrameEquivalence.Report? coverage = null;
        V9PhysicalCatalogueCensus? census = null;
        IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> candidates;
        int compared;
        if (plan.CollapseEquivalentCandidateFrames)
        {
            // Count the ENTIRE family before quotienting. Baselines are counted
            // conservatively even when already present in the declared lattice.
            var fullCount = checked((long)plan.DeclaredSearchSpace!.Count()
                + (plan.BaselineRuleSets?.Count ?? 0));
            compared = CountComparedConfigurations(plan, fullCount);
            var censusConnection = await openIndependentConnection(cancellationToken);
            try
            {
                census = await V9PhysicalSqlAdaptiveCalibrationProbe.ReadCatalogueEdgeMasksAsync(
                    censusConnection, observations, plan.DeclaredSearchSpace, plan.Execution, cancellationToken);
            }
            finally { if (ownsConnections) await censusConnection.DisposeAsync(); }
            coverage = V9CandidateFrameEquivalence.Partition(plan.DeclaredSearchSpace, census.EdgeMasks, cancellationToken);
            candidates = CombineCandidateRuleSets(coverage.Classes.Select(x => x.Representative), plan.BaselineRuleSets);
        }
        else
        {
            candidates = CombineCandidateRuleSets(plan.DeclaredSearchSpace is null ? discovery.Candidates
                : plan.DeclaredSearchSpace.Enumerate(), plan.BaselineRuleSets);
            compared = CountComparedConfigurations(plan, candidates.Count);
        }
        var totalRepresentatives = candidates.Count;
        if (plan.ShardCount > totalRepresentatives)
            throw new InvalidDataException("Mais shards que representantes; não gerar shards vazios.");
        candidates = Array.AsReadOnly(candidates.Where((_, index) => index % plan.ShardCount == plan.ShardIndex).ToArray());
        var trainSha = InputSha(train);
        var validationSha = InputSha(validation);
        var measurements = new List<V9PhysicalSourceResolveMetric>();
        var frozen = new List<V9PhysicalFrozenConfiguration>();
        var timings = new List<V9PhysicalEndToEndMeasurement>();
        var fanout = new Dictionary<string, double>(StringComparer.Ordinal);
        var rejected = new List<V9AdaptiveCandidateRejection>();
        var failedOptions = new List<V9BootstrapPareto2D.CandidateOption>();
        var eligibleSources = validation.GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Gestor).Distinct(StringComparer.Ordinal).Skip(1).Any()).Sum(g => (long)g.Count());
        if (eligibleSources == 0) throw new InvalidDataException("VALIDATION sem fontes elegíveis pré-blocking.");
        // Every worker owns a SQL session: local #temp tables cannot race. The
        // immutable frame/family count is shared; results fold in catalogue order.
        var activeConnections = new HashSet<SqlConnection>(ReferenceEqualityComparer.Instance);
        using var referenceLease = new SemaphoreSlim(1);
        var runs = await V9ParallelWork.MapAsync(candidates, plan.MaximumParallelRuleSets, async (passes, ct) =>
        {
            FrozenIbgeNominalTfV9? reference = null;
            if (frozenTfReferenceForRuleset is not null)
            {
                await referenceLease.WaitAsync(ct);
                try { reference = await frozenTfReferenceForRuleset(passes, ct); }
                finally { referenceLease.Release(); }
            }
            var worker = await openIndependentConnection(ct)
                ?? throw new InvalidDataException("Fábrica não forneceu conexão SQL.");
            lock (activeConnections)
                if (!activeConnections.Add(worker))
                    throw new InvalidDataException("Conexão SQL compartilhada entre workers é proibida.");
            try { return await EvaluateRulesetAsync(worker, observations, plan, passes, compared, eligibleSources, reference, ct); }
            finally
            {
                lock (activeConnections) activeConnections.Remove(worker);
                if (ownsConnections) await worker.DisposeAsync();
            }
        }, cancellationToken);
        foreach (var run in runs)
        {
            measurements.AddRange(run.Measurements); frozen.AddRange(run.Frozen); timings.AddRange(run.Timings);
            foreach (var pair in run.Fanout) fanout.Add(pair.Key, pair.Value);
            rejected.AddRange(run.Rejected); failedOptions.AddRange(run.FailedOptions);
        }
        V9ParetoSourceSelectionReport? selection = null;
        if (measurements.Count > 0)
        {
            var fittedByKey = frozen.ToDictionary(x => (x.OptionId, x.ConflictMargin));
            var timingByKey = timings.ToDictionary(x => (x.OptionId, x.ConflictMargin));
            var source = V9PostResolveAdaptiveSelector.EvaluateValidation("VALIDATION", false,
                plan.Budget, measurements, fanout);
            var evidence = measurements.Select(row =>
            {
                var key = (row.OptionId, row.ConflictMargin);
                var snapshot = fittedByKey[key].Snapshot;
                timingByKey.TryGetValue(key, out var time);
                return new V9ParetoPhysicalEvidence(row.OptionId, row.CandidateGrouping, row.ConflictMargin,
                    snapshot.ContentSha256, trainSha, validationSha, source.EvidenceFingerprintSha256,
                    time?.ProtocolSha256 ?? V9PhysicalAdaptiveExecution.EndToEndProtocolSha256,
                    time?.EnvironmentSha256 ?? V9PhysicalAdaptiveExecution.CurrentEnvironmentSha256(),
                    time?.Quantiles.P95Milliseconds, time is not null);
            }).ToArray();
            selection = V9ParetoSourceSelection.EvaluateValidation("VALIDATION", false, plan.Budget,
                measurements, fanout, evidence, plan.SelectionPolicy);
        }
        var pareto = V9BootstrapPareto2D.Evaluate(
            (selection?.Pareto.Table.Select(x => x.Option) ?? []).Concat(failedOptions), plan.SelectionPolicy);
        if (plan.ShardCount > 1)
            pareto = pareto with { InitialSuggestedOptionId = null, Status = "PARTIAL_SEARCH_NO_GLOBAL_SUGGESTION" };
        return new(Version, Sha(JsonSerializer.Serialize(plan)), trainSha, validationSha,
            compared, discovery, selection, pareto, measurements.AsReadOnly(), frozen.AsReadOnly(),
            timings.AsReadOnly(), rejected.AsReadOnly(), candidates.Count,
            plan.RiskCeilings is null ? null : V9RiskBandFrontier.Evaluate("VALIDATION", false,
                pareto.Table.Select(x => x.Option), plan.RiskCeilings, plan.SelectionPolicy),
            plan.SampleDesign is null ? null : V9StatisticalSamplePlan.Calculate(plan.SampleDesign, plan.RiskCeilings!),
            coverage, census, new(Sha(JsonSerializer.Serialize(plan with { ShardIndex = 0 })),
                plan.ShardIndex, plan.ShardCount, totalRepresentatives, candidates.Count, plan.ShardCount == 1));
    }

    private sealed record RulesetResult(
        List<V9PhysicalSourceResolveMetric> Measurements, List<V9PhysicalFrozenConfiguration> Frozen,
        List<V9PhysicalEndToEndMeasurement> Timings, Dictionary<string, double> Fanout,
        List<V9AdaptiveCandidateRejection> Rejected, List<V9BootstrapPareto2D.CandidateOption> FailedOptions);

    private static async Task<RulesetResult> EvaluateRulesetAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations, V9AdaptiveCalibrationPlan plan,
        IReadOnlyList<LinkageBlockingPass> passes, int compared, long eligibleSources,
        FrozenIbgeNominalTfV9? pinnedTfReference, CancellationToken cancellationToken)
    {
        var measurements = new List<V9PhysicalSourceResolveMetric>();
        var frozen = new List<V9PhysicalFrozenConfiguration>();
        var timings = new List<V9PhysicalEndToEndMeasurement>();
        var fanout = new Dictionary<string, double>(StringComparer.Ordinal);
        var rejected = new List<V9AdaptiveCandidateRejection>();
        var failedOptions = new List<V9BootstrapPareto2D.CandidateOption>();
        cancellationToken.ThrowIfCancellationRequested();
        var rulesetSha = Sha(JsonSerializer.Serialize(passes));
        var options = plan.ModelOptions.Select(x => x with { OptionId = rulesetSha + ":" + x.OptionId }).ToArray();
        // The legacy family adapter's 64-option working set is only a batch size.
        // Every option and every margin reaches physical evaluation; no best-only screen.
        foreach (var batch in options.Chunk(64))
        foreach (var margins in plan.ConflictMargins.Chunk(8))
        {
            try
            {
                var run = await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(connection, observations,
                    passes, batch, plan.Budget, plan.StudyFingerprintSha256, cancellationToken,
                    pinnedTfReference, evaluateFinalResolve: true,
                    finalConflictMargins: margins, includeFsBlindGrouping: true,
                    minimumFsGroupingPosterior: plan.MinimumGroupingPosterior,
                    predeclaredMaximumAcceptedPerBaseIdentity: plan.MaximumAcceptedPerBaseIdentity,
                    adaptiveExecution: plan.Execution with { TotalComparedConfigurations = compared });
                measurements.AddRange(run.SourceResolveValidation!);
                frozen.AddRange(run.FrozenConfigurations!);
                timings.AddRange(run.EndToEndMeasurements!);
                foreach (var option in run.CalibrationReport.Options)
                    fanout[option.OptionId] = option.Validation.BlockingFalseDegreeP99;
            }
            catch (InvalidDataException exception) when
                (exception.Data[V9PhysicalSqlAdaptiveCalibrationProbe.CandidateRejectionKey] is true)
            {
                var ids = batch.SelectMany(option => margins.Select(margin =>
                    V9ParetoSourceSelection.ConfigurationId(option.OptionId, Grouping, margin))).ToArray();
                rejected.Add(new(rulesetSha, ids, exception.Message));
                failedOptions.AddRange(ids.Select(id => new V9BootstrapPareto2D.CandidateOption(
                    id, Sha(id), 0, null, null, 0, 0, eligibleSources,
                    observations.Where(x => x.Partition == "VALIDATION").Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal).Count(),
                    false, false, MultiplicityComparedConfigurations: compared,
                    EvidenceRejectionReasons: ["CANDIDATO_FISICO_NAO_MENSURAVEL: " + exception.Message])));
            }
        }
        return new(measurements, frozen, timings, fanout, rejected, failedOptions);
    }

    private static string InputSha(IEnumerable<V9PhysicalCalibrationObservation> observations) =>
        Sha(JsonSerializer.Serialize(observations.OrderBy(x => x.ObservationId).ToArray()));

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

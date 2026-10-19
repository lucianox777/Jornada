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
    IReadOnlyList<IReadOnlyList<LinkageBlockingPass>>? BaselineRuleSets = null);

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
    int TotalCandidateRuleSets = 0);

/// <summary>
/// One C# calibration path: TRAIN proposal discovery, full physical candidate union,
/// TRAIN-specific m/u and TF, held-out source Rank/Resolve, and global 2D Pareto.
/// Batching does not prune options or reset multiplicity. No TEST, publication or activation.
/// </summary>
public static class V9AdaptiveCalibrationCoordinator
{
    public const string Version = "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1";
    public const string Grouping = "BLIND_FS_COMPLETE_LINK_CANDIDATES";

    public static void ValidatePlan(V9AdaptiveCalibrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.StudyFingerprintSha256 is not { Length: 64 }
            || !plan.StudyFingerprintSha256.All(Uri.IsHexDigit)
            || plan.Discovery is null || plan.ModelOptions is null || plan.Execution is null
            || plan.ConflictMargins is null || plan.Budget is null || plan.SelectionPolicy is null
            || plan.MaximumPhysicalRuleSets < 1 || plan.MaximumComparedConfigurations < 1
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
        if (plan.BaselineRuleSets is not null)
            _ = CombineCandidateRuleSets([], plan.BaselineRuleSets);
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
            if (ruleset is null || ruleset.Count is < 1 or > 64
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

    public static async Task<V9AdaptiveCalibrationStudyResult> RunAsync(SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        V9AdaptiveCalibrationPlan plan, CancellationToken cancellationToken,
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? frozenTfReferenceForRuleset = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(observations);
        ValidatePlan(plan);
        // Freeze mutable collections before discovery or any asynchronous boundary.
        observations = Array.AsReadOnly(observations.ToArray());
        plan = plan with { ModelOptions = Array.AsReadOnly(plan.ModelOptions.ToArray()),
            ConflictMargins = Array.AsReadOnly(plan.ConflictMargins.ToArray()),
            BaselineRuleSets = plan.BaselineRuleSets is null ? null
                : CombineCandidateRuleSets([], plan.BaselineRuleSets) };
        if (observations.Any(x => x is null || x.Partition is not ("TRAIN" or "VALIDATION"))
            || observations.Select(x => x.ObservationId).Distinct().Count() != observations.Count)
            throw new InvalidDataException("Coordenador exige TRAIN/VALIDATION sem TEST ou IDs duplicados.");
        var train = observations.Where(x => x.Partition == "TRAIN").ToArray();
        var validation = observations.Where(x => x.Partition == "VALIDATION").ToArray();
        if (validation.Length < 2 || train.Select(x => x.BasePersonId).Intersect(
                validation.Select(x => x.BasePersonId), StringComparer.Ordinal).Any())
            throw new InvalidDataException("VALIDATION ausente ou vazamento de identidade TRAIN/VALIDATION.");
        if (plan.ModelOptions.Any(x => x.TfWeight > 0) && frozenTfReferenceForRuleset is null)
            throw new InvalidDataException("Busca TF positiva exige referência imutável por frame SQL.");
        var discovery = V9TrainBlockingCandidateDiscovery.Discover(train, plan.Discovery);
        var candidates = CombineCandidateRuleSets(discovery.Candidates, plan.BaselineRuleSets);
        var compared = CountComparedConfigurations(plan, candidates.Count);
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
        foreach (var passes in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rulesetSha = Sha(JsonSerializer.Serialize(passes));
            var options = plan.ModelOptions.Select(x => x with { OptionId = rulesetSha + ":" + x.OptionId }).ToArray();
            // Resolve a published reference once, before any option/margin batch.
            // A provider must never change the reference midway through this ruleset.
            var pinnedTfReference = frozenTfReferenceForRuleset is null ? null
                : await frozenTfReferenceForRuleset(passes, cancellationToken);
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
                        validation.Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal).Count(),
                        false, false, MultiplicityComparedConfigurations: compared,
                        EvidenceRejectionReasons: ["CANDIDATO_FISICO_NAO_MENSURAVEL: " + exception.Message])));
                }
            }
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
        return new(Version, Sha(JsonSerializer.Serialize(plan)), trainSha, validationSha,
            compared, discovery, selection, pareto, measurements.AsReadOnly(), frozen.AsReadOnly(),
            timings.AsReadOnly(), rejected.AsReadOnly(), candidates.Count);
    }

    private static string InputSha(IEnumerable<V9PhysicalCalibrationObservation> observations) =>
        Sha(JsonSerializer.Serialize(observations.OrderBy(x => x.ObservationId).ToArray()));

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

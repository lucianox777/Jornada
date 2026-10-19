namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// One source-observation post-Resolve screen for an FS-formed CANDIDATE
/// IDENTITY (never a raw observation or truth-oracle Gold grouping).
/// All rates use eligible sources BEFORE blocking as recall denominator;
/// false discovery uses the final RESOLVIDO decisions only.
/// </summary>
public sealed record V9PostResolveOptionScreen(
    string OptionId, decimal Threshold, decimal TfWeight,
    decimal ConflictMargin, string CandidateGrouping,
    long EligibleSourceObservationsBeforeBlocking, long TrueAccepted,
    long FalseAccepted, long Conflicts, long Unresolved,
    double GlobalSourceRecall, double? FinalAcceptedFdrPoint,
    double? ConditionalClusterFdrUpper,
    bool MeetsRecallFloor, bool MeetsPointFdrCeiling,
    bool MeetsConditionalClusterFdrCeiling,
    bool IdentityIidIndependentlyAttested,
    bool MeetsPositiveMarginPolicy,
    bool EligibleForDescriptiveSelection);

/// <summary>
/// Manual TRAIN->VALIDATION diagnostic: NOTHING is certified or activated.
/// A suggestion must survive post-Resolve recall and both FDR gates on
/// independently evaluated identity groupings; synthetic current reports
/// fail closed because cluster-IID is NOT independently attested.
/// </summary>
public sealed record V9PostResolveSourceSelectionReport(
    string Version, string StudyFingerprintSha256, string Status,
    string SelectedGrouping,
    decimal MinimumConflictMargin,
    double MinimumGlobalSourceRecall,
    double MaximumFinalAcceptedFdr,
    IReadOnlyList<V9PostResolveOptionScreen> Options,
    string? SuggestedOptionId,
    decimal? SuggestedConflictMargin,
    bool TestUsed,
    bool IndependenceProved,
    bool OperationalModelCreated,
    bool AutomaticallyActivated);

public static class V9PostResolveSourceDiagnosticSelector
{
    public const string Version = "V9_POST_RESOLVE_SOURCE_VALIDATION_SELECTOR_DIAGNOSTIC_V1";
    public const string SelectedGrouping = "BLIND_FS_COMPLETE_LINK_CANDIDATES";

    public static V9PostResolveSourceSelectionReport Evaluate(
        IReadOnlyCollection<V9PhysicalSourceResolveMetric> measured,
        AdaptiveCalibrationPreviewBudget budget,
        decimal minimumConflictMargin = .5m)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.Version != AdaptiveCalibrationPreviewBudget.CurrentVersion
            || budget.StudyFingerprintSha256.Length != 64
            || budget.StudyFingerprintSha256.Any(x => !Uri.IsHexDigit(x))
            || !double.IsFinite(budget.MinimumGlobalAcceptedRecall)
            || budget.MinimumGlobalAcceptedRecall is < 0 or > 1
            || !double.IsFinite(budget.MaximumObservedAcceptedFdrPoint)
            || budget.MaximumObservedAcceptedFdrPoint is < 0 or > .01
            || minimumConflictMargin is <= 0m or > 1000m)
            throw new InvalidDataException(
                "Política de seleção pós-Resolve inválida ou margem de conflito desabilitada.");

        var input = measured.ToArray();
        if (input.Length == 0)
            throw new InvalidDataException("VALIDATION pós-Resolve física é obrigatória.");
        var unique = new HashSet<(string Option, string Grouping, decimal Margin)>();
        var eligible = input[0].EligibleSourceObservationsBeforeBlocking;
        foreach (var metric in input)
        {
            if (metric is null || string.IsNullOrWhiteSpace(metric.OptionId)
                || string.IsNullOrWhiteSpace(metric.CandidateGrouping)
                || metric.AllValidationObservations <= 0
                || metric.EligibleSourceObservationsBeforeBlocking <= 0
                || metric.EligibleSourceObservationsBeforeBlocking != eligible
                || metric.EligibleSourceObservationsBeforeBlocking > metric.AllValidationObservations
                || metric.EligibleSourceObservationsWithTrueSqlCandidate
                    > metric.EligibleSourceObservationsBeforeBlocking
                || metric.TrueAcceptedSourceObservations < 0
                || metric.FalseAcceptedSourceObservations < 0
                || metric.Conflicts < 0 || metric.Unresolved < 0
                || metric.TrueAcceptedSourceObservations >
                    metric.EligibleSourceObservationsWithTrueSqlCandidate
                || metric.TrueAcceptedSourceObservations + metric.FalseAcceptedSourceObservations
                    + metric.Conflicts + metric.Unresolved != metric.AllValidationObservations
                || metric.SyntheticGoldOracleGrouping
                    && metric.CandidateGrouping == SelectedGrouping
                || metric.OperationallyCertified
                || metric.ConflictMargin < 0m
                || !double.IsFinite(metric.GlobalSourceRecall)
                || Math.Abs(metric.GlobalSourceRecall -
                    (double)metric.TrueAcceptedSourceObservations / eligible) > 1e-10
                || (metric.FdrAfterResolve is { } fdr
                    && (!double.IsFinite(fdr) || fdr < 0d || fdr > 1d))
                || (metric.ConditionalClusterCpFdrUpper is { } upper
                    && (!double.IsFinite(upper) || upper < 0d || upper > 1d))
                || !unique.Add((metric.OptionId, metric.CandidateGrouping,
                    metric.ConflictMargin)))
                throw new InvalidDataException("Métrica pós-Resolve inválida, misturada ou duplicada.");

            var accepted = metric.TrueAcceptedSourceObservations
                + metric.FalseAcceptedSourceObservations;
            if ((accepted == 0) != (metric.FdrAfterResolve is null)
                || (accepted > 0 && Math.Abs(
                    metric.FdrAfterResolve!.Value
                    - (double)metric.FalseAcceptedSourceObservations / accepted) > 1e-10))
                throw new InvalidDataException("FDR pós-Resolve não reconcilia com os aceites.");
        }

        var screens = input.Where(x => x.CandidateGrouping == SelectedGrouping
                && !x.SyntheticGoldOracleGrouping)
            .OrderBy(x => x.OptionId, StringComparer.Ordinal)
            .ThenBy(x => x.ConflictMargin)
            .Select(x =>
            {
                var recall = x.GlobalSourceRecall >= budget.MinimumGlobalAcceptedRecall;
                var point = x.FdrAfterResolve is { } fdr
                    && fdr <= budget.MaximumObservedAcceptedFdrPoint;
                var bound = x.ConditionalClusterCpFdrUpper is { } upper
                    && upper <= budget.MaximumObservedAcceptedFdrPoint;
                var margin = x.ConflictMargin >= minimumConflictMargin;
                var valid = recall && point && bound && margin
                    && x.ClusterIidIndependentlyValidated;
                return new V9PostResolveOptionScreen(
                    x.OptionId, x.Threshold, x.TfWeight, x.ConflictMargin,
                    x.CandidateGrouping,
                    x.EligibleSourceObservationsBeforeBlocking,
                    x.TrueAcceptedSourceObservations,
                    x.FalseAcceptedSourceObservations,
                    x.Conflicts, x.Unresolved,
                    x.GlobalSourceRecall, x.FdrAfterResolve,
                    x.ConditionalClusterCpFdrUpper,
                    recall, point, bound,
                    x.ClusterIidIndependentlyValidated,
                    margin, valid);
            }).ToArray();
        var candidate = screens.Where(x => x.EligibleForDescriptiveSelection)
            .OrderByDescending(x => x.GlobalSourceRecall)
            .ThenBy(x => x.ConditionalClusterFdrUpper)
            .ThenBy(x => x.FinalAcceptedFdrPoint)
            .ThenByDescending(x => x.ConflictMargin)
            .ThenBy(x => x.OptionId, StringComparer.Ordinal)
            .FirstOrDefault();

        var status = screens.Length == 0
            ? "NO_FS_IDENTITY_GROUPING_MEASURED"
            : candidate is not null
                ? "DESCRIPTIVE_POST_RESOLVE_SUGGESTION_NOT_OPERATIONAL"
                : screens.All(x => !x.IdentityIidIndependentlyAttested)
                    ? "NO_SUGGESTION_CLUSTER_INDEPENDENCE_UNPROVEN"
                    : "NO_SUGGESTION_POST_RESOLVE_FDR_RECALL_OR_MARGIN_GATES";
        return new V9PostResolveSourceSelectionReport(
            Version, budget.StudyFingerprintSha256, status,
            SelectedGrouping, minimumConflictMargin,
            budget.MinimumGlobalAcceptedRecall,
            budget.MaximumObservedAcceptedFdrPoint,
            Array.AsReadOnly(screens),
            candidate?.OptionId, candidate?.ConflictMargin,
            TestUsed: false,
            IndependenceProved: screens.Length > 0
                && screens.All(x => x.IdentityIidIndependentlyAttested),
            OperationalModelCreated: false,
            AutomaticallyActivated: false);
    }
}

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Deriva a proveniência das features diretamente do ResolutionProjectionPlan.
/// Como cada ResolutionProjectedFeature já declara SourceAttribute, o anti-leakage
/// não precisa manter um catálogo paralelo nem inferir proveniência pelo nome final.
/// </summary>
public static class ResolutionProjectionGroundTruthLineage
{
    public static IReadOnlyList<GroundTruthFeatureLineage> All(
        ResolutionProjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Features
            .Select(static feature => GroundTruthFeatureLineage.Direct(
                feature.Feature,
                feature.SourceAttribute))
            .OrderBy(static lineage => lineage.FeatureName, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<GroundTruthFeatureLineage> BlockingCandidates(
        ResolutionProjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Features
            .Where(static feature => feature.CandidateForBlocking)
            .Select(static feature => GroundTruthFeatureLineage.Direct(
                feature.Feature,
                feature.SourceAttribute))
            .OrderBy(static lineage => lineage.FeatureName, StringComparer.Ordinal)
            .ToArray();
    }

    public static void EnsureBlockingCandidatesDoNotLeak(
        ResolutionProjectionPlan plan,
        GroundTruthSource labelSource) =>
        GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(
            labelSource,
            BlockingCandidates(plan));
}

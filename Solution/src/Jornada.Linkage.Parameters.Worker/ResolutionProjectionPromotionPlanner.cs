using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum ResolutionFeatureLifecycle
{
    Source,
    Candidate,
    Promoted,
    Indexed
}

public sealed record ResolutionFeaturePromotion(
    string Feature,
    string SourceAttribute,
    ResolutionFeatureOrigin Origin,
    ResolutionFeatureLifecycle Lifecycle,
    ResolutionMaterializationKind Materialization,
    bool MultiValued,
    string? Algorithm);

public sealed record ResolutionIndexProposal(
    string Feature,
    bool MultiValued,
    string Reason);

/// <summary>
/// Plano físico provider-independent produzido depois da escolha estatística dos passes.
/// O plano lógico de blocking não depende do índice. Índices compostos, quando necessários,
/// são uma otimização posterior dos poucos passes vencedores e não participam da busca combinatória.
/// </summary>
public sealed record ResolutionPhysicalProjectionPlan(
    string ProjectionFingerprint,
    IReadOnlyList<ResolutionFeaturePromotion> Features,
    IReadOnlyList<ResolutionIndexProposal> SimpleIndexes,
    IReadOnlyList<LinkageBlockingPass> WinningPasses);

public static class ResolutionProjectionPromotionPlanner
{
    public const string MethodVersion = "RESOLUTION_PROJECTION_PROMOTION_PLANNER_V2";

    public static ResolutionPhysicalProjectionPlan Build(
        ResolutionProjectionPlan projection,
        IReadOnlyList<LinkageBlockingPass> winningPasses,
        IReadOnlyCollection<string>? previouslyPromotedFeatures = null,
        IReadOnlyCollection<string>? physicallyValidatedIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(winningPasses);

        var known = projection.Features
            .Select(static feature => feature.Feature)
            .ToHashSet(StringComparer.Ordinal);
        var selected = winningPasses
            .SelectMany(static pass => pass.Fields)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = selected.Where(feature => !known.Contains(feature)).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"O BLOCKING_PLAN referencia features fora da projeção: {string.Join(",", unknown)}.", nameof(winningPasses));

        var previous = previouslyPromotedFeatures is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : previouslyPromotedFeatures.ToHashSet(StringComparer.Ordinal);
        var validatedIndexes = physicallyValidatedIndexes is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : physicallyValidatedIndexes.ToHashSet(StringComparer.Ordinal);

        var unknownIndexes = validatedIndexes.Where(feature => !known.Contains(feature)).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        if (unknownIndexes.Length > 0)
            throw new ArgumentException($"Índice validado referencia feature fora da projeção: {string.Join(",", unknownIndexes)}.", nameof(physicallyValidatedIndexes));

        var indexesOutsideWinningPlan = validatedIndexes.Where(feature => !selected.Contains(feature)).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        if (indexesOutsideWinningPlan.Length > 0)
            throw new ArgumentException($"Índice só pode ser promovido após evidência física para feature do BLOCKING_PLAN vencedor: {string.Join(",", indexesOutsideWinningPlan)}.", nameof(physicallyValidatedIndexes));

        var promotions = projection.Features
            .Select(feature => Promote(feature, selected, previous, validatedIndexes))
            .OrderBy(static feature => feature.Feature, StringComparer.Ordinal)
            .ToArray();

        var indexes = promotions
            .Where(static feature => feature.Lifecycle == ResolutionFeatureLifecycle.Indexed)
            .Select(static feature => new ResolutionIndexProposal(
                feature.Feature,
                feature.MultiValued,
                "MEASURED_PHYSICAL_BENEFIT"))
            .OrderBy(static proposal => proposal.Feature, StringComparer.Ordinal)
            .ToArray();

        return new ResolutionPhysicalProjectionPlan(
            projection.Fingerprint,
            promotions,
            indexes,
            winningPasses.ToArray());
    }

    private static ResolutionFeaturePromotion Promote(
        ResolutionProjectedFeature feature,
        IReadOnlySet<string> selected,
        IReadOnlySet<string> previous,
        IReadOnlySet<string> validatedIndexes)
    {
        var lifecycle = feature.Origin == ResolutionFeatureOrigin.Original
            ? ResolutionFeatureLifecycle.Source
            : validatedIndexes.Contains(feature.Feature)
                ? ResolutionFeatureLifecycle.Indexed
                : selected.Contains(feature.Feature) || previous.Contains(feature.Feature)
                    ? ResolutionFeatureLifecycle.Promoted
                    : ResolutionFeatureLifecycle.Candidate;

        return new ResolutionFeaturePromotion(
            feature.Feature,
            feature.SourceAttribute,
            feature.Origin,
            lifecycle,
            feature.Materialization,
            feature.MultiValued,
            feature.Algorithm);
    }
}

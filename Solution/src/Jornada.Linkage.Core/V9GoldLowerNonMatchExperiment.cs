using System.Globalization;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Harness-only adapter. It NEVER makes the V9 classifier operational: the
/// original Rank/Resolve stays authoritative, and SQL may interpret the special
/// reason only in a positively identified disposable Gold TRAIN database.
/// </summary>
public static class V9GoldLowerNonMatchExperiment
{
    public const string ArgumentPrefix = "LOWER_NONMATCH=";
    public const string PublicationReason = "LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL";

    public static decimal ParseThreshold(string argument, decimal fixedUpperThreshold)
    {
        if (argument is null || !argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal)
            || !decimal.TryParse(argument.AsSpan(ArgumentPrefix.Length),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var lower)
            || lower <= 0m || lower >= fixedUpperThreshold || fixedUpperThreshold > 1m)
            throw new ArgumentException(
                "LOWER_NONMATCH exige decimal invariante com 0 < limiar inferior < limiar superior congelado.");
        return lower;
    }

    public static ProbabilisticLinkageDecision Apply(
        ProbabilisticLinkageDecision original,
        V9LowerNonMatchPreview.VerifiedRetrieval retrieval,
        string expectedSnapshotSha256,
        decimal lowerThreshold,
        decimal frozenUpperThreshold)
    {
        var preview = V9LowerNonMatchPreview.Evaluate(
            original, retrieval, expectedSnapshotSha256, lowerThreshold, frozenUpperThreshold);
        // This is a publication-policy proposal, not a second scorer and not a
        // "SEM_CANDIDATO" result: physical candidates really were retrieved.
        // The preview always retains AllowedToPublishNewReference=false.
        return preview.Decision == V9LowerNonMatchPreview.Disposition.CandidateRejectedNewReferenceHypothesis
            ? original with { Motivo = PublicationReason }
            : original;
    }
}

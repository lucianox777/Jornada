namespace Jornada.Contracts;

/// <summary>
/// Candidate, mutually exclusive nominal levels for the V9 calibration contract.
/// These values are NOT calibrated Fellegi-Sunter states yet: no m/u, TF or
/// operational score may be inferred from their names or ordinal values.
/// </summary>
public enum CompositeNominalLevelV9
{
    Missing,
    ExactFull,
    ExactFirstLast,
    ReversedFirstLast,
    HighFull,
    HighFirstLast,
    ExactLast,
    ExactFirst,
    Other
}

/// <summary>
/// Pure V9 nominal feature/classification proposal for person and mother names.
/// Preserves the whole normalized name, including middle tokens and agnomes;
/// components qualify a SINGLE comparison level, never additive evidence.
/// Explicitly NOT connected to Rank/Resolve/GENERATE_DRAFT until calibration.
/// </summary>
public static class CompositeNominalComparisonV9
{
    public const string ContractVersion = "PTBR_COMPOSITE_FULL_FIRST_LAST_V9_DRAFT_1";

    // Experimental thresholds carried from the existing comparator family.
    // They do not imply Splink numeric parity or an approved calibrated model.
    public const double HighSimilarityThreshold = 0.92d;

    public static CompositeNominalLevelV9 Classify(string? left, string? right)
    {
        var a = BrazilianNameComponents.Project(left);
        var b = BrazilianNameComponents.Project(right);
        if (a is null || b is null)
            return CompositeNominalLevelV9.Missing;

        // Exact is on the INTEGRAL name, not "first + last". An omitted
        // middle name or a changed agnome must never become ExactFull.
        if (string.Equals(a.NormalizedFull, b.NormalizedFull, StringComparison.Ordinal))
            return CompositeNominalLevelV9.ExactFull;

        var firstA = FirstName(a);
        var firstB = FirstName(b);
        var lastA = a.LastContentSurname;
        var lastB = b.LastContentSurname;
        var firstExact = EqualPresent(firstA, firstB);
        var lastExact = EqualPresent(lastA, lastB);

        if (firstExact && lastExact)
            return CompositeNominalLevelV9.ExactFirstLast;

        if (EqualPresent(firstA, lastB) && EqualPresent(lastA, firstB)
            && !EqualPresent(firstA, lastA))
            return CompositeNominalLevelV9.ReversedFirstLast;

        if (IdentityComparison.JaroWinkler(a.NormalizedFull, b.NormalizedFull)
            >= HighSimilarityThreshold)
            return CompositeNominalLevelV9.HighFull;

        // Do not invent a last surname for a single-token name; do not
        // interpret a missing surname as a matching surname.
        if (firstA is not null && firstB is not null
            && lastA is not null && lastB is not null
            && IdentityComparison.JaroWinkler(firstA, firstB) >= HighSimilarityThreshold
            && IdentityComparison.JaroWinkler(lastA, lastB) >= HighSimilarityThreshold)
            return CompositeNominalLevelV9.HighFirstLast;

        if (lastExact)
            return CompositeNominalLevelV9.ExactLast;
        if (firstExact)
            return CompositeNominalLevelV9.ExactFirst;
        return CompositeNominalLevelV9.Other;
    }

    private static bool EqualPresent(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);

    private static string? FirstName(BrazilianNameComponentProjection name)
    {
        var tokens = name.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Project() recognizes an optional title; exclude it only from the
        // auxiliary first-name component, never from the full name.
        var index = name.TitlePrefix is null ? 0 : 1;
        if (index >= tokens.Length)
            return null;
        var token = tokens[index].Trim('.', ',');
        return token.Length == 0 ? null : token;
    }
}

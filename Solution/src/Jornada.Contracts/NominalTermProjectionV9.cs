namespace Jornada.Contracts;

/// <summary>
/// Versioned, non-destructive nominal term projection for V9 calibration and
/// blocking diagnostics. A title is not a given name; surname is auxiliary.
/// This is not a TF estimator and must not fabricate surname frequencies.
/// </summary>
public static class NominalTermProjectionV9
{
    public const string ProjectionVersion = "NOMINAL_TERM_PROJECTION_V9_1";
    public const string ComponentsVersion = BrazilianNameComponents.MethodVersion;

    public static NominalTermTokensV9? Project(string? fullName)
    {
        var components = BrazilianNameComponents.Project(fullName);
        if (components is null)
            return null;

        var tokens = components.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstIndex = components.TitlePrefix is null ? 0 : 1;
        if (firstIndex >= tokens.Length)
            return null;

        var first = tokens[firstIndex].Trim('.', ',');
        if (first.Length == 0)
            return null;

        return new NominalTermTokensV9(
            components.NormalizedFull,
            first,
            components.LastContentSurname,
            components.TitlePrefix,
            ProjectionVersion,
            ComponentsVersion);
    }
}

/// <summary>
/// Explicitly separate first-name and last-content-surname units. The two
/// marginals are not a joint distribution and must not be multiplied blindly.
/// </summary>
public sealed record NominalTermTokensV9(
    string NormalizedFull,
    string FirstGivenName,
    string? LastContentSurname,
    string? TitlePrefix,
    string ProjectionVersion,
    string ComponentsVersion);

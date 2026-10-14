using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Typed, diagnostic-only projection for the V9 physical-index laboratory.
/// It does not create SQL objects, select a blocking policy, compute TF, or
/// participate in operational Rank/Resolve. Surname membership is MULTI:
/// never serialize it as one scalar equality key.
/// </summary>
public static class V9DiagnosticIndexProjection
{
    public const string Version = "V9_DIAGNOSTIC_INDEX_PROJECTION_V1";

    public sealed record NameKeys(
        string? Normalized,
        string? WithoutParticles,
        string? Prefix3,
        string? Phonetic,
        string? FirstName,
        string? LastContentSurname,
        IReadOnlyList<string> AnySurnames);

    public sealed record Row(
        string ProjectionVersion,
        string BasicNameVersion,
        string BrazilianComponentsVersion,
        string PhoneticVersion,
        string SurnamePolicyVersion,
        string Partition,
        NameKeys Person,
        NameKeys Mother,
        DateOnly? BirthDate);

    public static Row Project(string partition, string? person, string? mother, DateOnly? birthDate)
    {
        if (partition is not ("TRAIN" or "VALIDATION"))
            throw new InvalidDataException("V9 index diagnostic projections require TRAIN/VALIDATION; TEST is sealed.");

        return new Row(
            Version,
            PersonNameBasicNormalization.MethodVersion,
            BrazilianNameComponents.MethodVersion,
            MetaphoneBr.MethodVersion,
            V9NameRequiredSqlCellAudit.Version,
            partition,
            ProjectName(person),
            ProjectName(mother),
            birthDate);
    }

    private static NameKeys ProjectName(string? fullName)
    {
        var components = BrazilianNameComponents.Project(fullName);
        var basic = PersonNameBasicNormalization.Project(fullName);
        if (components is null || basic is null)
            return new NameKeys(null, null, null, null, null, null, Array.Empty<string>());

        var normalized = components.NormalizedFull;
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstIndex = components.TitlePrefix is null ? 0 : 1;
        var first = firstIndex < tokens.Length ? tokens[firstIndex].Trim('.', ',') : null;
        if (string.IsNullOrEmpty(first))
            first = null;

        return new NameKeys(
            normalized,
            basic.WithoutPortugueseParticles,
            normalized.Length >= 3 ? normalized[..3] : null,
            MetaphoneBr.Encode(fullName),
            first,
            components.LastContentSurname,
            V9NameRequiredSqlCellAudit.AnySurname(fullName));
    }
}

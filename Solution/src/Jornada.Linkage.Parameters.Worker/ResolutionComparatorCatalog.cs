using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum ResolutionComparatorOutputKind
{
    ExactAgreement,
    SimilarityScore
}

/// <summary>
/// Comparadores operam sobre dois valores e, por isso, não produzem coluna calculada.
/// O Calibrador pode escolher projeções e limiares, mas o algoritmo do comparador é uma
/// capacidade universal versionada do sistema.
/// </summary>
public sealed record HomologatedResolutionComparator(
    string Comparator,
    string ComparatorVersion,
    ResolutionComparatorOutputKind OutputKind,
    bool SystemDefault,
    bool CalibratedThresholdAllowed,
    IReadOnlyList<string> ImplementationReferences,
    IReadOnlyList<string> TestReferences)
{
    public string QualifiedComparator => $"{Comparator}@{ComparatorVersion}";
}

public static class HomologatedResolutionComparatorCatalog
{
    public const string CatalogVersion = "RESOLUTION_COMPARATOR_CATALOG_V1";
    public const string ExactOrdinalComparator = "EXACT_ORDINAL";
    public const string ExactOrdinalVersion = "V1";
    public const string JaroWinklerComparator = "JARO_WINKLER";
    public const string JaroWinklerVersion = "V1";

    private static readonly HomologatedResolutionComparator[] Comparators =
    {
        new(
            ExactOrdinalComparator,
            ExactOrdinalVersion,
            ResolutionComparatorOutputKind.ExactAgreement,
            SystemDefault: true,
            CalibratedThresholdAllowed: false,
            new[] { "System.String.Equals(StringComparison.Ordinal)" },
            new[] { "Solution/tests/Jornada.Tests/ResolutionComparatorCatalogTests.cs" }),
        new(
            JaroWinklerComparator,
            JaroWinklerVersion,
            ResolutionComparatorOutputKind.SimilarityScore,
            SystemDefault: true,
            CalibratedThresholdAllowed: true,
            new[] { "Solution/src/Jornada.Contracts/IdentityComparison.cs#JaroWinkler" },
            new[] { "Solution/tests/Jornada.Tests/ResolutionComparatorCatalogTests.cs" })
    };

    static HomologatedResolutionComparatorCatalog()
    {
        var qualified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var comparator in Comparators)
        {
            if (!qualified.Add(comparator.QualifiedComparator))
                throw new InvalidOperationException($"Comparador homologado duplicado: {comparator.QualifiedComparator}.");
            if (comparator.ImplementationReferences.Count == 0 || comparator.TestReferences.Count == 0)
                throw new InvalidOperationException($"{comparator.QualifiedComparator} exige implementação e teste.");
        }
    }

    public static IReadOnlyList<HomologatedResolutionComparator> All => Comparators;

    public static bool TryGet(string comparator, string version, out HomologatedResolutionComparator definition)
    {
        var found = Comparators.SingleOrDefault(candidate =>
            string.Equals(candidate.Comparator, comparator, StringComparison.Ordinal) &&
            string.Equals(candidate.ComparatorVersion, version, StringComparison.Ordinal));
        if (found is null)
        {
            definition = null!;
            return false;
        }

        definition = found;
        return true;
    }

    public static double Evaluate(
        string comparator,
        string version,
        string left,
        string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!TryGet(comparator, version, out _))
            throw new ArgumentOutOfRangeException(nameof(comparator), $"Comparador não homologado: {comparator}@{version}.");

        return comparator switch
        {
            ExactOrdinalComparator => string.Equals(left, right, StringComparison.Ordinal) ? 1d : 0d,
            JaroWinklerComparator => IdentityComparison.JaroWinkler(left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(comparator), comparator, "Comparador sem executor registrado.")
        };
    }
}

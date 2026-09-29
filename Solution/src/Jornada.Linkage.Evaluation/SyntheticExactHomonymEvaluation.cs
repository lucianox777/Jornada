namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Pure synthetic-truth contract for DC-SYN-01. Reserved challenge families are deliberately
/// excluded from population-prevalence estimates; they exist only to measure resistance.
/// </summary>
public static class SyntheticReservedTruthFamilies
{
    public const string ExactDemographicHomonym = "CHALLENGE_EXACT_DEMOGRAPHIC_HOMONYM_V1";

    public static bool IsReservedChallenge(string? family) =>
        string.Equals(family, ExactDemographicHomonym, StringComparison.Ordinal);
}

public sealed record SyntheticTruthDecisionCase(
    string PairId,
    string Partition,
    string Stratum,
    string? ReservedFamily,
    bool SamePerson,
    bool NameExact,
    bool MotherNameExact,
    bool BirthDateExact,
    bool Linked);

public sealed record SyntheticExactHomonymSlice(
    string Partition,
    string Stratum,
    long EligibleDistinctPairs,
    long FalseLinks,
    decimal FalseLinkRate);

public sealed record SyntheticExactHomonymReport(
    string MethodVersion,
    long PopulationPairs,
    long ReservedChallengePairs,
    long ExactDistinctHomonymPairs,
    long ExactDistinctHomonymFalseLinks,
    decimal ExactDistinctHomonymFalseLinkRate,
    IReadOnlyList<SyntheticExactHomonymSlice> ByStratum);

public static class SyntheticExactHomonymEvaluator
{
    public const string MethodVersion = "DC_SYN_01_EXACT_HOMONYM_FALSE_LINK_V1";

    public static SyntheticExactHomonymReport Evaluate(
        IEnumerable<SyntheticTruthDecisionCase> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cases = source.ToArray();

        if (cases.Any(static x => string.IsNullOrWhiteSpace(x.PairId)
            || string.IsNullOrWhiteSpace(x.Partition)
            || string.IsNullOrWhiteSpace(x.Stratum)))
            throw new ArgumentException("PairId, Partition e Stratum são obrigatórios.", nameof(source));

        var duplicate = cases.GroupBy(static x => x.PairId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Par sintético duplicado: {duplicate.Key}.");

        var reserved = cases.Where(static x =>
            SyntheticReservedTruthFamilies.IsReservedChallenge(x.ReservedFamily)).ToArray();
        var population = cases.Where(static x =>
            !SyntheticReservedTruthFamilies.IsReservedChallenge(x.ReservedFamily)).ToArray();

        var exactDistinct = reserved.Where(IsExactDistinctHomonym).ToArray();
        var falseLinks = exactDistinct.LongCount(static x => x.Linked);

        var slices = exactDistinct
            .GroupBy(static x => (x.Partition, x.Stratum))
            .OrderBy(static group => group.Key.Partition, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Stratum, StringComparer.Ordinal)
            .Select(static group =>
            {
                var eligible = group.LongCount();
                var linked = group.LongCount(static x => x.Linked);
                return new SyntheticExactHomonymSlice(
                    group.Key.Partition,
                    group.Key.Stratum,
                    eligible,
                    linked,
                    Rate(linked, eligible));
            })
            .ToArray();

        return new SyntheticExactHomonymReport(
            MethodVersion,
            population.LongLength,
            reserved.LongLength,
            exactDistinct.LongLength,
            falseLinks,
            Rate(falseLinks, exactDistinct.LongLength),
            slices);
    }

    public static void ConferArithmetic(
        SyntheticExactHomonymReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(report.MethodVersion, MethodVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Versão matemática desconhecida.");

        var eligible = report.ByStratum.Sum(static x => x.EligibleDistinctPairs);
        var falseLinks = report.ByStratum.Sum(static x => x.FalseLinks);
        if (eligible != report.ExactDistinctHomonymPairs
            || falseLinks != report.ExactDistinctHomonymFalseLinks)
            throw new InvalidDataException("Totais EXACT/EXACT/EXACT divergem da soma dos estratos.");

        if (report.ExactDistinctHomonymFalseLinkRate != Rate(falseLinks, eligible))
            throw new InvalidDataException("Taxa global de falso vínculo diverge da razão FP/N.");

        foreach (var slice in report.ByStratum)
        {
            if (slice.FalseLinks < 0 || slice.FalseLinks > slice.EligibleDistinctPairs
                || slice.FalseLinkRate != Rate(slice.FalseLinks, slice.EligibleDistinctPairs))
                throw new InvalidDataException(
                    $"Conferência matemática falhou em {slice.Partition}/{slice.Stratum}.");
        }
    }

    private static bool IsExactDistinctHomonym(SyntheticTruthDecisionCase item) =>
        !item.SamePerson && item.NameExact && item.MotherNameExact && item.BirthDateExact;

    private static decimal Rate(long numerator, long denominator) =>
        denominator == 0 ? 0m : decimal.Divide(numerator, denominator);
}

using System.Collections.Generic;
using System.IO;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Shared TRAIN/VALIDATION and portable-snapshot check for the exact unit
/// whose IBGE marginal would contribute positive TF. Does not impute names,
/// consult TEST, fit U or depend on synthetic truth labels.
/// </summary>
public static class V9NominalTfTermCoverage
{
    public static string? UnitToken(string? input, NominalFrequencyUnitV9 unit)
    {
        if (unit == NominalFrequencyUnitV9.LastContentSurname)
            return BrazilianNameComponents.Project(input)?.LastContentSurname;
        if (unit != NominalFrequencyUnitV9.FirstName)
            throw new InvalidDataException("Unidade TF desconhecida.");
        var projection = BrazilianNameComponents.Project(input);
        if (projection is null)
            return null;
        var tokens = projection.NormalizedFull.Split(' ',
            StringSplitOptions.RemoveEmptyEntries);
        var idx = projection.TitlePrefix is null ? 0 : 1;
        if (idx >= tokens.Length)
            return null;
        var first = tokens[idx].Trim('.', ',');
        return first.Length == 0 ? null : first;
    }

    public static bool MissingPublishedFrequency(
        NominalFrequencyUnitV9 unit, string? leftName, string? rightName,
        IReadOnlyDictionary<string, decimal> frequencies)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        if (V9NominalTfUnitSelection.ForLevel(
                CompositeNominalComparisonV9.Classify(leftName, rightName)) != unit)
            return false; // Explicit positive TF is not applicable to this pair.
        var value = UnitToken(leftName, unit);
        if (value is null || value != UnitToken(rightName, unit))
            throw new InvalidDataException("TF positiva sem unidade nominal coincidente.");
        return !frequencies.TryGetValue(value, out var f) || f <= 0m || f > 1m;
    }
}

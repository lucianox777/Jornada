using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Population frequency denominator: ONE row per synthetic base identity,
/// restricted to TRAIN. Never counts repeatedly appearing candidate endpoints.
/// Does not estimate an admissible conditional U baseline or TF weight.
/// </summary>
public static class V9SyntheticBaseFrequencyAudit
{
    public const string Version = "V9_SYNTHETIC_BASE_TRAIN_FREQUENCY_AUDIT_V1";
    public const string Status = "TRAIN_BASE_DISTRIBUTION_ONLY_NO_CONDITIONAL_U_OR_CALIBRATED_TF";

    public sealed record BasePerson(
        string BasePersonId, string Partition,
        string? Name, string? MotherName);

    public sealed record UnitAudit(
        string Side, NominalFrequencyUnitV9 Unit,
        int TrainingBasePeople,
        int PresentTrainingBasePeople, int MissingTrainingBasePeople,
        int DistinctValues, int SingletonValues,
        int ValuesObservedAtLeastFiveBases,
        int BasesCoveredByValuesObservedAtLeastFive,
        int LargestValueCount,
        decimal? LargestValueShare);

    public sealed record Report(
        string Version, string Status, string Partition,
        int CorpusBasePeople, int TrainingBasePeople,
        string PopulationFrameFingerprint,
        IReadOnlyList<UnitAudit> Units);

    /// <summary>
    /// Counts on the original synthetic BASE PERSON population, not observed
    /// source rows. An external consumer must separately pin/check the
    /// generation-manifest SHA; no names or per-value hashes are exported.
    /// </summary>
    public static Report Analyze(
        IReadOnlyCollection<BasePerson> people,
        string populationFrameFingerprint)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentException.ThrowIfNullOrWhiteSpace(populationFrameFingerprint);
        if (people.Count == 0 || people.Any(p => p is null ||
            string.IsNullOrWhiteSpace(p.BasePersonId) ||
            p.Partition is not ("TRAIN" or "VALIDATION" or "TEST")))
            throw new InvalidDataException("Frame TF exige identidades-base e partições válidas.");
        if (people.Select(p => p.BasePersonId).Distinct(StringComparer.Ordinal).Count() != people.Count)
            throw new InvalidDataException("Uma identidade-base não pode contribuir duas vezes para TF.");
        var train = people.Where(p => p.Partition == "TRAIN").ToArray();
        if (train.Length == 0)
            throw new InvalidDataException("Sem população sintética TRAIN para TF.");

        var result = new List<UnitAudit>(8);
        foreach (var side in new[] { "PERSON", "MOTHER" })
        foreach (var unit in Enum.GetValues<NominalFrequencyUnitV9>())
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var missing = 0;
            foreach (var person in train)
            {
                var name = side == "PERSON" ? person.Name : person.MotherName;
                var key = Project(name, unit);
                if (key is null)
                {
                    missing++;
                    continue;
                }
                counts.TryGetValue(key, out var before);
                checked { counts[key] = before + 1; }
            }
            var present = train.Length - missing;
            var values = counts.Values.ToArray();
            var max = values.Length == 0 ? 0 : values.Max();
            result.Add(new UnitAudit(
                side, unit, train.Length, present, missing,
                counts.Count, values.Count(x => x == 1),
                values.Count(x => x >= 5),
                values.Where(x => x >= 5).Sum(),
                max,
                present == 0 ? null : decimal.Divide(max, present)));
        }
        return new Report(
            Version, Status, "TRAIN", people.Count, train.Length,
            populationFrameFingerprint,
            Array.AsReadOnly(result.ToArray()));
    }

    /// <summary>
    /// Identical logical nominal units to CompositeNominalTfPreviewEvaluatorV9.
    /// FullName includes middle names and agnomes; first+last is a JOINT
    /// observed key, never the product of two marginals.
    /// </summary>
    private static string? Project(string? input, NominalFrequencyUnitV9 unit)
    {
        var normalized = BrazilianNameComponents.Project(input);
        if (normalized is null)
            return null;
        var tokens = normalized.NormalizedFull.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        var firstIndex = normalized.TitlePrefix is null ? 0 : 1;
        var first = firstIndex >= tokens.Length
            ? null : tokens[firstIndex].Trim('.', ',');
        if (string.IsNullOrWhiteSpace(first))
            first = null;
        return unit switch
        {
            NominalFrequencyUnitV9.FullName => normalized.NormalizedFull,
            NominalFrequencyUnitV9.FirstLastJoint =>
                first is not null && normalized.LastContentSurname is not null
                    ? string.Concat(first, "|", normalized.LastContentSurname)
                    : null,
            NominalFrequencyUnitV9.FirstName => first,
            NominalFrequencyUnitV9.LastContentSurname =>
                normalized.LastContentSurname,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit,
                "Unidade TF V9 desconhecida.")
        };
    }
}

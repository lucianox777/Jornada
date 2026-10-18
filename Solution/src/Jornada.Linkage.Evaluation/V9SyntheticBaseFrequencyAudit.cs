using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// A strictly TRAIN-only, one-row-per-base-person frequency materialization
    /// for the pinned 30k synthetic corpus. Observed-term frequencies are
    /// conditional on that unit being present, not a SQL-candidate U estimate.
    /// Value-level data is intended for an in-memory diagnostic only.
    /// </summary>
    public sealed record UnitFrequencyValue(
        string NormalizedValue, int BaseIdentities, decimal FrequencyAmongPresent);

    public sealed record MaterializedTrainUnitSnapshot(
        string Version,
        string Status,
        string PopulationFrameFingerprint,
        string InputBootstrapCorpusFingerprint,
        string GeneratorVersion,
        ulong GeneratorSeed,
        string GenerationHypothesesFingerprint,
        string Side,
        NominalFrequencyUnitV9 Unit,
        int CorpusBaseIdentities,
        int TrainBaseIdentities,
        int PresentTrainBaseIdentities,
        int MissingTrainBaseIdentities,
        int MinimumBaseOccurrences,
        IReadOnlyList<UnitFrequencyValue> Frequencies,
        string SnapshotFingerprintSha256);

    public const string MaterializedVersion =
        "V9_CANONICAL_30000_BASE_TRAIN_NOMINAL_FREQUENCIES_V1";
    public const string MaterializedStatus =
        "SYNTHETIC_TRAIN_DISTRIBUTION_NOT_SQL_U_NO_CERTIFICATION";

    /// <summary>
    /// Requires the full pinned 30k census, but only reads TRAIN names for
    /// frequency. Other partitions contribute to census-completeness and
    /// identity-leakage checks only. Pinned metadata is a caller assertion:
    /// external source authentication is a distinct required step.
    /// </summary>
    public static MaterializedTrainUnitSnapshot MaterializeCanonicalTrainUnit(
        IReadOnlyCollection<BasePerson> people,
        string populationFrameFingerprint,
        NominalSyntheticIbgeBootstrapV9 bootstrap,
        string side,
        NominalFrequencyUnitV9 unit,
        int minimumBaseOccurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(bootstrap);
        if (people.Count != CompositeNominalTermFrequencyContractV9.CanonicalBootstrapPeople
            || bootstrap.People != CompositeNominalTermFrequencyContractV9.CanonicalBootstrapPeople
            || bootstrap.Provenance != CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance
            || string.IsNullOrWhiteSpace(populationFrameFingerprint)
            || bootstrap.CorpusFingerprint != populationFrameFingerprint
            || string.IsNullOrWhiteSpace(bootstrap.GeneratorVersion)
            || string.IsNullOrWhiteSpace(bootstrap.GenerationHypothesesFingerprint)
            || side is not ("PERSON" or "MOTHER")
            || !Enum.IsDefined(unit)
            || minimumBaseOccurrences < 1)
            throw new InvalidDataException("Massa-base V9 exige 30 mil pessoas, bootstrap fixado, unidade e lado válidos.");

        var rows = people.ToArray();
        if (rows.Any(x => x is null || string.IsNullOrWhiteSpace(x.BasePersonId)
                || x.Partition is not ("TRAIN" or "VALIDATION" or "TEST"))
            || rows.Select(x => x.BasePersonId)
                .Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new InvalidDataException("O censo sintético deve ter um registro por identidade-base e splits íntegros.");

        var train = rows.Where(x => x.Partition == "TRAIN").ToArray();
        if (train.Length == 0
            || !rows.Any(x => x.Partition == "VALIDATION")
            || !rows.Any(x => x.Partition == "TEST"))
            throw new InvalidDataException("O corpus de 30 mil deve manter TRAIN, VALIDATION e TEST.");
        if (minimumBaseOccurrences > train.Length)
            throw new InvalidDataException("Suporte mínimo excede todo o TRAIN sintético.");

        var normalized = train.Select(x => new
        {
            x.BasePersonId,
            UnitValue = Project(side == "PERSON" ? x.Name : x.MotherName, unit)
        }).OrderBy(x => x.BasePersonId, StringComparer.Ordinal).ToArray();
        var present = normalized.Where(x => x.UnitValue is not null).ToArray();
        if (present.Length == 0)
            throw new InvalidDataException("Unidade sem valores observados no TRAIN.");

        var observed = present.GroupBy(x => x.UnitValue!, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new UnitFrequencyValue(
                x.Key, x.Count(), decimal.Divide(x.Count(), present.Length)))
            .ToArray();
        var retained = observed.Where(x => x.BaseIdentities >= minimumBaseOccurrences)
            .ToArray();
        if (retained.Length == 0)
            throw new InvalidDataException("Sem frequência nominal com suporte TRAIN suficiente.");

        // Hash normalized TRAIN observations (including suppressed values),
        // not just the public-looking marginal counts. Thus two different
        // training assignments with identical histograms cannot silently
        // reuse the same evidence fingerprint.
        var identity = JsonSerializer.Serialize(new
        {
            Version = MaterializedVersion,
            Frame = populationFrameFingerprint,
            bootstrap.GeneratorVersion,
            bootstrap.GeneratorSeed,
            bootstrap.GenerationHypothesesFingerprint,
            Side = side,
            Unit = unit.ToString(),
            MinSupport = minimumBaseOccurrences,
            Training = normalized,
            Retained = retained
        });
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return new MaterializedTrainUnitSnapshot(
            MaterializedVersion, MaterializedStatus, populationFrameFingerprint,
            bootstrap.CorpusFingerprint, bootstrap.GeneratorVersion,
            bootstrap.GeneratorSeed, bootstrap.GenerationHypothesesFingerprint,
            side, unit, rows.Length, train.Length, present.Length,
            train.Length - present.Length, minimumBaseOccurrences,
            Array.AsReadOnly(retained), digest);
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

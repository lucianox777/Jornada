using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Preview-only U calibration on the same TRAIN SQL candidate-pair frame used
/// by the FS scorer: p(unit VALUE | U, unit-selected agreement, blocking).
/// This is not the marginal frequency of people in the population and not a
/// valid identity-cluster confidence bound. No TEST or automated promotion.
/// </summary>
public static class V9TrainSqlNominalUCalibration
{
    public const string Version = "V9_TRAIN_SQL_NONMATCH_NOMINAL_U_REF_V2";

    public sealed record Frozen(
        CompositeNominalTfSideV9 Side,
        string TrainFingerprintSha256,
        int UnmatchedUnitCandidatePairs,
        IReadOnlyDictionary<string, decimal> UnitUByValue,
        string FingerprintSha256,
        NominalFrequencyUnitV9 Unit = NominalFrequencyUnitV9.FirstName,
        V9UnseenTfUAllocation? UnseenAllocation = null,
        V9NominalTfUReference? NominalUReference = null)
    {
        public bool HasSufficientSupport => NominalUReference is { UnmatchedCandidatePairs: >= 2, MatchingNominalPairs: >= 2 };
    }

    public static Frozen Fit(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> trainSqlCandidates,
        CompositeNominalTfSideV9 side,
        decimal smoothingAlpha,
        string trainFingerprintSha256,
        NominalFrequencyUnitV9 unit = NominalFrequencyUnitV9.FirstName,
        IReadOnlyDictionary<string, decimal>? publishedFrequencies = null)
    {
        ArgumentNullException.ThrowIfNull(trainSqlCandidates);
        if (!Enum.IsDefined(side) || unit is not (NominalFrequencyUnitV9.FirstName or NominalFrequencyUnitV9.LastContentSurname)
            || smoothingAlpha <= 0m
            || trainFingerprintSha256 is null || trainFingerprintSha256.Length != 64
            || trainFingerprintSha256.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidDataException("Parâmetros U de prenome TRAIN inválidos.");

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        long n = 0;
        long allNonmatches = 0;
        long nominalAgreements = 0;
        foreach (var row in trainSqlCandidates)
        {
            if (row is null || row.Partition != "TRAIN")
                throw new InvalidDataException("Baseline U só pode usar pares SQL TRAIN.");
            if (row.IsTruePair)
                continue;
            allNonmatches = checked(allNonmatches + 1);
            var left = side == CompositeNominalTfSideV9.Person
                ? row.LeftPersonName : row.LeftMotherName;
            var right = side == CompositeNominalTfSideV9.Person
                ? row.RightPersonName : row.RightMotherName;
            // U_ref counts the exact equality event across ALL nonmatch TRAIN
            // candidates (including ExactFull); the 81-cell classifier priority
            // must not omit agreements from the event denominator/numerator.
            var leftToken = UnitToken(left, unit);
            var equalTokens = leftToken is not null && leftToken == UnitToken(right, unit);
            if (equalTokens)
                nominalAgreements = checked(nominalAgreements + 1);
            // Keep the old per-value conditional distribution only as a
            // diagnostic; it is not the term's U reference in scoring.
            if (V9NominalTfUnitSelection.ForLevel(
                    CompositeNominalComparisonV9.Classify(left, right)) != unit)
                continue;
            var name = UnitToken(left, unit);
            if (name is null || name != UnitToken(right, unit))
                throw new InvalidDataException($"Unidade TF do par U não coincide com {side}/{unit}.");
            counts[name] = checked(counts.GetValueOrDefault(name) + 1L);
            n = checked(n + 1);
        }
        // The reference event is independent of the 81-cell TF selector.
        // Zero selected-level agreements can coexist with ample agreement
        // support across other composite levels (e.g. ExactFirstLast).
        if (allNonmatches < 2 || nominalAgreements < 2)
        {
            var insufficientCanonical = $"{Version}|INSUFFICIENT_SUPPORT|{side}|{unit}|{trainFingerprintSha256}|{smoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{n}|{allNonmatches}|{nominalAgreements}";
            var insufficientSha = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(insufficientCanonical))).ToLowerInvariant();
            return new Frozen(side, trainFingerprintSha256, checked((int)n),
                new ReadOnlyDictionary<string, decimal>(
                    new Dictionary<string, decimal>(StringComparer.Ordinal)),
                insufficientSha, unit);
        }
        // This per-value distribution is diagnostic only. It may be empty
        // when the TRAIN event U_ref is supported entirely by composite
        // levels that do not select TF. Unseen IBGE-published terms are valid.
        var denominator = n + smoothingAlpha * (counts.Count + 1);
        var units = counts.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (x.Value + smoothingAlpha) / denominator,
                StringComparer.Ordinal);
        var nominalReference = V9NominalTfUReference.Fit(
            side, unit, allNonmatches, nominalAgreements, smoothingAlpha,
            trainFingerprintSha256);
        // No IBGE-proportional residual U allocation participates in the score.
        // IBGE is checked per term at the Preview/portable-scorer boundary.
        var allocation = (V9UnseenTfUAllocation?)null;
        var canonical = new StringBuilder()
            .Append(Version).Append('|').Append(side).Append('|').Append(unit).Append('|')
            .Append(trainFingerprintSha256).Append('|')
            .Append(smoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|').Append(n).Append('\n');
        foreach (var term in units.OrderBy(x => x.Key, StringComparer.Ordinal))
            canonical.Append(term.Key).Append('=')
                .Append(term.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        canonical.Append("|U_REF|").Append(nominalReference.PolicyVersion)
            .Append('|').Append(nominalReference.UnmatchedCandidatePairs)
            .Append('|').Append(nominalReference.MatchingNominalPairs)
            .Append('|').Append(nominalReference.SmoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|').Append(nominalReference.Probability.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        return new Frozen(side, trainFingerprintSha256, checked((int)n),
            new ReadOnlyDictionary<string, decimal>(units), digest, unit, allocation, nominalReference);
    }

    /// <summary>
    /// Returns null only where the first-name TF is not applicable, NEVER for
    /// an unknown first-name value while TF weight is positive. The pinned
    /// IBGE frequency and conditional SQL U must reference that same term.
    /// </summary>
    public static CompositeNominalTfPreviewV9? Preview(
        Frozen calibration,
        FrozenIbgeNominalTfV9 reference,
        CompositeNominalLevelV9 personLevel,
        CompositeNominalLevelV9 motherLevel,
        V9FsAdaptiveCandidatePair pair,
        decimal weight,
        decimal minimumFrequency,
        long minimumIndependentSupport,
        CompositeNominalTfAdjustmentModeV9 mode = CompositeNominalTfAdjustmentModeV9.Symmetric)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(pair);
        if (weight <= 0m || weight > 1m
            || minimumFrequency < 0m || minimumFrequency > 1m
            || minimumIndependentSupport <= 0 || !Enum.IsDefined(calibration.Side)
            || !Enum.IsDefined(mode))
            throw new InvalidDataException("Preview U positivo exige configuração TF válida.");
        // Applicability is evidence, not caller policy. A stale/mismatched
        // level must not turn an applicable positive TF into a neutral null.
        if (personLevel != CompositeNominalComparisonV9.Classify(
                pair.LeftPersonName, pair.RightPersonName)
            || motherLevel != CompositeNominalComparisonV9.Classify(
                pair.LeftMotherName, pair.RightMotherName))
            throw new InvalidDataException("Estados TF V9 divergem do par nominal observado.");
        var level = calibration.Side == CompositeNominalTfSideV9.Person
            ? personLevel : motherLevel;
        if (V9NominalTfUnitSelection.ForLevel(level) != calibration.Unit)
            return null;
        var marginalLevel = V9NominalTfUnitSelection.MarginalLevel(calibration.Unit);
        var left = calibration.Side == CompositeNominalTfSideV9.Person
            ? pair.LeftPersonName : pair.LeftMotherName;
        var right = calibration.Side == CompositeNominalTfSideV9.Person
            ? pair.RightPersonName : pair.RightMotherName;
        var value = UnitToken(left, calibration.Unit);
        if (value is null || value != UnitToken(right, calibration.Unit))
            throw new InvalidDataException("TF positiva sem unidade nominal coincidente.");
        var unitU = calibration.NominalUReference
            ?? throw new InvalidDataException("TF positiva sem U_ref TRAIN compatível.");
        unitU.Validate(calibration.Side, calibration.Unit,
            calibration.TrainFingerprintSha256);
        var unitReferenceProbability = unitU.ResolveTerm(
            reference.FrequenciesForUnit(calibration.Side, calibration.Unit), value);
        var preview = reference.CreatePreview(
            calibration.Side, marginalLevel, left, right,
            unitReferenceProbability, weight, minimumFrequency, minimumIndependentSupport)
            ?? throw new InvalidDataException("TF positiva não pode gerar preview nulo.");
        return preview with { Mode = mode };
    }

    private static string? UnitToken(string? input, NominalFrequencyUnitV9 unit)
    {
        if (unit == NominalFrequencyUnitV9.LastContentSurname)
            return BrazilianNameComponents.Project(input)?.LastContentSurname;
        return FirstToken(input);
    }

    private static string? FirstToken(string? input)
    {
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
}

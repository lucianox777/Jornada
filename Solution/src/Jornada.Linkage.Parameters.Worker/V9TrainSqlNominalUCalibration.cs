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
    public const string Version = "V9_TRAIN_SQL_CONDITIONAL_FIRST_NAME_U_V1";

    public sealed record Frozen(
        CompositeNominalTfSideV9 Side,
        string TrainFingerprintSha256,
        int UnmatchedUnitCandidatePairs,
        IReadOnlyDictionary<string, decimal> UnitUByValue,
        string FingerprintSha256,
        NominalFrequencyUnitV9 Unit = NominalFrequencyUnitV9.FirstName)
    {
        public bool HasSufficientSupport => UnmatchedUnitCandidatePairs >= 2 && UnitUByValue.Count > 0;
    }

    public static Frozen Fit(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> trainSqlCandidates,
        CompositeNominalTfSideV9 side,
        decimal smoothingAlpha,
        string trainFingerprintSha256,
        NominalFrequencyUnitV9 unit = NominalFrequencyUnitV9.FirstName)
    {
        ArgumentNullException.ThrowIfNull(trainSqlCandidates);
        if (!Enum.IsDefined(side) || unit is not (NominalFrequencyUnitV9.FirstName or NominalFrequencyUnitV9.LastContentSurname)
            || smoothingAlpha <= 0m
            || trainFingerprintSha256 is null || trainFingerprintSha256.Length != 64
            || trainFingerprintSha256.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidDataException("Parâmetros U de prenome TRAIN inválidos.");

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        long n = 0;
        foreach (var row in trainSqlCandidates)
        {
            if (row is null || row.Partition != "TRAIN")
                throw new InvalidDataException("Baseline U só pode usar pares SQL TRAIN.");
            if (row.IsTruePair)
                continue;
            var left = side == CompositeNominalTfSideV9.Person
                ? row.LeftPersonName : row.LeftMotherName;
            var right = side == CompositeNominalTfSideV9.Person
                ? row.RightPersonName : row.RightMotherName;
            // TRAIN support must count the exact same nominal events that
            // VALIDATION/runtime select (ExactFull also selects FirstName).
            if (V9NominalTfUnitSelection.ForLevel(
                    CompositeNominalComparisonV9.Classify(left, right)) != unit)
                continue;
            var name = UnitToken(left, unit);
            if (name is null || name != UnitToken(right, unit))
                throw new InvalidDataException($"Unidade TF do par U não coincide com {side}/{unit}.");
            counts[name] = checked(counts.GetValueOrDefault(name) + 1L);
            n = checked(n + 1);
        }
        if (n < 2 || counts.Count == 0)
        {
            var insufficientCanonical = $"{Version}|INSUFFICIENT_SUPPORT|{side}|{unit}|{trainFingerprintSha256}|{smoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{n}";
            var insufficientSha = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(insufficientCanonical))).ToLowerInvariant();
            return new Frozen(side, trainFingerprintSha256, checked((int)n),
                new ReadOnlyDictionary<string, decimal>(
                    new Dictionary<string, decimal>(StringComparer.Ordinal)),
                insufficientSha, unit);
        }
        // The additional unseen bucket is reserved for diagnostic support
        // accounting, but unseen terms are always rejected by Preview.
        var denominator = n + smoothingAlpha * (counts.Count + 1);
        var units = counts.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (x.Value + smoothingAlpha) / denominator,
                StringComparer.Ordinal);
        var canonical = new StringBuilder()
            .Append(Version).Append('|').Append(side).Append('|').Append(unit).Append('|')
            .Append(trainFingerprintSha256).Append('|')
            .Append(smoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|').Append(n).Append('\n');
        foreach (var term in units.OrderBy(x => x.Key, StringComparer.Ordinal))
            canonical.Append(term.Key).Append('=')
                .Append(term.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        return new Frozen(side, trainFingerprintSha256, checked((int)n),
            new ReadOnlyDictionary<string, decimal>(units), digest, unit);
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
        if (value is null || value != UnitToken(right, calibration.Unit)
            || !calibration.UnitUByValue.TryGetValue(value, out var unitU))
            throw new InvalidDataException(
                "TF positiva desconhecida/sem suporte U TRAIN: não converter em peso zero ou BF=1.");
        var preview = reference.CreatePreview(
            calibration.Side, marginalLevel, left, right,
            unitU, weight, minimumFrequency, minimumIndependentSupport)
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

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Preview-only U calibration on the same TRAIN SQL candidate-pair frame used
/// by the FS scorer: p(first-name VALUE | U, exact-first agreement, blocking).
/// This is not the marginal frequency of people in the population and not a
/// valid identity-cluster confidence bound. No TEST or automated promotion.
/// </summary>
public static class V9TrainSqlFirstNameUCalibration
{
    public const string Version = "V9_TRAIN_SQL_CONDITIONAL_FIRST_NAME_U_V1";

    public sealed record Frozen(
        CompositeNominalTfSideV9 Side,
        string TrainFingerprintSha256,
        int UnmatchedExactFirstCandidatePairs,
        IReadOnlyDictionary<string, decimal> FirstNameUnitUByValue,
        string FingerprintSha256);

    public static Frozen Fit(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> trainSqlCandidates,
        CompositeNominalTfSideV9 side,
        decimal smoothingAlpha,
        string trainFingerprintSha256)
    {
        ArgumentNullException.ThrowIfNull(trainSqlCandidates);
        if (!Enum.IsDefined(side) || smoothingAlpha <= 0m
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
            if (CompositeNominalComparisonV9.Classify(left, right)
                != CompositeNominalLevelV9.ExactFirst)
                continue;
            var name = FirstToken(left);
            if (name is null || name != FirstToken(right))
                throw new InvalidDataException("Unidade TF do par U não coincide com ExactFirst.");
            counts[name] = checked(counts.GetValueOrDefault(name) + 1L);
            n = checked(n + 1);
        }
        if (n < 2 || counts.Count == 0)
            throw new InvalidDataException(
                "TF positiva exige suporte TRAIN U na unidade ExactFirst do mesmo SQL.");
        // The additional unseen bucket is reserved for diagnostic support
        // accounting, but unseen terms are always rejected by Preview.
        var denominator = n + smoothingAlpha * (counts.Count + 1);
        var units = counts.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (x.Value + smoothingAlpha) / denominator,
                StringComparer.Ordinal);
        var canonical = new StringBuilder()
            .Append(Version).Append('|').Append(side).Append('|')
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
            new ReadOnlyDictionary<string, decimal>(units), digest);
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
        long minimumIndependentSupport)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(pair);
        if (weight <= 0m || weight > 1m)
            throw new InvalidDataException("Preview U positivo exige TF em (0,1].");
        var level = calibration.Side == CompositeNominalTfSideV9.Person
            ? personLevel : motherLevel;
        if (level != CompositeNominalLevelV9.ExactFirst)
            return null;
        var left = calibration.Side == CompositeNominalTfSideV9.Person
            ? pair.LeftPersonName : pair.LeftMotherName;
        var right = calibration.Side == CompositeNominalTfSideV9.Person
            ? pair.RightPersonName : pair.RightMotherName;
        var value = FirstToken(left);
        if (value is null || value != FirstToken(right)
            || !calibration.FirstNameUnitUByValue.TryGetValue(value, out var unitU))
            throw new InvalidDataException(
                "TF positiva desconhecida/sem suporte U TRAIN: não converter em peso zero ou BF=1.");
        return reference.CreatePreview(
            calibration.Side, level, left, right,
            unitU, weight, minimumFrequency, minimumIndependentSupport);
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

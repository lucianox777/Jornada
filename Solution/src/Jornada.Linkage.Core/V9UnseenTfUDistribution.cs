using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// One aggregate U mass is reserved for TRAIN-unseen values and distributed
/// over the *pinned* IBGE-supported unseen names, proportional to their
/// marginal frequencies. The IBGE marginal is a declared modeling proxy,
/// not a measured SQL nonmatch distribution or an FDR certificate.
/// </summary>
public sealed record V9UnseenTfUAllocation(
    string PolicyVersion, decimal ReservedUProbability, decimal UnseenReferenceMass);

public static class V9UnseenTfUDistribution
{
    public const string Version = "V9_TRAIN_U_UNSEEN_IBGE_CONDITIONAL_V1";
    private const decimal Tolerance = 0.000000000001m;

    public static V9UnseenTfUAllocation Fit(
        IReadOnlyDictionary<string, decimal> trainU,
        IReadOnlyDictionary<string, decimal> publishedFrequencies,
        long unmatchedCount, decimal smoothingAlpha)
    {
        ArgumentNullException.ThrowIfNull(trainU);
        ArgumentNullException.ThrowIfNull(publishedFrequencies);
        if (unmatchedCount < 2 || smoothingAlpha <= 0m || trainU.Count == 0
            || publishedFrequencies.Count == 0)
            throw new InvalidDataException("Distribuição U residual exige TRAIN e IBGE com suporte.");
        var denominator = unmatchedCount + smoothingAlpha * (trainU.Count + 1);
        var reserved = smoothingAlpha / denominator;
        var unseenReferenceMass = publishedFrequencies
            .Where(x => !trainU.ContainsKey(x.Key)).Sum(x => x.Value);
        var policy = new V9UnseenTfUAllocation(Version, reserved, unseenReferenceMass);
        Validate(policy, trainU, publishedFrequencies);
        return policy;
    }

    public static void Validate(V9UnseenTfUAllocation policy,
        IReadOnlyDictionary<string, decimal> trainU,
        IReadOnlyDictionary<string, decimal> publishedFrequencies)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(trainU);
        ArgumentNullException.ThrowIfNull(publishedFrequencies);
        if (policy.PolicyVersion != Version
            || policy.ReservedUProbability <= 0m || policy.ReservedUProbability >= 1m
            || policy.UnseenReferenceMass < 0m
            || trainU.Count == 0 || publishedFrequencies.Count == 0
            || trainU.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value <= 0m || x.Value > 1m)
            || publishedFrequencies.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value <= 0m || x.Value > 1m)
            || Math.Abs(trainU.Values.Sum() + policy.ReservedUProbability - 1m) > Tolerance)
            throw new InvalidDataException("Massa U TRAIN residual inválida ou não normalizada.");
        var expected = publishedFrequencies
            .Where(x => !trainU.ContainsKey(x.Key)).Sum(x => x.Value);
        if (Math.Abs(expected - policy.UnseenReferenceMass)
            > Tolerance * Math.Max(1m, expected))
            throw new InvalidDataException("Massa IBGE não vista diverge do snapshot congelado.");
    }

    public static decimal Resolve(
        IReadOnlyDictionary<string, decimal> trainU,
        IReadOnlyDictionary<string, decimal> publishedFrequencies,
        V9UnseenTfUAllocation? policy, string term)
    {
        if (!publishedFrequencies.TryGetValue(term, out var published) || published <= 0m)
            throw new InvalidDataException("TF positiva sem frequência IBGE publicada para o valor.");
        if (trainU.TryGetValue(term, out var observed))
            return observed;
        if (policy is null || policy.PolicyVersion != Version
            || policy.ReservedUProbability <= 0m || policy.ReservedUProbability >= 1m
            || policy.UnseenReferenceMass <= 0m)
            throw new InvalidDataException("TF positiva sem política U residual TRAIN congelada.");
        var allocated = policy.ReservedUProbability * published / policy.UnseenReferenceMass;
        if (allocated <= 0m || allocated > 1m)
            throw new InvalidDataException("U residual nominal não representável.");
        return allocated;
    }
}

// Decision 19. One U reference per nominal side/unit: the unconditional
// TRAIN nonmatch event P(unit tokens coincide | candidate is a nonmatch).
// Its event is counted directly on the frozen SQL candidate-pair frame, not
// by summing a guessed subset of the mutually exclusive 81 nominal cells.
// The former per-value residual IBGE allocator above is diagnostic/legacy;
// neither the calibrator nor the portable scorer may use it for positive TF.
public sealed record V9NominalTfUReference(
    string PolicyVersion, CompositeNominalTfSideV9 Side,
    NominalFrequencyUnitV9 Unit, long UnmatchedCandidatePairs,
    long MatchingNominalPairs, decimal SmoothingAlpha,
    decimal Probability, string TrainCorpusFingerprintSha256)
{
    public const string Version = "V9_TRAIN_NONMATCH_NOMINAL_AGREEMENT_U_REF_V1";
    public static V9NominalTfUReference Fit(
        CompositeNominalTfSideV9 side, NominalFrequencyUnitV9 unit,
        long unmatchedCandidatePairs, long matchingNominalPairs,
        decimal smoothingAlpha, string trainCorpusFingerprintSha256)
    {
        if (unmatchedCandidatePairs < 2 || matchingNominalPairs < 2
            || matchingNominalPairs > unmatchedCandidatePairs || smoothingAlpha <= 0m
            || !Enum.IsDefined(side)
            || unit is not (NominalFrequencyUnitV9.FirstName or NominalFrequencyUnitV9.LastContentSurname)
            || trainCorpusFingerprintSha256 is not { Length: 64 }
            || trainCorpusFingerprintSha256.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidDataException("U_ref exige evento nominal e suporte SQL TRAIN válidos.");
        // Beta(alpha,alpha) smoothing of the binary agreement event; never
        // allocate IBGE-proportional residual U per name.
        var probability = (matchingNominalPairs + smoothingAlpha)
            / (unmatchedCandidatePairs + 2m * smoothingAlpha);
        var reference = new V9NominalTfUReference(Version, side, unit,
            unmatchedCandidatePairs, matchingNominalPairs, smoothingAlpha,
            probability, trainCorpusFingerprintSha256);
        reference.Validate(side, unit, trainCorpusFingerprintSha256);
        return reference;
    }

    public void Validate(CompositeNominalTfSideV9 side, NominalFrequencyUnitV9 unit,
        string trainCorpusFingerprintSha256)
    {
        if (PolicyVersion != Version || Side != side || Unit != unit
            || !string.Equals(TrainCorpusFingerprintSha256, trainCorpusFingerprintSha256,
                StringComparison.OrdinalIgnoreCase)
            || UnmatchedCandidatePairs < 2 || MatchingNominalPairs < 2
            || MatchingNominalPairs > UnmatchedCandidatePairs || SmoothingAlpha <= 0m
            || Probability <= 0m || Probability >= 1m
            || Math.Abs(Probability - (MatchingNominalPairs + SmoothingAlpha)
                / (UnmatchedCandidatePairs + 2m * SmoothingAlpha)) > .000000000001m)
            throw new InvalidDataException("U_ref TRAIN adulterado, incompatível ou sem suporte.");
    }

    public decimal ResolveTerm(IReadOnlyDictionary<string, decimal> publishedFrequencies,
        string normalizedTerm)
    {
        Validate(Side, Unit, TrainCorpusFingerprintSha256);
        if (string.IsNullOrWhiteSpace(normalizedTerm) || publishedFrequencies is null
            || !publishedFrequencies.TryGetValue(normalizedTerm, out var frequency)
            || frequency <= 0m || frequency > 1m)
            throw new InvalidDataException("TF positiva sem frequência IBGE publicada.");
        return Probability;
    }
}

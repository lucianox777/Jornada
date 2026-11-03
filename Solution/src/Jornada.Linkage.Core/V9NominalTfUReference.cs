using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

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

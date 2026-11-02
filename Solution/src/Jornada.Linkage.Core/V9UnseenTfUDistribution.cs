using System.Collections.Generic;
using System.IO;
using System.Linq;

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

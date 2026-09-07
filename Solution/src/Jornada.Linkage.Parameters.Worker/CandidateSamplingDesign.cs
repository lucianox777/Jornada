using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Attested, frozen source frame. Completeness is an external governance assertion.</summary>
public sealed record CandidateSamplingFrame(string Reference, bool Complete,
    IReadOnlyList<CandidateUniverseSource> Sources);

public sealed record CandidateSamplingOptions(
    bool UseComponents, int YearTolerance, int SourceSampleSize,
    IReadOnlyList<int> PrimaryPassQuotas, int MaxSources, int MaxCandidatesPerSource,
    long MaxEnumeratedPairs, int MaxSelectedPairs, int CommandTimeoutSeconds)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(PrimaryPassQuotas);
        if (YearTolerance is < 0 or > 2 || SourceSampleSize < 1 || MaxSources is < 1 or > 100_000 ||
            SourceSampleSize > MaxSources || MaxCandidatesPerSource is < 1 or > 1_000_000 ||
            MaxEnumeratedPairs is < 1 or > 5_000_000 || MaxSelectedPairs is < 1 or > 1_000_000 ||
            CommandTimeoutSeconds is < 1 or > 3600 || PrimaryPassQuotas.Count != 5 ||
            PrimaryPassQuotas.Any(q => q < 0 || q > 1_000_000) ||
            PrimaryPassQuotas.Take(UseComponents ? 5 : 1).Any(q => q == 0) ||
            (!UseComponents && PrimaryPassQuotas.Skip(1).Any(q => q != 0)))
            throw new ArgumentOutOfRangeException(nameof(PrimaryPassQuotas), "Desenho amostral ou limites inválidos.");
    }
}

/// <summary>A selected pair is not a match or non-match label.</summary>
public sealed record CandidateSamplingPair(Guid SourceId, Guid CandidateId,
    BirthBlockingPass Membership, BirthBlockingPass PrimaryPass,
    long StratumPopulation, int StratumSampleSize, decimal InclusionProbability, decimal DesignWeight);

public sealed record CandidateSamplingPassCount(BirthBlockingPass Pass, long Members, long Primary,
    long Selected, decimal EstimatedPopulation, decimal EstimatedMembership);

/// <summary>Restricted result: IDs are pseudonymous personal data and require governed handling.</summary>
public sealed record CandidateSamplingCapture(string DesignVersion, string BlockingVersion,
    string FrameReference, string FrameFingerprint, string SeedCommitment, string ConfigurationFingerprint,
    string SnapshotToken, DateTimeOffset CapturedAt, string UniverseFingerprint, string SelectionFingerprint,
    int FrameSize, int SelectedSources, int EmptySources, long EnumeratedPairs, long OverlapCount,
    decimal EstimatedPairPopulation, IReadOnlyList<CandidateSamplingPassCount> PassCounts,
    IReadOnlyList<CandidateSamplingPair> Pairs);

/// <summary>
/// Exact finite-population SRS probabilities under a uniform random-rank design.
/// HMAC-SHA256 implements reproducible pseudorandom ranks; it is not a label or identity score.
/// </summary>
public static class CandidateSamplingDesign
{
    public const string Version = "CANDIDATE_SRS_TWO_STAGE_V1";
    private const string Domain = "JORNADA_LINKAGE_SAMPLING_V1";

    public static IReadOnlyList<T> Draw<T>(IReadOnlyList<T> population, int count, byte[] seed,
        string domain, Func<T, string> identity)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(identity);
        if (seed.Length != 32 || count < 1 || count > population.Count || string.IsNullOrWhiteSpace(domain))
            throw new ArgumentOutOfRangeException(nameof(count), "Amostra, domínio ou semente inválidos.");
        var ranked = population.Select(item => (Item: item, Id: identity(item))).ToArray();
        if (ranked.Any(x => string.IsNullOrWhiteSpace(x.Id)) ||
            ranked.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ranked.Length)
            throw new ArgumentException("Identificadores amostrais devem ser únicos e não vazios.", nameof(population));
        return ranked.Select(x => (x.Item, x.Id, Rank: Rank(seed, domain, x.Id)))
            .OrderBy(x => x.Rank, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(count).Select(x => x.Item).ToArray();
    }

    public static (decimal Probability, decimal Weight) Inclusion(int frameSize, int sourceSampleSize,
        long stratumPopulation, int stratumSampleSize)
    {
        if (frameSize < 1 || sourceSampleSize < 1 || sourceSampleSize > frameSize ||
            stratumPopulation < 1 || stratumSampleSize < 1 || stratumSampleSize > stratumPopulation)
            throw new ArgumentOutOfRangeException(nameof(stratumSampleSize));
        var probability = ((decimal)sourceSampleSize / frameSize) * ((decimal)stratumSampleSize / stratumPopulation);
        var weight = ((decimal)frameSize / sourceSampleSize) * ((decimal)stratumPopulation / stratumSampleSize);
        return (probability, weight);
    }

    public static string Fingerprint(byte[] seed, string domain, string value)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(value);
        if (seed.Length != 32 || string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("Chave ou domínio inválido.");
        return Rank(seed, domain, value);
    }

    private static string Rank(byte[] seed, string domain, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(seed,
            Encoding.UTF8.GetBytes(Domain + "|" + domain + "|" + value))).ToLowerInvariant();

    public static string CanonicalNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
}

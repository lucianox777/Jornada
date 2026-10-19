using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9TrainBlockingDiscoveryOptions(
    int MaximumPrefixLength = 4,
    int NonMatchSampleSize = 100_000,
    int NonMatchSampleSeed = 20261019,
    int PositivePairCap = 2_000_000,
    long PairSamplingAttemptCap = 10_000_000,
    BlockingRuleSetSearchOptions? SearchOptions = null);

public sealed record V9TrainBlockingDiscoveryResult(
    string Version,
    string ProjectionSchemaVersion,
    string ProjectionSchemaFingerprintSha256,
    string TrainProjectionFingerprintSha256,
    string SamplePairFingerprintSha256,
    V9TrainBlockingDiscoveryOptions Options,
    BlockingRuleSetSearchOptions EffectiveSearchOptions,
    int TrainObservationCount,
    int TrainBaseIdentityCount,
    long EligiblePositivePairsBeforeBlocking,
    long EligibleNonMatchPairsBeforeBlocking,
    int SampledNonMatchPairs,
    long PairSamplingAttempts,
    int FeatureCount,
    long PairFeatureComparisonCount,
    int EvaluatedPrimitiveCount,
    int RetainedPrimitiveCount,
    long EnumeratedUnionCount,
    bool ExhaustivePrimitivePool,
    IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> Candidates);

/// <summary>
/// TRAIN-only proposal generation. All cross-gestor positives are included BEFORE
/// blocking; nonmatches are sampled before blocking with an explicit reproducible
/// budget. Sampled retention is only a search heuristic, never an m/u estimate,
/// SQL cost measurement or evidence for VALIDATION/TEST. All retained unions are
/// returned for physical SQL fitting and global post-Resolve selection.
/// </summary>
public static class V9TrainBlockingCandidateDiscovery
{
    public const string Version = "V9_TRAIN_PREBLOCKING_CANDIDATE_DISCOVERY_V1";

    public static V9TrainBlockingDiscoveryResult Discover(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> trainObservations,
        V9TrainBlockingDiscoveryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(trainObservations);
        options ??= new V9TrainBlockingDiscoveryOptions();
        V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(options.MaximumPrefixLength);
        if (options.NonMatchSampleSize is < 1 or > 2_000_000
            || options.PositivePairCap is < 1 or > 10_000_000
            || options.PairSamplingAttemptCap is < 1 or > 1_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(options));
        var search = options.SearchOptions ?? new BlockingRuleSetSearchOptions(
            MaxFieldsPerPass: 2, MaxPasses: 2, PrimitivePoolSize: 16,
            CandidateRuleSetCap: 100_000);
        search.Validate();
        if (trainObservations.Count < 2 || trainObservations.Any(x => x is null
                || x.ObservationId <= 0 || x.Partition != "TRAIN"
                || string.IsNullOrWhiteSpace(x.BasePersonId) || string.IsNullOrWhiteSpace(x.Gestor)))
            throw new InvalidDataException("Discovery accepts valid TRAIN observations only; VALIDATION/TEST are prohibited.");
        var rows = trainObservations.OrderBy(x => x.ObservationId).ToArray();
        if (rows.Select(x => x.ObservationId).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Duplicate discovery ObservationId.");
        var groups = rows.Select((row, index) => (row, index))
            .GroupBy(x => x.row.BasePersonId, StringComparer.Ordinal).ToArray();
        static long CrossGestorCount(IEnumerable<V9PhysicalCalibrationObservation> values)
        {
            var counts = values.GroupBy(x => x.Gestor, StringComparer.Ordinal)
                .Select(g => (long)g.Count()).ToArray();
            var n = counts.Sum();
            return (n * n - counts.Sum(x => x * x)) / 2;
        }
        var positiveCount = groups.Sum(g => CrossGestorCount(g.Select(x => x.row)));
        if (positiveCount == 0 || positiveCount > options.PositivePairCap)
            throw new InvalidOperationException(
                $"Complete preblocking positives={positiveCount}; configured cap={options.PositivePairCap}. No truncation is permitted.");
        var nonMatchUniverse = CrossGestorCount(rows) - positiveCount;
        if (nonMatchUniverse == 0)
            throw new InvalidOperationException("No cross-gestor nonmatch support for proposal generation.");
        var target = (int)Math.Min(options.NonMatchSampleSize, nonMatchUniverse);
        var sample = new HashSet<(int Left, int Right)>();
        long attempts = 0;
        bool EligibleNonMatch(int i, int j) => rows[i].Gestor != rows[j].Gestor
            && rows[i].BasePersonId != rows[j].BasePersonId;

        // Small/dense samples are exhaustively enumerated, then reservoir-sampled.
        // Large frames use bounded uniform rejection sampling of distinct pairs.
        if (nonMatchUniverse <= 2L * target
            && (long)rows.Length * (rows.Length - 1) / 2 <= options.PairSamplingAttemptCap)
        {
            var reservoir = new List<(int Left, int Right)>(target);
            var random = new Random(options.NonMatchSampleSeed);
            long eligibleSeen = 0;
            for (var i = 0; i < rows.Length; i++)
            for (var j = i + 1; j < rows.Length; j++)
            {
                attempts++;
                if (!EligibleNonMatch(i, j)) continue;
                eligibleSeen++;
                if (reservoir.Count < target) reservoir.Add((i, j));
                else
                {
                    var slot = random.NextInt64(eligibleSeen);
                    if (slot < target) reservoir[(int)slot] = (i, j);
                }
            }
            sample.UnionWith(reservoir);
        }
        else
        {
            var random = new Random(options.NonMatchSampleSeed);
            while (sample.Count < target && attempts < options.PairSamplingAttemptCap)
            {
                attempts++;
                var i = random.Next(rows.Length);
                var j = random.Next(rows.Length);
                if (i > j) (i, j) = (j, i);
                if (i != j && EligibleNonMatch(i, j)) sample.Add((i, j));
            }
        }
        if (sample.Count != target)
            throw new InvalidOperationException(
                $"Incomplete nonmatch sample: requested={target}, sampled={sample.Count}, attempts={attempts}; raise the explicit sampling budget.");

        var features = V9CalibrationBlockingProjection.SupportedFeatures(options.MaximumPrefixLength);
        var projected = rows.Select(row => V9CalibrationBlockingProjection.Project(
                row.PersonName, row.MotherName, row.BirthDate, options.MaximumPrefixLength)
            .GroupBy(x => x.Feature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal))
            .ToArray();
        using var projectionFingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < rows.Length; i++)
            projectionFingerprint.AppendData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
            {
                rows[i].ObservationId, rows[i].BasePersonId, rows[i].Gestor,
                Keys = projected[i].OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new { Feature = x.Key, Values = x.Value.OrderBy(v => v, StringComparer.Ordinal).ToArray() }).ToArray()
            }) + "\n"));
        var observations = new List<BlockingFeatureObservation>(checked((int)positiveCount + target));
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var group in groups)
        {
            var members = group.Select(x => x.index).ToArray();
            for (var a = 0; a < members.Length; a++)
            for (var b = a + 1; b < members.Length; b++)
                if (rows[members[a]].Gestor != rows[members[b]].Gestor)
                    AddPair(members[a], members[b], true);
        }
        foreach (var pair in sample.OrderBy(x => x.Left).ThenBy(x => x.Right))
            AddPair(pair.Left, pair.Right, false);
        var enumeration = BlockingRuleSetSearch.EnumerateCandidateRuleSets(observations, features, search);
        return new V9TrainBlockingDiscoveryResult(Version,
            V9CalibrationBlockingProjection.SchemaVersion,
            V9CalibrationBlockingProjection.ComputeSchemaFingerprint(options.MaximumPrefixLength),
            Convert.ToHexString(projectionFingerprint.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(fingerprint.GetHashAndReset()).ToLowerInvariant(),
            options, search, rows.Length, groups.Length, positiveCount, nonMatchUniverse,
            sample.Count, attempts, features.Count, checked((positiveCount + sample.Count) * features.Count),
            enumeration.EvaluatedPrimitiveCount,
            enumeration.RetainedPrimitiveCount, enumeration.EnumeratedUnionCount,
            enumeration.ExhaustivePrimitivePool, enumeration.Candidates);

        void AddPair(int left, int right, bool match)
        {
            var agreements = new Dictionary<string, bool?>(features.Count, StringComparer.Ordinal);
            foreach (var feature in features)
                agreements[feature] = projected[left].TryGetValue(feature, out var l)
                    && projected[right].TryGetValue(feature, out var r) ? l.Overlaps(r) : null;
            observations.Add(new BlockingFeatureObservation(match, agreements));
            fingerprint.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
                $"{rows[left].ObservationId}|{rows[right].ObservationId}|{match}\n")));
        }
    }
}

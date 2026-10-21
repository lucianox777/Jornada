namespace Jornada.Linkage.Runner;

/// <summary>
/// Pre-registered TRAIN_SELECTION order-sensitivity protocol for isolated,
/// independently replayed incremental Gold experiments. No UUID publication,
/// SQL access, scorer changes or TEST exposure occurs in this class.
/// </summary>
public static class GoldTrainThreeOrderProtocol
{
    public const string Version = "GOLD_TRAIN_THREE_ORDERS_FROZEN_FS_V1";
    public const string Baseline = "BASELINE_G0_G1_G2_G3";
    public const string Reverse = "REVERSE_G3_G2_G1_G0";
    public const string Interleaved = "INTERLEAVED_G1_G3_G0_G2";
    public const string StableSourceKeyContract = "TRAIN_CSV_OBSERVATION_ID_NOT_SQL_ID_V1";

    public static IReadOnlyList<string> OrderIds { get; } =
        Array.AsReadOnly(new[] { Baseline, Reverse, Interleaved });

    private static readonly IReadOnlyDictionary<string, string[]> Orders =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Baseline] = ["G0", "G1", "G2", "G3"],
            [Reverse] = ["G3", "G2", "G1", "G0"],
            [Interleaved] = ["G1", "G3", "G0", "G2"]
        };

    /// <summary>
    /// Never choose an order adaptively from a previous trial's Gold metrics.
    /// All four manager waves must be represented by a nonempty source set.
    /// </summary>
    public static IReadOnlyList<string> ResolveOrder(string orderId, IEnumerable<string> managerCodes)
    {
        if (string.IsNullOrWhiteSpace(orderId) || !Orders.TryGetValue(orderId, out var plan))
            throw new InvalidDataException("Ordem Gold não pré-declarada.");
        ArgumentNullException.ThrowIfNull(managerCodes);
        var received = managerCodes.ToArray();
        if (received.Length != 4 || received.Any(string.IsNullOrWhiteSpace)
            || received.Distinct(StringComparer.Ordinal).Count() != 4
            || !received.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                Orders[Baseline], StringComparer.Ordinal))
            throw new InvalidDataException("Gabarito de ondas exige G0, G1, G2 e G3 exatamente uma vez.");
        return Array.AsReadOnly(plan.ToArray());
    }

    /// <summary>
    /// Must be constructed from three SEPARATE disposable SQL runs. Identity
    /// assignment is compared by truth-labeled partition metrics, never by
    /// UUID byte equality across runs.
    /// </summary>
    public sealed record Trial(
        string OrderId,
        string OriginalCorpusSha256,
        string FrozenSnapshotSha256,
        string IsolatedRunFingerprintSha256,
        string EvidenceScope,
        string SourceKeyContract,
        bool TestUsed,
        bool Activated,
        bool AllFourWavesPublished,
        IReadOnlyList<string> ExecutedWaveOrder,
        IReadOnlyDictionary<long, string> TruthBySource,
        IReadOnlyDictionary<long, Guid> FinalPublishedGoldBySource);

    public sealed record Metrics(
        string OrderId,
        long Sources,
        long PublishedSources,
        long PendingSources,
        double PendingRate,
        long TrueIdentitiesRepresented,
        long FragmentedTrueIdentities,
        long ImpureGoldReferences,
        long TruePairs,
        long CorrectGoldPairs,
        long FalseGoldPairs,
        double? PairRecall,
        double? PairPrecision,
        double BcubedStandardPrecision,
        double BcubedStandardRecall,
        double BcubedCoverageAwarePrecision,
        double BcubedCoverageAwareRecall);

    public sealed record Comparison(
        string Version,
        string OriginalCorpusSha256,
        string FrozenSnapshotSha256,
        IReadOnlyList<Metrics> Orders,
        long LargestPendingDifference,
        long LargestFalseGoldPairDifference,
        bool GoldPartitionOrderInvariant,
        bool IncrementalLinkageFdrComputed,
        bool IndependentlyCertified,
        bool TestUsed,
        bool OperationallyActivated);

    public static Comparison Compare(IReadOnlyList<Trial> trials)
    {
        ArgumentNullException.ThrowIfNull(trials);
        if (trials.Count != OrderIds.Count || trials.Any(x => x is null)
            || trials.Select(x => x.OrderId).Distinct(StringComparer.Ordinal).Count() != OrderIds.Count
            || !trials.Select(x => x.OrderId).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(OrderIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Comparação Gold exige as três ordens pré-declaradas completas.");
        var first = trials[0];
        RequireSha(first.OriginalCorpusSha256);
        RequireSha(first.FrozenSnapshotSha256);
        if (first.TruthBySource is null || first.TruthBySource.Count == 0)
            throw new InvalidDataException("Gabarito TRAIN completo obrigatório.");
        var outputs = new List<Metrics>();
        IReadOnlyDictionary<long, (bool Published, long Representative)>? baselinePartition = null;
        var partitionInvariant = true;
        var runFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trial in trials.OrderBy(x => OrderIds.ToList().IndexOf(x.OrderId)))
        {
            RequireSha(trial.OriginalCorpusSha256);
            RequireSha(trial.FrozenSnapshotSha256);
            RequireSha(trial.IsolatedRunFingerprintSha256);
            if (!runFingerprints.Add(trial.IsolatedRunFingerprintSha256))
                throw new InvalidDataException("Cada ordem exige execução SQL isolada, não reutilização do mesmo run.");
            if (trial.EvidenceScope != "TRAIN_SELECTION"
                || trial.SourceKeyContract != StableSourceKeyContract
                || trial.TestUsed || trial.Activated
                || !trial.AllFourWavesPublished
                || !string.Equals(trial.OriginalCorpusSha256, first.OriginalCorpusSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(trial.FrozenSnapshotSha256, first.FrozenSnapshotSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ordens devem usar corpus TRAIN e fit congelados, sem ativação ou TEST.");
            if (trial.TruthBySource is null || trial.FinalPublishedGoldBySource is null
                || trial.TruthBySource.Count != first.TruthBySource.Count
                || first.TruthBySource.Any(x => !trial.TruthBySource.TryGetValue(x.Key, out var truth)
                    || !string.Equals(truth, x.Value, StringComparison.Ordinal)))
                throw new InvalidDataException("Alteração de identidade-fonte entre ordens invalida comparação.");
            var order = ResolveOrder(trial.OrderId, ["G0", "G1", "G2", "G3"]);
            if (trial.ExecutedWaveOrder is null || !trial.ExecutedWaveOrder.SequenceEqual(order, StringComparer.Ordinal))
                throw new InvalidDataException("Sequência executada diverge do plano congelado.");
            var partition = CanonicalPartition(trial.TruthBySource.Keys, trial.FinalPublishedGoldBySource);
            if (baselinePartition is null) baselinePartition = partition;
            else if (baselinePartition.Count != partition.Count
                || baselinePartition.Any(x => !partition.TryGetValue(x.Key, out var key)
                    || key != x.Value))
                partitionInvariant = false;
            var pair = V9GoldEndToEndAudit.Measure(trial.TruthBySource, trial.FinalPublishedGoldBySource);
            var bcubed = GoldPartitionBcubedAudit.Measure(trial.TruthBySource, trial.FinalPublishedGoldBySource);
            if (pair.Observations != bcubed.Sources
                || pair.PublishedSources != bcubed.PublishedSources
                || pair.PendingSources != bcubed.PendingSources
                || pair.GoldReferences != bcubed.GoldIdentityGroups)
                throw new InvalidDataException("B-cubed e pares Gold não reconciliam.");
            outputs.Add(new(
                trial.OrderId, pair.Observations, pair.PublishedSources, pair.PendingSources,
                pair.PendingSourceRate, pair.TruthIdentitiesRepresented,
                pair.FragmentedTrueIdentities, pair.ImpureGoldReferences,
                pair.AllTrueObservationPairs, pair.CorrectGoldObservationPairs,
                pair.FalseGoldObservationPairs, pair.FinalGoldPairRecall,
                pair.FinalGoldPairPrecision, bcubed.StandardPrecision,
                bcubed.StandardRecall, bcubed.CoverageAwarePrecision,
                bcubed.CoverageAwareRecall));
        }

        var pending = outputs.Select(x => x.PendingSources).ToArray();
        var falsePairs = outputs.Select(x => x.FalseGoldPairs).ToArray();
        return new Comparison(
            Version, first.OriginalCorpusSha256, first.FrozenSnapshotSha256,
            outputs, pending.Max() - pending.Min(), falsePairs.Max() - falsePairs.Min(),
            GoldPartitionOrderInvariant: partitionInvariant, // source-membership equivalence, never UUID byte equality
            IncrementalLinkageFdrComputed: false, // needs per-accepted-link truth, not pairs
            IndependentlyCertified: false, TestUsed: false, OperationallyActivated: false);
    }

    /// <summary>
    /// SHA-256 of the COMPLETE final source membership, including pending
    /// source flags. Source keys MUST be stable IDs read from the frozen TRAIN
    /// CSV, not SQL identity columns. No labels or person UUIDs are exported.
    /// Distinct LocalDB trials with equivalent final partitions yield the
    /// exact same digest even though their generated Gold UUIDs differ.
    /// A hash is an aggregate comparison aid, not a substitute for an
    /// independently authenticated SQL run manifest.
    /// </summary>
    public static string FingerprintStablePartition(
        IReadOnlyDictionary<long, string> truthByStableSource,
        IReadOnlyDictionary<long, Guid> publishedGoldByStableSource)
    {
        ArgumentNullException.ThrowIfNull(truthByStableSource);
        ArgumentNullException.ThrowIfNull(publishedGoldByStableSource);
        if (truthByStableSource.Count == 0 || truthByStableSource.Keys.Any(k => k <= 0)
            || truthByStableSource.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Fonte estável e gabarito TRAIN completos obrigatórios.");
        if (publishedGoldByStableSource.Any(x =>
            x.Key <= 0 || x.Value == Guid.Empty || !truthByStableSource.ContainsKey(x.Key)))
            throw new InvalidDataException("Partição Gold contém UUID ou fonte fora do TRAIN.");
        var partition = CanonicalPartition(truthByStableSource.Keys, publishedGoldByStableSource);
        if (partition.Count != truthByStableSource.Count)
            throw new InvalidDataException("Partição Gold não cobre todas as fontes TRAIN.");
        var canonical = new System.Text.StringBuilder();
        canonical.Append(Version).Append('|').Append(StableSourceKeyContract).Append('\n');
        foreach (var source in partition.OrderBy(x => x.Key))
        {
            canonical.Append(source.Key.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('|').Append(source.Value.Published ? 'P' : 'U').Append('|')
                .Append(source.Value.Representative.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<long, (bool Published, long Representative)> CanonicalPartition(
        IEnumerable<long> allSourceIds, IReadOnlyDictionary<long, Guid> published)
    {
        var result = new Dictionary<long, (bool Published, long Representative)>();
        foreach (var group in published.GroupBy(x => x.Value))
        {
            var representative = group.Min(x => x.Key);
            foreach (var source in group)
                if (!result.TryAdd(source.Key, (true, representative)))
                    throw new InvalidDataException("Fonte publicada mais de uma vez.");
        }
        foreach (var source in allSourceIds)
            result.TryAdd(source, (false, source)); // pending is a singleton with explicit status
        return result;
    }

    private static void RequireSha(string? value)
    {
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Fingerprint SHA-256 obrigatório.");
    }
}

namespace Jornada.Linkage.Runner;

/// <summary>
/// Crosswalk between database-local IDENTITY keys and stable TRAIN CSV source keys.
/// Used strictly AFTER the real incremental Runner has published its final Gold.
/// The exported Trial is PRIVATE evidence, never an aggregate publication artifact.
/// </summary>
public static class GoldTrainOrderEvidence
{
    public sealed record SourceCrosswalk(long StableCsvSourceId, string IndependentTruth);

    public static GoldTrainThreeOrderProtocol.Trial Create(
        string orderId,
        string originalCorpusSha256,
        string frozenSnapshotSha256,
        string isolatedRunFingerprintSha256,
        IReadOnlyList<string> executedWaveOrder,
        IReadOnlyDictionary<long, SourceCrosswalk> sqlObservationToCsv,
        IReadOnlyDictionary<long, Guid> finalGoldBySqlObservation)
    {
        ArgumentNullException.ThrowIfNull(executedWaveOrder);
        ArgumentNullException.ThrowIfNull(sqlObservationToCsv);
        ArgumentNullException.ThrowIfNull(finalGoldBySqlObservation);
        var order = GoldTrainThreeOrderProtocol.ResolveOrder(orderId, ["G0", "G1", "G2", "G3"]);
        if (!executedWaveOrder.SequenceEqual(order, StringComparer.Ordinal))
            throw new InvalidDataException("Ordem executada não corresponde à ordem Gold pré-declarada.");
        if (sqlObservationToCsv.Count == 0)
            throw new InvalidDataException("Crosswalk SQL/CSV vazio.");
        var truth = new Dictionary<long, string>();
        var gold = new Dictionary<long, Guid>();
        foreach (var (sqlId, source) in sqlObservationToCsv)
        {
            if (sqlId <= 0 || source is null || source.StableCsvSourceId <= 0
                || string.IsNullOrWhiteSpace(source.IndependentTruth)
                || !truth.TryAdd(source.StableCsvSourceId, source.IndependentTruth))
                throw new InvalidDataException("Fonte Gold não possui chave CSV estável e única.");
        }
        foreach (var (sqlId, uuid) in finalGoldBySqlObservation)
        {
            if (uuid == Guid.Empty || !sqlObservationToCsv.TryGetValue(sqlId, out var source)
                || !gold.TryAdd(source.StableCsvSourceId, uuid))
                throw new InvalidDataException("Referência Gold aponta para fonte SQL desconhecida/duplicada.");
        }
        // Computes both audits as an additional structural guard BEFORE writing
        // evidence. The three-order protocol will recompute metrics on comparison.
        var pair = V9GoldEndToEndAudit.Measure(truth, gold);
        var bcubed = GoldPartitionBcubedAudit.Measure(truth, gold);
        if (pair.Observations != bcubed.Sources
            || pair.PublishedSources != bcubed.PublishedSources
            || pair.PendingSources != bcubed.PendingSources)
            throw new InvalidDataException("Crosswalk produziu Gold não reconciliado.");
        return new GoldTrainThreeOrderProtocol.Trial(
            orderId, originalCorpusSha256, frozenSnapshotSha256,
            isolatedRunFingerprintSha256, "TRAIN_SELECTION",
            GoldTrainThreeOrderProtocol.StableSourceKeyContract,
            TestUsed: false, Activated: false, AllFourWavesPublished: true,
            ExecutedWaveOrder: Array.AsReadOnly(order.ToArray()),
            TruthBySource: truth,
            FinalPublishedGoldBySource: gold);
    }
}

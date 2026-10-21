using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Compare three public-aggregate TRAIN Gold reports with stable partition SHA.
/// No source-level truth, Gold UUID or SQL access. Requires physical execution
/// evidence produced by the pre-registered three-order harness, never fabricates it.
/// </summary>
public static class GoldTrainThreeOrderAggregateCommand
{
    public const string Flag = "--v9-gold-compare-aggregates";
    public const string Version = "GOLD_TRAIN_THREE_ORDER_AGGREGATES_V1";
    public const string SourceReportName = "gold-runner-study.json";

    public sealed record OrderMetric(
        string OrderId,
        string StablePartitionSha256,
        long Sources,
        long TrueIdentities,
        long PublishedSources,
        long PendingSources,
        double PendingSourceRate,
        long GoldReferences,
        long FragmentedTrueIdentities,
        long ImpureGoldReferences,
        long FalseGoldPairs,
        double? PairRecall,
        double? PairPrecision,
        double BcubedStandardPrecision,
        double BcubedStandardRecall,
        double BcubedCoverageAwarePrecision,
        double BcubedCoverageAwareRecall,
        long CorrectAcceptedLinks,
        long FalseAcceptedLinks,
        double? AcceptedLinkFdr);

    public sealed record Comparison(
        string Version,
        string OriginalCorpusSha256,
        string FrozenSnapshotSha256,
        IReadOnlyList<OrderMetric> Orders,
        bool SameFinalGoldPartition,
        long LargestPendingDifference,
        long LargestFalseGoldPairDifference,
        long LargestAcceptedFalseLinkDifference,
        string Scope,
        bool IdentityLevelEvidenceExported,
        bool TestUsed,
        bool IndependentlyCertified,
        bool OperationallyActivated);

    private sealed record Parsed(OrderMetric Metric, string OrderId, string OriginalCorpusSha,
        string FrozenSnapshotSha, string RunFingerprint, string[] WaveOrder);

    /// <summary>
    /// Inputs MUST be the complete, post-reevaluation reports produced in three
    /// distinct owned LocalDB experiments. A distinct random run fingerprint
    /// is a consistency check, not cryptographic attestation of SQL isolation.
    /// </summary>
    public static Comparison Compare(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count != 3 || reports.Any(x => x is null))
            throw new InvalidDataException("Comparação exige três relatórios físicos completos.");
        var trials = reports.Select(Parse).OrderBy(x =>
            GoldTrainThreeOrderProtocol.OrderIds.ToList().IndexOf(x.OrderId)).ToArray();
        if (!trials.Select(x => x.OrderId).SequenceEqual(GoldTrainThreeOrderProtocol.OrderIds))
            throw new InvalidDataException("Ordens físicas precisam ser baseline, reversa e intercalada, uma vez cada.");
        var first = trials[0];
        if (trials.Any(x =>
                !string.Equals(x.OriginalCorpusSha, first.OriginalCorpusSha, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(x.FrozenSnapshotSha, first.FrozenSnapshotSha, StringComparison.OrdinalIgnoreCase)
                || x.Metric.Sources != first.Metric.Sources
                || x.Metric.TrueIdentities != first.Metric.TrueIdentities)
            || trials.Select(x => x.RunFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("Três ordens devem ter corpus/fit/universo iguais e execuções distintas.");

        var outputs = trials.Select(x => x.Metric).ToArray();
        return new Comparison(Version, first.OriginalCorpusSha, first.FrozenSnapshotSha,
            Array.AsReadOnly(outputs),
            SameFinalGoldPartition: outputs.Select(x => x.StablePartitionSha256)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1,
            LargestPendingDifference: outputs.Max(x => x.PendingSources) - outputs.Min(x => x.PendingSources),
            LargestFalseGoldPairDifference: outputs.Max(x => x.FalseGoldPairs) - outputs.Min(x => x.FalseGoldPairs),
            LargestAcceptedFalseLinkDifference: outputs.Max(x => x.FalseAcceptedLinks) - outputs.Min(x => x.FalseAcceptedLinks),
            Scope: "TRAIN_SELECTION_PRE_REGISTERED_ORDERS",
            IdentityLevelEvidenceExported: false,
            TestUsed: false, IndependentlyCertified: false, OperationallyActivated: false);
    }

    private static Parsed Parse(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            var trial = root.GetProperty("goldThreeOrderTrial");
            var orderId = trial.GetProperty("orderId").GetString()
                ?? throw new InvalidDataException("Ordem ausente.");
            var order = GoldTrainThreeOrderProtocol.ResolveOrder(orderId,
                ["G0", "G1", "G2", "G3"]);
            var observedOrder = trial.GetProperty("executedWaveOrder")
                .EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (!observedOrder.SequenceEqual(order, StringComparer.Ordinal))
                throw new InvalidDataException("Ordem executada diverge da registrada.");
            if (trial.GetProperty("protocol").GetString() != GoldTrainThreeOrderProtocol.Version
                || trial.GetProperty("sourceKeyContract").GetString() != GoldTrainThreeOrderProtocol.StableSourceKeyContract
                || trial.GetProperty("evidenceScope").GetString() != "TRAIN_SELECTION"
                || !trial.GetProperty("allFourWavesPublished").GetBoolean()
                || trial.GetProperty("testUsed").GetBoolean()
                || trial.GetProperty("activated").GetBoolean()
                || trial.GetProperty("independentlyCertified").GetBoolean()
                || trial.GetProperty("truthExported").GetBoolean()
                || trial.GetProperty("uuidExported").GetBoolean())
                throw new InvalidDataException("Relatório não atende escopo seguro do experimento Gold.");
            var corpus = Sha(trial.GetProperty("originalCorpusSha256"));
            var fit = Sha(trial.GetProperty("frozenSnapshotSha256"));
            var run = Sha(trial.GetProperty("isolatedRunFingerprintSha256"));
            var partition = Sha(trial.GetProperty("stablePartitionSha256"));
            if (!string.Equals(fit, Sha(root.GetProperty("snapshotSha256")), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Fit Gold diverge do cabeçalho do estudo.");
            var e2e = root.GetProperty("goldEndToEnd");
            var bc = root.GetProperty("goldBcubed");
            var pub = root.GetProperty("publication");
            var n = e2e.GetProperty("Observations").GetInt64();
            var published = e2e.GetProperty("PublishedSources").GetInt64();
            var pending = e2e.GetProperty("PendingSources").GetInt64();
            var distinctTrue = e2e.GetProperty("TrueIdentities").GetInt64();
            var goldRefs = e2e.GetProperty("GoldReferences").GetInt64();
            var trueAccepted = pub.GetProperty("trueExistingLinks").GetInt64();
            var falseAccepted = pub.GetProperty("falseExistingLinks").GetInt64();
            if (n < 1 || published < 0 || pending < 0 || published + pending != n
                || distinctTrue < 1 || distinctTrue > n
                || goldRefs < 0 || goldRefs > published
                || n != trial.GetProperty("sourceCount").GetInt64()
                || published != trial.GetProperty("publishedSourceCount").GetInt64()
                || n != root.GetProperty("observations").GetInt64()
                || distinctTrue != root.GetProperty("identityGroups").GetInt64()
                || pending != pub.GetProperty("pendingSources").GetInt64()
                || published != bc.GetProperty("PublishedSources").GetInt64()
                || pending != bc.GetProperty("PendingSources").GetInt64()
                || goldRefs != bc.GetProperty("GoldIdentityGroups").GetInt64()
                || trueAccepted < 0 || falseAccepted < 0 || trueAccepted + falseAccepted > published
                || pending != root.GetProperty("pendingReasonBreakdown").EnumerateArray()
                    .Sum(x => x.GetProperty("Sources").GetInt64()))
                throw new InvalidDataException("Contagens do relatório Gold não reconciliam.");
            var fdr = ReadNullableDouble(pub, "fdr");
            if (trueAccepted + falseAccepted == 0 ? fdr is not null :
                fdr is null || Math.Abs(fdr.Value - (double)falseAccepted / (trueAccepted + falseAccepted)) > 1e-9)
                throw new InvalidDataException("FDR publicada diverge dos contadores de associações.");

            var metric = new OrderMetric(
                orderId, partition, n, distinctTrue, published, pending,
                e2e.GetProperty("PendingSourceRate").GetDouble(), goldRefs,
                e2e.GetProperty("FragmentedTrueIdentities").GetInt64(),
                e2e.GetProperty("ImpureGoldReferences").GetInt64(),
                e2e.GetProperty("FalseGoldObservationPairs").GetInt64(),
                ReadNullableDouble(e2e, "FinalGoldPairRecall"),
                ReadNullableDouble(e2e, "FinalGoldPairPrecision"),
                bc.GetProperty("StandardPrecision").GetDouble(),
                bc.GetProperty("StandardRecall").GetDouble(),
                bc.GetProperty("CoverageAwarePrecision").GetDouble(),
                bc.GetProperty("CoverageAwareRecall").GetDouble(),
                trueAccepted, falseAccepted, fdr);
            if (Math.Abs(metric.PendingSourceRate - (double)pending / n) > 1e-9
                || metric.FalseGoldPairs < 0
                || !ValidRate(metric.PairRecall) || !ValidRate(metric.PairPrecision)
                || !ValidRate(metric.AcceptedLinkFdr)
                || !ValidRate(metric.BcubedStandardPrecision)
                || !ValidRate(metric.BcubedStandardRecall)
                || !ValidRate(metric.BcubedCoverageAwarePrecision)
                || !ValidRate(metric.BcubedCoverageAwareRecall))
                throw new InvalidDataException("Métrica Gold não finita ou com denominador inconsistente.");
            return new Parsed(metric, orderId, corpus, fit, run, observedOrder);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException
            or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("Relatório Gold ausente, malformado ou não autenticável.", ex);
        }
    }

    private static bool ValidRate(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
    private static bool ValidRate(double? value) => value is null || ValidRate(value.Value);

    private static string Sha(JsonElement element)
    {
        var value = element.GetString();
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 do ensaio Gold é obrigatório.");
        return value.ToLowerInvariant();
    }

    private static double? ReadNullableDouble(JsonElement obj, string field)
    {
        var x = obj.GetProperty(field);
        return x.ValueKind == JsonValueKind.Null ? null : x.GetDouble();
    }

    public static async Task<Comparison> RunAsync(string[] args)
    {
        if (args is null || args.Length != 5 || args[0] != Flag)
            throw new ArgumentException("Uso: --v9-gold-compare-aggregates <baseline/gold-runner-study.json> <reverse/gold-runner-study.json> <interleaved/gold-runner-study.json> <saida-nova.json>");
        var paths = args.Skip(1).Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4
            || paths.Take(3).Any(x => Path.GetFileName(x) != SourceReportName)
            || paths.Take(3).Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3
            || File.Exists(paths[3]) || Directory.Exists(paths[3]))
            throw new InvalidDataException("Três relatórios Gold de execuções separadas e saída inédita obrigatórios.");
        var inputs = await Task.WhenAll(paths.Take(3).Select(File.ReadAllBytesAsync));
        var result = Compare(inputs);
        await using var stream = new FileStream(paths[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, result, new JsonSerializerOptions { WriteIndented = true });
        return result;
    }
}

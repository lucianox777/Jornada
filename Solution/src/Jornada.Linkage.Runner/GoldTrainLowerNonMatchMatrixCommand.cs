using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Offline, aggregate-only 3 orders x 4 lower-band policies physical TRAIN matrix.
/// Input order is policy-major (current, knee, conservative, least-pending),
/// then the three pre-registered wave orders. Never reads a database or TEST.
/// </summary>
public static class GoldTrainLowerNonMatchMatrixCommand
{
    public const string Flag = "--v9-gold-compare-matrix";
    private static readonly string[] PolicySlots =
        ["CURRENT", "KNEE", "CONSERVATIVE", "LOWEST_PENDING"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed record PendingReason(string RawStatus, string RawReasonCategory,
        string PublicationReasonCategory, long Sources);
    public sealed record Row(string PolicySlot, string Policy, decimal? LowerThreshold,
        GoldTrainThreeOrderAggregateCommand.OrderMetric Metrics,
        long ElapsedMilliseconds, long LowerBandProposals, long LowerBandCreated,
        long FalseNewIdentityCreations, long FalseLowerBandCreations,
        IReadOnlyList<PendingReason> PendingReasons);
    public sealed record Matrix(string Version, string OriginalCorpusSha256,
        string FrozenSnapshotSha256, IReadOnlyList<Row> Rows,
        bool TrainOnly, bool IndependentlyCertified, bool OperationallyActivated);

    public static Matrix Compare(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count != 12 || reports.Any(x => x is null))
            throw new InvalidDataException("Matriz Gold exige 12 relatórios físicos: quatro políticas × três ordens.");

        var rows = new List<Row>(12);
        string? corpus = null, fit = null;
        var seenRunFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinctLowerThresholds = new HashSet<decimal>();
        for (int policyIndex = 0; policyIndex < 4; policyIndex++)
        {
            var group = reports.Skip(policyIndex * 3).Take(3).ToArray();
            var comparison = GoldTrainThreeOrderAggregateCommand.Compare(group);
            if (corpus is null) { corpus = comparison.OriginalCorpusSha256; fit = comparison.FrozenSnapshotSha256; }
            if (corpus != comparison.OriginalCorpusSha256 || fit != comparison.FrozenSnapshotSha256)
                throw new InvalidDataException("Todas as políticas devem usar o mesmo TRAIN e fit V9 congelados.");
            if (policyIndex == 0
                ? comparison.LowerNonMatchPolicy != "CURRENT" || comparison.LowerNonMatchThreshold is not null
                : comparison.LowerNonMatchPolicy != "EXPERIMENTAL_LOWER_TRAIN_ONLY"
                  || comparison.LowerNonMatchThreshold is not { } lower
                  || !distinctLowerThresholds.Add(lower))
                throw new InvalidDataException("Matriz exige controle atual e três limiares inferiores diferentes.");
            for (int orderIndex = 0; orderIndex < 3; orderIndex++)
            {
                using var json = JsonDocument.Parse(group[orderIndex]);
                var root = json.RootElement;
                var trial = root.GetProperty("goldThreeOrderTrial");
                var runFingerprint = trial.GetProperty("isolatedRunFingerprintSha256").GetString()
                    ?? throw new InvalidDataException("Fingerprint da execução ausente.");
                if (!seenRunFingerprints.Add(runFingerprint))
                    throw new InvalidDataException("As 12 tentativas exigem execuções físicas distintas.");
                var ms = trial.GetProperty("elapsedMilliseconds").GetInt64();
                if (ms < 0) throw new InvalidDataException("Latência experimental inválida.");
                var orderId = trial.GetProperty("orderId").GetString();
                var metric = comparison.Orders.Single(x => x.OrderId == orderId);
                var reasons = root.GetProperty("pendingReasonBreakdown").EnumerateArray()
                    .Select(x => new PendingReason(
                        x.GetProperty("RawStatus").GetString() ?? "",
                        x.GetProperty("RawReasonCategory").GetString() ?? "",
                        x.GetProperty("PublicationReasonCategory").GetString() ?? "",
                        x.GetProperty("Sources").GetInt64())).ToArray();
                if (reasons.Any(x => x.Sources < 0 || x.RawStatus.Length == 0
                    || x.RawReasonCategory.Length == 0 || x.PublicationReasonCategory.Length == 0)
                    || reasons.Sum(x => x.Sources) != metric.PendingSources)
                    throw new InvalidDataException("Motivos de pendência inconsistentes.");
                var lowerData = root.GetProperty("lowerNonMatchExperiment");
                var proposals = lowerData.GetProperty("finalLowerBandProposals").GetInt64();
                var created = lowerData.GetProperty("finalLowerBandCreated").GetInt64();
                var falseNew = lowerData.GetProperty("finalFalseNewIdentityCreations").GetInt64();
                var falseLower = lowerData.GetProperty("finalFalseLowerBandCreations").GetInt64();
                rows.Add(new Row(PolicySlots[policyIndex], comparison.LowerNonMatchPolicy,
                    comparison.LowerNonMatchThreshold, metric, ms, proposals, created,
                    falseNew, falseLower, reasons));
            }
        }
        return new Matrix("GOLD_TRAIN_LOWER_NONMATCH_MATRIX_V1", corpus!, fit!,
            rows.AsReadOnly(), TrainOnly: true,
            IndependentlyCertified: false, OperationallyActivated: false);
    }

    public static async Task<Matrix> RunAsync(string[] args)
    {
        if (args is null || args.Length != 14 || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-gold-compare-matrix <12 caminhos gold-runner-study.json: CURRENT, KNEE, CONSERVATIVE, LOWEST_PENDING; 3 ordens cada> <saída-nova.json>");
        var paths = args.Skip(1).Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 13
            || paths.Take(12).Any(x => Path.GetFileName(x) != GoldTrainThreeOrderAggregateCommand.SourceReportName)
            || File.Exists(paths[12]) || Directory.Exists(paths[12]))
            throw new InvalidDataException("Matriz exige doze fontes de execução diferentes e saída inédita.");
        var bytes = await Task.WhenAll(paths.Take(12).Select(path => File.ReadAllBytesAsync(path)));
        var matrix = Compare(bytes);
        await using var stream = new FileStream(paths[12], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, matrix, Json);
        return matrix;
    }
}

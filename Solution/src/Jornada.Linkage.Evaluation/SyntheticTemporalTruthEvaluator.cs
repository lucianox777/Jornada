using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Avaliador isolado: a truth de cada onda só é aberta depois do RASCUNHO.
/// Nunca entrega IDs, atributos ou pares individuais no relatório agregado.
/// </summary>
public static class SyntheticTemporalTruthEvaluator
{
    public const string Version = "JORNADA_SYNTHETIC_TEMPORAL_V1";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public sealed record SourceTruth(
        string Status, string BasePersonId, string GestorCodigo,
        string? OpaquePersonId, string ObservationId);

    public sealed record SourceSnapshot(
        string SourceCode, string GestorCodigo, long LatestObservationId,
        int Version, bool HasCpf, string? CurrentStatus, string? CurrentUuid,
        string? CurrentMethod);

    public sealed record OperationalSnapshot(
        string SchemaVersion, int Wave, long SourceCount, long ObservationCount,
        string DecisionProvenance, string? LinkageRunId, string LinkageRunStatus,
        SourceSnapshot[] Sources);

    public sealed record PairTotals(long BothCpf, long NeitherCpf, long MixedCpf)
    {
        public long Total => checked(BothCpf + NeitherCpf + MixedCpf);
    }

    public sealed record WaveMetric(
        int Wave, string ManifestSha256, string TruthSha256, string SnapshotSha256,
        long CurrentMaterializedSources, long CurrentObservations, PairTotals TruePairs,
        string MeasurementStatus, PairTotals? TruePositive, PairTotals? FalsePositive,
        PairTotals? FalseNegative, decimal? Precision, decimal? Recall);

    public sealed record Report(
        string SchemaVersion, string Purpose, ulong Seed,
        string CorpusInputFingerprintSha256, string WaveManifestSha256,
        IReadOnlyList<WaveMetric> Waves, bool TruthConsumedByIngestionOrCalibrator,
        bool ModelPromotionAttempted, string[] Safeguards);

    private sealed record CumulativeSource(
        string SourceCode, string Gestor, string TruthId, bool HasCpf,
        string? ResolvedUuid);

    /// <summary>Validates the hash chain and all source snapshots before computing any metric.</summary>
    public static async Task<Report> EvaluateAsync(
        string generatedRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(generatedRoot);
        var ingestion = Path.Combine(root, "ingestion");
        var wavesPath = Path.Combine(ingestion, "waves-manifest.json");
        using var wavesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(wavesPath, cancellationToken));
        var manifest = wavesDocument.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1
            || manifest.GetProperty("scenarioVersion").GetString() != "SYNTHETIC_INGESTION_WAVES_V1")
            throw new InvalidDataException("Manifesto temporal inválido.");
        var seed = manifest.GetProperty("seed").GetUInt64();
        var fingerprint = manifest.GetProperty("corpusInputFingerprintSha256").GetString()
            ?? throw new InvalidDataException("Fingerprint do corpus ausente.");
        var waves = manifest.GetProperty("waves");
        if (waves.ValueKind != JsonValueKind.Array || waves.GetArrayLength() is < 2 or > 12)
            throw new InvalidDataException("O ensaio temporal exige de duas a doze ondas.");

        var currentTruth = new Dictionary<string, SourceTruth>(StringComparer.Ordinal);
        var results = new List<WaveMetric>();
        long previousObservations = 0;
        for (var index = 0; index < waves.GetArrayLength(); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = waves[index];
            var number = index + 1;
            if (expected.GetProperty("wave").GetInt32() != number)
                throw new InvalidDataException("Ondas fora de ordem.");
            var directory = Path.Combine(ingestion, "wave-" + number.ToString("D2", CultureInfo.InvariantCulture));
            var manifestPath = Path.Combine(directory, "bridge-manifest.json");
            var truthPath = Path.Combine(directory, "bridge-truth.jsonl");
            var snapshotPath = Path.Combine(directory, "operational-snapshot.json");
            var manifestSha = await Sha256Async(manifestPath, cancellationToken);
            var truthSha = await Sha256Async(truthPath, cancellationToken);
            var snapshotSha = await Sha256Async(snapshotPath, cancellationToken);
            var snapshotSeal = (await File.ReadAllTextAsync(snapshotPath + ".sha256", cancellationToken))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            RequireHash(snapshotSeal, snapshotSha, "snapshot SQL", number);
            RequireHash(expected.GetProperty("manifestSha256").GetString(), manifestSha, "manifesto", number);
            RequireHash(expected.GetProperty("truthSha256").GetString(), truthSha, "truth", number);

            using var bridgeDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var bridge = bridgeDocument.RootElement;
            if (bridge.GetProperty("bridgeVersion").GetString() != "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1"
                || !string.Equals(bridge.GetProperty("corpusInputFingerprintSha256").GetString(),
                    fingerprint, StringComparison.OrdinalIgnoreCase)
                || bridge.GetProperty("materializedObservationCount").GetInt32() !=
                    expected.GetProperty("materializedObservationCount").GetInt32())
                throw new InvalidDataException("Proveniência/contagem do bridge divergente.");

            var changedThisWave = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(truthPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var row = JsonSerializer.Deserialize<SourceTruth>(line, Json)
                    ?? throw new InvalidDataException("Linha inválida no sidecar de truth.");
                if (row.Status != "MATERIALIZADA") continue;
                if (string.IsNullOrWhiteSpace(row.OpaquePersonId)
                    || string.IsNullOrWhiteSpace(row.BasePersonId)
                    || string.IsNullOrWhiteSpace(row.GestorCodigo)
                    || !changedThisWave.Add(row.OpaquePersonId))
                    throw new InvalidDataException("Truth da onda contém fonte duplicada ou incompleta.");
                if (currentTruth.TryGetValue(row.OpaquePersonId, out var old) &&
                    (old.BasePersonId != row.BasePersonId || old.GestorCodigo != row.GestorCodigo))
                    throw new InvalidDataException("Fonte trocou de pessoa verdadeira/gestor ao longo das ondas.");
                currentTruth[row.OpaquePersonId] = row;
            }
            if (changedThisWave.Count != bridge.GetProperty("materializedObservationCount").GetInt32())
                throw new InvalidDataException("Truth materializada diverge do manifesto.");

            var snapshot = JsonSerializer.Deserialize<OperationalSnapshot>(
                await File.ReadAllTextAsync(snapshotPath, cancellationToken), Json)
                ?? throw new InvalidDataException("Snapshot SQL ausente.");
            if (snapshot.SchemaVersion != "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V1"
                || snapshot.Wave != number || snapshot.SourceCount != currentTruth.Count
                || snapshot.ObservationCount != previousObservations + changedThisWave.Count
                || snapshot.Sources.Length != currentTruth.Count)
                throw new InvalidDataException("Snapshot temporal não corresponde ao universo congelado.");
            previousObservations = snapshot.ObservationCount;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cumulative = new List<CumulativeSource>(snapshot.Sources.Length);
            foreach (var source in snapshot.Sources)
            {
                if (!seen.Add(source.SourceCode)
                    || !currentTruth.TryGetValue(source.SourceCode, out var truth)
                    || truth.GestorCodigo != source.GestorCodigo
                    || source.LatestObservationId <= 0 || source.Version <= 0)
                    throw new InvalidDataException("Snapshot contém origem inesperada, repetida ou inválida.");
                cumulative.Add(new CumulativeSource(source.SourceCode, source.GestorCodigo,
                    truth.BasePersonId, source.HasCpf,
                    source.CurrentStatus == "RESOLVIDO" ? source.CurrentUuid : null));
            }

            var truePairs = AggregatePairs(cumulative.GroupBy(x => x.TruthId, StringComparer.Ordinal));
            var measured = snapshot.DecisionProvenance == "LINKAGE_RUN_RESULTADO"
                && Guid.TryParse(snapshot.LinkageRunId, out var runId) && runId != Guid.Empty
                && snapshot.LinkageRunStatus == "CONCLUIDO_SEM_PUBLICACAO";
            PairTotals? tp = null, fp = null, fn = null;
            decimal? precision = null, recall = null;
            if (measured)
            {
                var predicted = AggregatePairs(cumulative.Where(x => x.ResolvedUuid is not null)
                    .GroupBy(x => x.ResolvedUuid!, StringComparer.Ordinal));
                tp = AggregatePairs(cumulative.Where(x => x.ResolvedUuid is not null)
                    .GroupBy(x => (x.TruthId, x.ResolvedUuid)));
                fp = Subtract(predicted, tp);
                fn = Subtract(truePairs, tp);
                precision = predicted.Total == 0 ? null : (decimal)tp.Total / predicted.Total;
                recall = truePairs.Total == 0 ? null : (decimal)tp.Total / truePairs.Total;
            }
            results.Add(new WaveMetric(number, manifestSha, truthSha, snapshotSha,
                snapshot.SourceCount, snapshot.ObservationCount, truePairs,
                measured ? "MEDIDO_RUN_TEMPORAL_VERIFICADO" : "NAO_MEDIDO_RUN_TEMPORAL_AUSENTE",
                tp, fp, fn, precision, recall));
        }
        return new Report(Version, "ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE", seed,
            fingerprint, await Sha256Async(wavesPath, cancellationToken), results,
            false, false, [
                "Gabarito lido exclusivamente pelo avaliador pos-RASCUNHO.",
                "Snapshots SQL congelados no fim de cada onda, sem CPF nominal.",
                "Recall/PPV nulos sem resultados comprovados de Runner por onda.",
                "Avaliacao sintetica nao satisfaz a validacao empirica #31."
            ]);
    }

    private static PairTotals AggregatePairs<TKey>(
        IEnumerable<IGrouping<TKey, CumulativeSource>> groups)
    {
        long both = 0, neither = 0, mixed = 0;
        foreach (var group in groups)
        {
            var rows = group.ToArray();
            var p = rows.LongCount(x => x.HasCpf);
            var a = rows.LongLength - p;
            both = checked(both + Choose2(p));
            neither = checked(neither + Choose2(a));
            mixed = checked(mixed + p * a);
            foreach (var gestor in rows.GroupBy(x => x.Gestor, StringComparer.Ordinal))
            {
                var gp = gestor.LongCount(x => x.HasCpf);
                var ga = gestor.LongCount() - gp;
                both = checked(both - Choose2(gp));
                neither = checked(neither - Choose2(ga));
                mixed = checked(mixed - gp * ga);
            }
        }
        return new PairTotals(both, neither, mixed);
    }

    private static long Choose2(long n) => checked(n * (n - 1) / 2);

    private static PairTotals Subtract(PairTotals a, PairTotals b)
    {
        var result = new PairTotals(a.BothCpf - b.BothCpf,
            a.NeitherCpf - b.NeitherCpf, a.MixedCpf - b.MixedCpf);
        if (result.BothCpf < 0 || result.NeitherCpf < 0 || result.MixedCpf < 0)
            throw new InvalidDataException("Métricas temporais inconsistentes.");
        return result;
    }

    private static void RequireHash(string? expected, string actual, string label, int wave)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 divergente de " + label + " na onda " + wave);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}

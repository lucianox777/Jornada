using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Três ondas sintéticas DEV: cadastro inicial, melhora parcial e consolidação.
/// O gabarito é consultado SOMENTE pelo gerador de futuros registros de origem;
/// nunca é transportado no ZIP ou usado pelo algoritmo de linkage.
/// </summary>
public sealed record SyntheticProgressiveWaveOptions(
    double InitialCpfWithholdingFraction = .25,
    double EarlyCpfArrivalFraction = .50,
    double EarlyNameRepairFraction = .50,
    double EarlyMotherRepairFraction = .50,
    double EarlyBirthDateRepairFraction = .50,
    int DaysBetweenWaves = 30)
{
    public const string Version = "SYNTHETIC_PROGRESSIVE_WAVES_V1";

    public void Validate()
    {
        if (DaysBetweenWaves <= 0 || DaysBetweenWaves > 3650)
            throw new ArgumentOutOfRangeException(nameof(DaysBetweenWaves));
        foreach (var (name, value) in new[]
        {
            (nameof(InitialCpfWithholdingFraction), InitialCpfWithholdingFraction),
            (nameof(EarlyCpfArrivalFraction), EarlyCpfArrivalFraction),
            (nameof(EarlyNameRepairFraction), EarlyNameRepairFraction),
            (nameof(EarlyMotherRepairFraction), EarlyMotherRepairFraction),
            (nameof(EarlyBirthDateRepairFraction), EarlyBirthDateRepairFraction)
        })
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(name, "Probabilidade sintética deve estar em [0,1].");
    }

    public string ConfigSha256() => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
}

/// <param name="InitialCpfStratum">Coorte CONGELADA na primeira onda, nunca reclassificada retrospectivamente.</param>
/// <param name="CurrentCpfStratum">CPF efetivamente observado nesta onda, pode mudar.</param>
/// <param name="Status">MATERIALIZADA, EXCLUIDA_CONTRATO_ATIVO ou SEM_MUDANCA.</param>
/// <param name="OpaquePersonId">Código HMAC estável por registro de origem entre ondas; somente quando já materializado.</param>
public sealed record SyntheticWaveTruthRow(
    int Wave,
    string BasePersonId,
    string ObservationId,
    string Partition,
    string SyntheticGestor,
    string InitialCpfStratum,
    string CurrentCpfStratum,
    string Status,
    string? OpaquePersonId,
    string? ExclusionReason);

public sealed record SyntheticWaveBatch(
    int Wave,
    DateTimeOffset DataReferencia,
    SyntheticIngestionBridgeResult Bridge,
    IReadOnlyList<SyntheticWaveTruthRow> TruthRows,
    int ChangedObservationCount,
    int NewObservedCpfCount,
    int RepairedNameCount,
    int RepairedMotherNameCount,
    int RepairedBirthDateCount,
    int NewlyMaterializedCount);

public sealed record SyntheticProgressiveWaveGeneration(
    SyntheticProgressiveWaveOptions Schedule,
    string ScheduleSha256,
    IReadOnlyList<SyntheticWaveBatch> Waves);

public static class SyntheticProgressiveWaves
{
    public static SyntheticProgressiveWaveGeneration Build(
        SyntheticCorpusGeneration corpus,
        SyntheticIngestionBridgeOptions bridgeOptions,
        SyntheticProgressiveWaveOptions schedule)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(bridgeOptions);
        ArgumentNullException.ThrowIfNull(schedule);
        bridgeOptions.Validate();
        schedule.Validate();

        var people = corpus.People.ToDictionary(x => x.BasePersonId, StringComparer.Ordinal);
        // Uma identidade operacional por pessoa/Secretaria. Repetições dentro do
        // mesmo Gestor na massa V2 não geram um segundo codigoPessoaOrigem.
        var original = corpus.Observations
            .GroupBy(x => (x.BasePersonId, x.Gestor))
            .Select(x => x.OrderBy(row => row.ObservationId, StringComparer.Ordinal).First())
            .OrderBy(x => x.BasePersonId, StringComparer.Ordinal)
            .ThenBy(x => x.Gestor, StringComparer.Ordinal)
            .ToArray();

        if (original.Length == 0)
            throw new ArgumentException("Corpus sem observações para ondas.", nameof(corpus));
        var current = original.Select(Clone).ToArray();
        foreach (var item in current)
        {
            var person = people[item.BasePersonId];
            if (item.Cpf is not null && person.Cpf is not null
                && Decision(corpus.Options.Seed, item.ObservationId,
                    "INITIAL_CPF_WITHHOLD", schedule.InitialCpfWithholdingFraction))
                item.Cpf = null;
        }

        var initialCpfStratum = current.ToDictionary(
            x => x.ObservationId,
            x => Stratum(x.Cpf),
            StringComparer.Ordinal);
        var everMaterialized = new Dictionary<string, string>(StringComparer.Ordinal);
        var batches = new List<SyntheticWaveBatch>(3);

        for (var wave = 1; wave <= 3; wave++)
        {
            var changed = new List<SyntheticObservation>();
            var newCpf = 0;
            var improvedName = 0;
            var improvedMother = 0;
            var improvedBirth = 0;
            foreach (var item in current)
            {
                if (wave == 1)
                {
                    changed.Add(Clone(item));
                    continue;
                }

                var person = people[item.BasePersonId];
                var before = Clone(item);
                var early = wave == 2;
                if (person.Cpf is not null
                    && item.Cpf is null
                    && (!early || Decision(corpus.Options.Seed, item.ObservationId,
                        "EARLY_CPF", schedule.EarlyCpfArrivalFraction)))
                    item.Cpf = person.Cpf;

                if (item.Name != person.Name
                    && (!early || Decision(corpus.Options.Seed, item.ObservationId,
                        "EARLY_NAME", schedule.EarlyNameRepairFraction)))
                    item.Name = person.Name;

                if (item.MotherName != person.MotherName
                    && (!early || Decision(corpus.Options.Seed, item.ObservationId,
                        "EARLY_MOTHER", schedule.EarlyMotherRepairFraction)))
                    item.MotherName = person.MotherName;

                if (item.BirthDate != person.BirthDate
                    && (!early || Decision(corpus.Options.Seed, item.ObservationId,
                        "EARLY_BIRTH", schedule.EarlyBirthDateRepairFraction)))
                    item.BirthDate = person.BirthDate;

                if (Equivalent(before, item))
                    continue; // Sem CDC inventado: apenas registros realmente alterados.
                if (before.Cpf is null && item.Cpf is not null) newCpf++;
                if (before.Name != item.Name) improvedName++;
                if (before.MotherName != item.MotherName) improvedMother++;
                if (before.BirthDate != item.BirthDate) improvedBirth++;
                changed.Add(Clone(item));
            }

            var date = bridgeOptions.DataReferencia.AddDays(
                checked((wave - 1) * schedule.DaysBetweenWaves));
            var waveCorpus = new SyntheticCorpusGeneration(
                corpus.People, changed, SyntheticCorpusGenerator.ComputeEmpiricalM(changed),
                corpus.Options);
            var bridge = SyntheticIngestionBridge.Build(
                waveCorpus, bridgeOptions with { DataReferencia = date });
            var bridgeTruth = bridge.TruthRows.ToDictionary(
                x => x.ObservationId, StringComparer.Ordinal);
            var newlyMaterialized = 0;
            var truth = new List<SyntheticWaveTruthRow>(current.Length);

            foreach (var item in current)
            {
                bridgeTruth.TryGetValue(item.ObservationId, out var submitted);
                var status = submitted?.Status ?? "SEM_MUDANCA";
                var opaqueId = submitted?.OpaquePersonId;
                if (opaqueId is not null)
                {
                    if (everMaterialized.TryGetValue(item.ObservationId, out var prior))
                    {
                        if (!string.Equals(prior, opaqueId, StringComparison.Ordinal))
                            throw new InvalidDataException("codigoPessoaOrigem mudou entre ondas.");
                    }
                    else
                    {
                        everMaterialized.Add(item.ObservationId, opaqueId);
                        newlyMaterialized++;
                    }
                }
                else
                    everMaterialized.TryGetValue(item.ObservationId, out opaqueId);

                truth.Add(new SyntheticWaveTruthRow(
                    wave,
                    item.BasePersonId,
                    item.ObservationId,
                    item.Partition,
                    item.Gestor,
                    initialCpfStratum[item.ObservationId],
                    Stratum(item.Cpf),
                    status,
                    opaqueId,
                    submitted?.ExclusionReason));
            }

            batches.Add(new SyntheticWaveBatch(
                wave, date, bridge, truth, changed.Count, newCpf, improvedName,
                improvedMother, improvedBirth, newlyMaterialized));
        }

        return new SyntheticProgressiveWaveGeneration(
            schedule, schedule.ConfigSha256(), batches);
    }

    private static SyntheticObservation Clone(SyntheticObservation item)
        => new()
        {
            ObservationId = item.ObservationId,
            BasePersonId = item.BasePersonId,
            Partition = item.Partition,
            Gestor = item.Gestor,
            Name = item.Name,
            MotherName = item.MotherName,
            BirthDate = item.BirthDate,
            Sex = item.Sex,
            Cpf = item.Cpf,
            Cns = item.Cns,
            EvaluationWeight = item.EvaluationWeight,
            Corruptions = item.Corruptions
        };

    private static bool Equivalent(SyntheticObservation a, SyntheticObservation b)
        => a.Name == b.Name && a.MotherName == b.MotherName
            && a.BirthDate == b.BirthDate && a.Cpf == b.Cpf && a.Cns == b.Cns;

    private static string Stratum(string? cpf)
        => cpf is null ? "WITHOUT_CPF" : "WITH_CPF";

    private static bool Decision(
        ulong seed, string id, string purpose, double probability)
    {
        if (probability <= 0) return false;
        if (probability >= 1) return true;
        var input = Encoding.UTF8.GetBytes(
            $"{SyntheticProgressiveWaveOptions.Version}|{seed}|{purpose}|{id}");
        var digest = SHA256.HashData(input);
        var sample = BinaryPrimitives.ReadUInt64BigEndian(digest);
        return sample / (double)ulong.MaxValue < probability;
    }
}

/// <summary>
/// Pacotes reais Pessoa v4, separados por data/onda, mais manifesto público de
/// métricas agregadas e sidecar PRIVADO de verdade para avaliação pós-RASCUNHO.
/// A escrita não publica pacotes na API nem executa o resolvedor.
/// </summary>
public static class SyntheticProgressiveWaveMaterializer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task<string> WriteAsync(
        string outputDirectory,
        SyntheticProgressiveWaveGeneration generated,
        SyntheticIngestionBridgeOptions bridgeOptions,
        string corpusInputFingerprintSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(bridgeOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusInputFingerprintSha256);
        Directory.CreateDirectory(outputDirectory);
        var waveManifests = new List<object>();

        foreach (var wave in generated.Waves)
        {
            var folder = $"wave-{wave.Wave:D2}";
            var dir = Path.Combine(outputDirectory, folder, "ingestion");
            var materialized = await SyntheticIngestionBridgeMaterializer.WriteAsync(
                dir, wave.Bridge,
                bridgeOptions with { DataReferencia = wave.DataReferencia },
                corpusInputFingerprintSha256, cancellationToken);
            waveManifests.Add(new
            {
                wave = wave.Wave,
                dataReferencia = wave.DataReferencia,
                folder,
                changedObservationCount = wave.ChangedObservationCount,
                materializedCount = wave.Bridge.MaterializedObservationCount,
                excludedCount = wave.Bridge.ExcludedObservationCount,
                newlyMaterializedCount = wave.NewlyMaterializedCount,
                newlyObservedCpfCount = wave.NewObservedCpfCount,
                repairedNameCount = wave.RepairedNameCount,
                repairedMotherNameCount = wave.RepairedMotherNameCount,
                repairedBirthDateCount = wave.RepairedBirthDateCount,
                bridgeManifestSha256 = materialized.ManifestSha256,
                packages = wave.Bridge.Packages.Select(x => new
                {
                    x.GestorCodigo,
                    x.CodigoSistemaOrigem,
                    x.FileName,
                    sha256 = x.Sha256.ToUpperInvariant(),
                    x.PeopleCount
                }).ToArray()
            });
        }

        var truthPath = Path.Combine(outputDirectory, "wave-truth.jsonl");
        var truthLines = generated.Waves.SelectMany(w => w.TruthRows)
            .Select(row => JsonSerializer.Serialize(row));
        await File.WriteAllTextAsync(
            truthPath, string.Join("\n", truthLines) + "\n", Utf8NoBom, cancellationToken);
        var truthHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(truthPath, cancellationToken)));

        var manifest = new
        {
            schemaVersion = 1,
            version = SyntheticProgressiveWaveOptions.Version,
            scheduleSha256 = generated.ScheduleSha256,
            schedule = generated.Schedule,
            corpusInputFingerprintSha256 = corpusInputFingerprintSha256.ToUpperInvariant(),
            pseudonymizationKeySha256 = generated.Waves[0].Bridge.PseudonymizationKeySha256,
            truthSidecar = new
            {
                path = Path.GetFileName(truthPath),
                sha256 = truthHash,
                containsSyntheticGroundTruth = true,
                allowedForScoring = false
            },
            phases = new[]
            {
                "wave-01: initial observation and frozen CPF cohort",
                "wave-02: configured subset gains CPF or better name/mother/birth date",
                "wave-03: remaining fields improve to synthetic truth (no score truth leakage)"
            },
            noChangePolicy = "no package row when all observed fields are unchanged",
            sourceIdentityPolicy = "same HMAC observation key within same manager across waves",
            scope = "DEV_SYNTHETIC_PACKAGE_GENERATION_ONLY_NOT_AN_EXECUTED_LINKAGE_EVALUATION",
            waves = waveManifests
        };
        var path = Path.Combine(outputDirectory, "wave-manifest.json");
        await File.WriteAllTextAsync(
            path, JsonSerializer.Serialize(manifest, JsonOptions) + "\n",
            Utf8NoBom, cancellationToken);
        return path;
    }
}

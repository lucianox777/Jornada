using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Ensaio;

/// <summary>
/// Operational DEV rehearsal for successive synthetic deliveries. This runner reads
/// only package manifests and operational HMAC source IDs; synthetic truth remains
/// outside the ingestion/calibration process and no model is activated.
/// </summary>
public sealed partial class SyntheticCalibrationDevRunner
{
    public const string WaveMode = "SYNTHETIC_WAVES_DEV";

    public async Task<int> RunWavesAsync(CancellationToken cancellationToken)
    {
        var settings = SyntheticCalibrationSettings.FromConfiguration(
            configuration, options, FindSolutionRoot());
        await AssertDevelopmentEnvironmentAsync(settings, cancellationToken);

        var baseline = await ReadBaselineAsync(cancellationToken);
        if (baseline.SyntheticOrigins != 0 || baseline.SyntheticObservations != 0
            || baseline.ScaleOrigins != 0 || baseline.DraftModels != 0)
            throw new InvalidOperationException(
                "SYNTHETIC_WAVES_DEV exige banco Development limpo de SYNTH/SCALE e RASCUNHOs.");

        var waveCount = configuration.GetValue<int>("Ensaio:SyntheticCalibration:WaveCount", 3);
        if (waveCount is < 2 or > 12)
            throw new InvalidOperationException("WaveCount deve estar entre 2 e 12.");

        Directory.CreateDirectory(settings.RunDirectory);
        Directory.CreateDirectory(settings.RuntimeBronzeDirectory);
        Directory.CreateDirectory(settings.RuntimeStagingDirectory);
        await RestoreAndBuildAsync(settings, cancellationToken);
        await GenerateWavePackagesAsync(settings, waveCount, cancellationToken);

        var ingestionRoot = Path.Combine(settings.GeneratedDirectory, "ingestion");
        var waveManifestPath = Path.Combine(ingestionRoot, "waves-manifest.json");
        var key = Environment.GetEnvironmentVariable(settings.PseudonymizationKeyEnvironment)!;
        var expectedKeyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var expectedManifestHashes = new List<string>(waveCount);
        await using (var waveStream = File.OpenRead(waveManifestPath))
        {
            using var document = await JsonDocument.ParseAsync(
                waveStream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1
                || root.GetProperty("scenarioVersion").GetString() !=
                    "SYNTHETIC_INGESTION_WAVES_V1"
                || root.GetProperty("bridgeVersion").GetString() !=
                    "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1"
                || root.GetProperty("seed").GetUInt64() != settings.Seed
                || !string.Equals(root.GetProperty("pseudonymizationKeySha256").GetString(),
                    expectedKeyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Proveniência inválida no waves-manifest.");

            var waves = root.GetProperty("waves");
            if (waves.ValueKind != JsonValueKind.Array || waves.GetArrayLength() != waveCount)
                throw new InvalidDataException("Manifesto de ondas diverge do WaveCount solicitado.");
            for (var index = 0; index < waveCount; index++)
            {
                var item = waves[index];
                var hash = item.GetProperty("manifestSha256").GetString();
                if (item.GetProperty("wave").GetInt32() != index + 1 || !IsSha256(hash))
                    throw new InvalidDataException("Ordem ou hash inválido no manifesto de ondas.");
                expectedManifestHashes.Add(hash!);
            }
        }
        var credentials = await ReadDevelopmentCredentialsAsync(
            settings.DevelopmentKeysPath, new[] { "SEHAB", "SMADS", "SMDET", "SMS" },
            cancellationToken);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        await using var api = StartRuntimeProcess(
            settings, "Jornada.Api",
            Path.Combine(settings.SolutionRoot, "src", "Jornada.Api", "Jornada.Api.csproj"),
            isApi: true);
        await using var processor = StartRuntimeProcess(
            settings, "Jornada.Processor.Worker",
            Path.Combine(settings.SolutionRoot, "src", "Jornada.Processor.Worker",
                "Jornada.Processor.Worker.csproj"), isApi: false);
        await WaitForApiReadyAsync(http, settings, api, processor, cancellationToken);

        var previous = await ReadMaterializedCountsAsync(cancellationToken);
        var seenSourceCpf = new Dictionary<string, bool>(StringComparer.Ordinal);
        var seenDeliveryIds = new HashSet<string>(StringComparer.Ordinal);
        var checkpoints = new List<SyntheticWaveOperationalCheckpoint>();
        string? corpusFingerprint = null;

        for (var wave = 0; wave < waveCount; wave++)
        {
            var directory = Path.Combine(ingestionRoot,
                "wave-" + (wave + 1).ToString("D2", CultureInfo.InvariantCulture));
            var manifestPath = Path.Combine(directory, "bridge-manifest.json");
            await using (var manifestStream = File.OpenRead(manifestPath))
            {
                var hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(manifestStream, cancellationToken));
                if (!string.Equals(hash, expectedManifestHashes[wave],
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Hash do manifesto da onda {wave + 1} diverge do waves-manifest.");
            }
            var manifest = await ReadBridgeManifestAsync(manifestPath, cancellationToken);
            ValidateWaveManifest(manifest, settings, wave, corpusFingerprint, expectedKeyHash);
            corpusFingerprint ??= manifest.CorpusInputFingerprintSha256;

            var newlySeen = 0;
            var updated = 0;
            var cpfRevealed = 0;
            var deliveries = new List<SyntheticDeliveryEvidence>();
            var waveSourceCodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var package in manifest.Packages.OrderBy(
                         x => x.GestorCodigo, StringComparer.Ordinal))
            {
                api.ThrowIfExited();
                processor.ThrowIfExited();
                var path = Path.Combine(directory, package.FileName);
                await VerifyWavePackageAsync(package, path, cancellationToken);
                foreach (var source in ReadOperationalSources(path))
                {
                    if (!waveSourceCodes.Add(source.Code))
                        throw new InvalidDataException("Código-fonte duplicado na mesma onda.");
                    if (!seenDeliveryIds.Add(source.DeliveryId))
                        throw new InvalidDataException("ID de entrega sintética repetido entre ondas.");
                    if (seenSourceCpf.TryGetValue(source.Code, out var hadCpf))
                    {
                        updated++;
                        if (hadCpf && !source.HasCpf)
                            throw new InvalidDataException("Cenário de recuperação perdeu CPF observado.");
                        if (!hadCpf && source.HasCpf) cpfRevealed++;
                        seenSourceCpf[source.Code] = hadCpf || source.HasCpf;
                    }
                    else
                    {
                        seenSourceCpf.Add(source.Code, source.HasCpf);
                        newlySeen++;
                    }
                }

                var id = await PostPackageAsync(
                    http, settings, package, path,
                    credentials[package.GestorCodigo], cancellationToken);
                var status = await WaitForDeliveryAsync(
                    http, settings, package.GestorCodigo,
                    credentials[package.GestorCodigo], id, processor, cancellationToken);
                deliveries.Add(new SyntheticDeliveryEvidence(
                    package.GestorCodigo, package.CodigoSistemaOrigem,
                    package.FileName, package.Sha256, package.PeopleCount, id, status));
            }

            if (waveSourceCodes.Count != manifest.MaterializedObservationCount)
                throw new InvalidDataException(
                    "Total de identificadores operacionais diverge das pessoas do manifesto.");

            var after = await ReadMaterializedCountsAsync(cancellationToken);
            var originDelta = after.SyntheticOrigins - previous.SyntheticOrigins;
            var observationDelta = after.SyntheticObservations - previous.SyntheticObservations;
            if (originDelta != newlySeen || observationDelta != manifest.MaterializedObservationCount)
                throw new InvalidOperationException(
                    $"Onda {wave + 1}: divergência entre ZIP e Silver: " +
                    $"origens novas={originDelta}/{newlySeen}; " +
                    $"observações novas={observationDelta}/{manifest.MaterializedObservationCount}.");

            var snapshotSha256 = await WriteWaveOperationalSnapshotAsync(
                directory, wave + 1, after.SyntheticOrigins, after.SyntheticObservations,
                seenSourceCpf, cancellationToken);
            checkpoints.Add(new SyntheticWaveOperationalCheckpoint(
                wave + 1, manifest.DataReferencia, manifest.BridgeVersion,
                manifest.SourceObservationCount, manifest.MaterializedObservationCount,
                manifest.ExcludedObservationCount, newlySeen, updated, cpfRevealed,
                after.SyntheticOrigins, after.SyntheticObservations,
                snapshotSha256, deliveries));
            previous = after;
            Console.WriteLine(
                $"WAVE {wave + 1}/{waveCount}: origens novas={newlySeen}, atualizações={updated}, " +
                $"CPF revelados={cpfRevealed}, observações acumuladas={after.SyntheticObservations}.");
        }

        var beforeModel = await ReadMaxModelVersionAsync(cancellationToken);
        await EnsureNameFrequencySnapshotAsync(settings, cancellationToken);
        await RunGenerateDraftAsync(settings, cancellationToken);
        var model = await ReadSingleNewDraftAsync(beforeModel, cancellationToken);
        var validation = await RunModelValidationAsync(
            settings, model, checked((int)previous.SyntheticObservations), cancellationToken);

        // There is not yet a single truth/manifest pair spanning all wave sidecars.
        // A per-wave PPV/recall report would be misleading until the evaluator
        // supports a versioned temporal truth and frozen comparison windows.
        var report = new
        {
            reportVersion = "SYNTHETIC_WAVES_DEV_OPERATIONAL_V1",
            environmentProfile = RequiredEnvironment,
            generatedAtUtc = DateTimeOffset.UtcNow,
            settings.Seed,
            waveCount,
            corpusInputFingerprintSha256 = corpusFingerprint,
            pseudonymizationKeySha256 = expectedKeyHash,
            baseline,
            checkpoints,
            model,
            modelValidation = validation,
            truthConsumedByIngestionOrCalibrator = false,
            truthConsumedByEvaluation = false,
            modelPromotionAttempted = false,
            recallPerWave = (double?)null,
            recallStatus = "NAO_MEDIDO_AVALIADOR_TEMPORAL_PENDENTE"
        };
        var reportPath = Path.Combine(settings.RunDirectory, "synthetic-waves-dev.json");
        var json = JsonSerializer.Serialize(report, JsonWriteOptions) + "\n";
        await File.WriteAllTextAsync(reportPath, json, new UTF8Encoding(false), cancellationToken);
        var reportHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await File.WriteAllTextAsync(reportPath + ".sha256", reportHash + "  synthetic-waves-dev.json\n",
            new UTF8Encoding(false), cancellationToken);
        Console.WriteLine($"SYNTHETIC WAVES DEV: OK report={reportPath}");
        return 0;
    }

    private async Task GenerateWavePackagesAsync(
        SyntheticCalibrationSettings settings, int count, CancellationToken cancellationToken)
    {
        var key = Environment.GetEnvironmentVariable(settings.PseudonymizationKeyEnvironment);
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 16)
            throw new InvalidOperationException("Chave de pseudonimização DEV ausente ou curta.");
        var project = Path.Combine(settings.SolutionRoot, "src", "Jornada.Linkage.SyntheticCorpus",
            "Jornada.Linkage.SyntheticCorpus.csproj");
        var arguments = new List<string>
        {
            "run", "--project", project, "--configuration", "Release", "--no-build", "--",
            "generate-ingestion-waves",
            "--reference-root", settings.ReferenceRoot,
            "--out", settings.GeneratedDirectory,
            "--people", settings.People.ToString(CultureInfo.InvariantCulture),
            "--seed", settings.Seed.ToString(CultureInfo.InvariantCulture),
            "--error-profile", settings.ErrorProfile,
            "--gestores", "4", "--pessoa-schema-versao", "4",
            "--data-referencia", settings.DataReferencia.ToString("O", CultureInfo.InvariantCulture),
            "--wave-count", count.ToString(CultureInfo.InvariantCulture),
            "--pseudonymization-key-env", settings.PseudonymizationKeyEnvironment
        };
        foreach (var (setting, argument) in new[]
        {
            ("WaveDelayedArrivalRate", "--wave-delayed-arrival-rate"),
            ("WaveCpfRevealRate", "--wave-cpf-reveal-rate"),
            ("WaveNameCorrectionRate", "--wave-name-correction-rate"),
            ("WaveMotherCorrectionRate", "--wave-mother-correction-rate"),
            ("WaveBirthRecoveryRate", "--wave-birth-recovery-rate")
        })
        {
            var value = configuration["Ensaio:SyntheticCalibration:" + setting];
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
                || !double.IsFinite(rate) || rate is < 0 or > 1)
                throw new InvalidOperationException(setting + " exige taxa entre zero e um.");
            arguments.Add(argument);
            arguments.Add(rate.ToString("R", CultureInfo.InvariantCulture));
        }

        foreach (var (setting, argument) in new[]
        {
            ("StratifiedErrorsConfig", "--stratified-errors-config"),
            ("BrazilianNameErrorsConfig", "--brazilian-name-errors-config")
        })
        {
            var value = configuration["Ensaio:SyntheticCalibration:" + setting];
            if (string.IsNullOrWhiteSpace(value)) continue;
            var fullPath = Path.GetFullPath(value, settings.SolutionRoot);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Configuração DEV sintética não localizada.", fullPath);
            arguments.Add(argument);
            arguments.Add(fullPath);
        }

        await RunProcessAsync(settings.SolutionRoot, "dotnet",
            arguments, environment: null, cancellationToken);
    }

    private static void ValidateWaveManifest(
        SyntheticBridgeManifest manifest, SyntheticCalibrationSettings settings,
        int wave, string? fingerprint, string expectedKeyHash)
    {
        if (manifest.BridgeVersion != "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1"
            || manifest.PessoaSchemaVersao != 4
            || manifest.DataReferencia != settings.DataReferencia.AddDays(wave)
            || !IsSha256(manifest.CorpusInputFingerprintSha256)
            || !string.Equals(manifest.PseudonymizationKeySha256, expectedKeyHash,
                StringComparison.OrdinalIgnoreCase)
            || (fingerprint is not null &&
                !string.Equals(fingerprint, manifest.CorpusInputFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase))
            || manifest.SourceObservationCount < manifest.MaterializedObservationCount
            || manifest.ExcludedObservationCount !=
                manifest.SourceObservationCount - manifest.MaterializedObservationCount
            || manifest.Packages is null
            || manifest.Packages.Length > 4
            || manifest.Packages.Sum(x => x.PeopleCount) != manifest.MaterializedObservationCount
            || manifest.Packages.Select(x => x.GestorCodigo).Distinct(StringComparer.Ordinal).Count()
                != manifest.Packages.Length)
            throw new InvalidDataException($"bridge-manifest inválido para a onda {wave + 1}.");

        foreach (var item in manifest.Packages)
        {
            if (item.PeopleCount <= 0 || item.PeopleCount > settings.ProcessorMaxPeoplePerDelivery
                || !IsSha256(item.Sha256)
                || !string.Equals(item.FileName, Path.GetFileName(item.FileName), StringComparison.Ordinal))
                throw new InvalidDataException("Pacote inválido ou inseguro no manifesto de ondas.");
        }
    }

    private static async Task VerifyWavePackageAsync(
        SyntheticBridgePackage package, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 do pacote da onda diverge do manifesto.");
    }

    private static IEnumerable<SyntheticOperationalSource> ReadOperationalSources(string packagePath)
    {
        using var zip = ZipFile.OpenRead(packagePath);
        var entry = zip.GetEntry("pessoas.jsonl")
            ?? throw new InvalidDataException("ZIP sintético sem pessoas.jsonl.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var code = root.GetProperty("codigoPessoaOrigem").GetString();
            var deliveryId = root.GetProperty("idPessoaEntrega").GetString();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(deliveryId)
                || !code.StartsWith("SYNTH-", StringComparison.Ordinal)
                || !deliveryId.StartsWith("SYNTH-DEL-", StringComparison.Ordinal))
                throw new InvalidDataException("ZIP contém identidade operacional sintética inválida.");
            var cpf = root.GetProperty("cpf");
            yield return new SyntheticOperationalSource(
                code, deliveryId, cpf.ValueKind == JsonValueKind.String);
        }
    }

    private sealed record SyntheticOperationalSource(string Code, string DeliveryId, bool HasCpf);

    private sealed record SyntheticWaveOperationalCheckpoint(
        int Wave, DateTimeOffset DataReferencia, string BridgeVersion,
        int SourceObservations, int MaterializedObservations, int ExcludedObservations,
        int NewSources, int UpdatedSources, int CpfRevealed,
        long TotalSources, long TotalObservations,
        string SnapshotSha256, IReadOnlyList<SyntheticDeliveryEvidence> Deliveries);
}

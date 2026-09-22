using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Jornada.Ensaio;

/// <summary>
/// Harness DEV de recuperação de parâmetros: gera corpus sintético por processo separado,
/// envia os pacotes pela API real, aguarda o Processor e executa o Parameters.Worker real.
/// Nunca lê a truth linha a linha e nunca valida/ativa o modelo criado.
/// </summary>
public sealed class SyntheticCalibrationDevRunner(
    IConfiguration configuration,
    EnsaioRuntimeOptions options,
    Func<DbConnection> openConnection)
{
    public const string Mode = "SYNTHETIC_CALIBRATION_DEV";
    private const string EnvironmentProperty = "Jornada.EnvironmentProfile";
    private const string RequiredEnvironment = "Development";
    private const string RequiredSolutionSchema = "3.70";
    private const string DefaultKeyEnvironment = "JORNADA_SYNTH_PSEUDONYMIZATION_KEY";

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] ForwardedParameterSettings =
    [
        "AlgorithmVersion",
        "NormalizationVersion",
        "TrainingSampleSize",
        "TrainingSamplePoolSize",
        "SmoothingAlpha",
        "ReadCommandTimeoutSeconds",
        "MinimumIndependentMatchedPairs"
    ];

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var solutionRoot = FindSolutionRoot();
        var settings = SyntheticCalibrationSettings.FromConfiguration(configuration, options, solutionRoot);
        await AssertDevelopmentEnvironmentAsync(settings, cancellationToken);

        var baseline = await ReadBaselineAsync(cancellationToken);
        if (baseline.SyntheticOrigins != 0 || baseline.SyntheticObservations != 0)
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV exige zero origem/observação SYNTH-* prévia; " +
                $"origens={baseline.SyntheticOrigins}, observações={baseline.SyntheticObservations}. " +
                "Use um banco DEV limpo para não misturar execuções.");
        }

        if (baseline.ScaleOrigins != 0)
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV recusa o corpus SCALE canônico; origens SCALE-*={baseline.ScaleOrigins}. " +
                "Recrie o banco local com local-db reset --no-synthetic-corpus antes do ensaio.");
        }

        if (baseline.DraftModels != 0)
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV exige zero modelo RASCUNHO prévio; encontrados={baseline.DraftModels}.");

        Directory.CreateDirectory(settings.RunDirectory);
        Directory.CreateDirectory(settings.RuntimeBronzeDirectory);
        Directory.CreateDirectory(settings.RuntimeStagingDirectory);

        await RestoreAndBuildAsync(settings, cancellationToken);
        await GeneratePackagesAsync(settings, cancellationToken);

        var bridgeManifestPath = Path.Combine(
            settings.GeneratedDirectory,
            "ingestion",
            "bridge-manifest.json");
        var manifest = await ReadBridgeManifestAsync(bridgeManifestPath, cancellationToken);
        ValidateBridgeManifest(manifest, settings);

        var credentials = await ReadDevelopmentCredentialsAsync(
            settings.DevelopmentKeysPath,
            manifest.Packages.Select(x => x.GestorCodigo).Distinct(StringComparer.Ordinal),
            cancellationToken);
        await ValidatePackageHashesAsync(settings, manifest, cancellationToken);

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        await using var api = StartRuntimeProcess(
            settings,
            "Jornada.Api",
            Path.Combine(settings.SolutionRoot, "src", "Jornada.Api", "Jornada.Api.csproj"),
            isApi: true);
        await using var processor = StartRuntimeProcess(
            settings,
            "Jornada.Processor.Worker",
            Path.Combine(settings.SolutionRoot, "src", "Jornada.Processor.Worker", "Jornada.Processor.Worker.csproj"),
            isApi: false);

        await WaitForApiReadyAsync(http, settings, api, processor, cancellationToken);

        var deliveries = new List<SyntheticDeliveryEvidence>();
        foreach (var package in manifest.Packages.OrderBy(x => x.GestorCodigo, StringComparer.Ordinal))
        {
            api.ThrowIfExited();
            processor.ThrowIfExited();

            var credential = credentials[package.GestorCodigo];
            var packagePath = PackagePath(settings, package.FileName);
            var deliveryId = await PostPackageAsync(
                http,
                settings,
                package,
                packagePath,
                credential,
                cancellationToken);
            var status = await WaitForDeliveryAsync(
                http,
                settings,
                package.GestorCodigo,
                credential,
                deliveryId,
                processor,
                cancellationToken);
            deliveries.Add(new SyntheticDeliveryEvidence(
                package.GestorCodigo,
                package.CodigoSistemaOrigem,
                package.FileName,
                package.Sha256,
                package.PeopleCount,
                deliveryId,
                status));
        }

        var materialized = await ReadMaterializedCountsAsync(cancellationToken);
        if (materialized.SyntheticOrigins != manifest.MaterializedObservationCount
            || materialized.SyntheticObservations != manifest.MaterializedObservationCount)
        {
            throw new InvalidOperationException(
                "Contagem materializada diverge do bridge-manifest: " +
                $"manifest={manifest.MaterializedObservationCount}, " +
                $"origens={materialized.SyntheticOrigins}, observações={materialized.SyntheticObservations}.");
        }

        var versionBefore = await ReadMaxModelVersionAsync(cancellationToken);
        await RunNameFrequencySnapshotLoaderAsync(settings, cancellationToken);
        await RunGenerateDraftAsync(settings, cancellationToken);
        var model = await ReadSingleNewDraftAsync(versionBefore, cancellationToken);

        var report = new SyntheticCalibrationDevEvidence(
            "SYNTHETIC_CALIBRATION_DEV_V1",
            RequiredEnvironment,
            DateTimeOffset.UtcNow,
            settings.Seed,
            settings.People,
            settings.ErrorProfile,
            manifest.BridgeVersion,
            manifest.GeneratorVersion,
            manifest.RulesetVersion,
            manifest.RngVersion,
            manifest.CorpusInputFingerprintSha256,
            manifest.PseudonymizationKeySha256,
            manifest.SourceObservationCount,
            manifest.MaterializedObservationCount,
            manifest.ExcludedObservationCount,
            baseline,
            materialized,
            deliveries,
            model,
            SyntheticTruthConsumed: false,
            ModelPromotionAttempted: false);

        var reportPath = Path.Combine(settings.RunDirectory, "synthetic-calibration-dev.json");
        var json = JsonSerializer.Serialize(report, JsonWriteOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(reportPath, json, new UTF8Encoding(false), cancellationToken);
        var reportHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        await File.WriteAllTextAsync(
            reportPath + ".sha256",
            reportHash + "  " + Path.GetFileName(reportPath) + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);

        Console.WriteLine($"SYNTHETIC CALIBRATION DEV: OK report={reportPath}");
        Console.WriteLine(
            $"modelo=v{model.Version} status={model.Status}; materializadas={manifest.MaterializedObservationCount}; " +
            $"excluídas={manifest.ExcludedObservationCount}; truthConsumed=false; promotionAttempted=false");
        return 0;
    }

    private async Task AssertDevelopmentEnvironmentAsync(
        SyntheticCalibrationSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.IngestionEndpoint.IsLoopback)
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV aceita somente endpoint loopback; atual={settings.IngestionEndpoint}.");

        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);

        var profile = await ScalarStringAsync(
            connection,
            $"SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'{EnvironmentProperty}'));",
            cancellationToken);
        if (!string.Equals(profile, RequiredEnvironment, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV exige marcador residente {EnvironmentProperty}={RequiredEnvironment}; " +
                $"atual={profile ?? "(ausente)"}.");
        }

        var solutionSchema = await ScalarStringAsync(
            connection,
            "SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'));",
            cancellationToken);
        if (!string.Equals(solutionSchema, RequiredSolutionSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV exige Jornada.SolutionSchema={RequiredSolutionSchema}; " +
                $"atual={solutionSchema ?? "(ausente)"}.");
        }
    }

    private async Task<SyntheticCalibrationBaseline> ReadBaselineAsync(CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);

        var totalObservations = await ScalarInt64Async(
            connection,
            "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao;",
            cancellationToken);
        var syntheticOrigins = await ScalarInt64Async(
            connection,
            "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SYNTH-%';",
            cancellationToken);
        var syntheticObservations = await ScalarInt64Async(
            connection,
            """
            SELECT COUNT_BIG(*)
            FROM silver.pessoa_observacao o
            JOIN silver.pessoa_origem p ON p.pessoa_origem_id=o.pessoa_origem_id
            WHERE p.codigo_pessoa_origem LIKE N'SYNTH-%';
            """,
            cancellationToken);
        var scaleOrigins = await ScalarInt64Async(
            connection,
            "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';",
            cancellationToken);
        var draftModels = await ScalarInt64Async(
            connection,
            "SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'RASCUNHO';",
            cancellationToken);

        return new SyntheticCalibrationBaseline(
            totalObservations,
            syntheticOrigins,
            syntheticObservations,
            scaleOrigins,
            draftModels);
    }

    private async Task<SyntheticMaterializedCounts> ReadMaterializedCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);

        var origins = await ScalarInt64Async(
            connection,
            "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SYNTH-%';",
            cancellationToken);
        var observations = await ScalarInt64Async(
            connection,
            """
            SELECT COUNT_BIG(*)
            FROM silver.pessoa_observacao o
            JOIN silver.pessoa_origem p ON p.pessoa_origem_id=o.pessoa_origem_id
            WHERE p.codigo_pessoa_origem LIKE N'SYNTH-%';
            """,
            cancellationToken);

        return new SyntheticMaterializedCounts(origins, observations);
    }

    private static async Task RestoreAndBuildAsync(
        SyntheticCalibrationSettings settings,
        CancellationToken cancellationToken)
    {
        await RunProcessAsync(
            settings.SolutionRoot,
            "dotnet",
            ["restore", "Jornada.sln", "--locked-mode"],
            environment: null,
            cancellationToken);

        var projects = new[]
        {
            Path.Combine(
                settings.SolutionRoot,
                "src",
                "Jornada.Linkage.SyntheticCorpus",
                "Jornada.Linkage.SyntheticCorpus.csproj"),
            Path.Combine(
                settings.SolutionRoot,
                "src",
                "Jornada.Api",
                "Jornada.Api.csproj"),
            Path.Combine(
                settings.SolutionRoot,
                "src",
                "Jornada.Processor.Worker",
                "Jornada.Processor.Worker.csproj"),
            Path.Combine(
                settings.SolutionRoot,
                "src",
                "Jornada.Linkage.Parameters.Worker",
                "Jornada.Linkage.Parameters.Worker.csproj")
        };

        foreach (var project in projects)
        {
            await RunProcessAsync(
                settings.SolutionRoot,
                "dotnet",
                ["build", project, "--configuration", "Release", "--no-restore"],
                environment: null,
                cancellationToken);
        }
    }

    private static async Task GeneratePackagesAsync(
        SyntheticCalibrationSettings settings,
        CancellationToken cancellationToken)
    {
        var key = Environment.GetEnvironmentVariable(settings.PseudonymizationKeyEnvironment);
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 16)
        {
            throw new InvalidOperationException(
                $"Defina {settings.PseudonymizationKeyEnvironment} com ao menos 16 bytes UTF-8 antes do ensaio.");
        }

        var project = Path.Combine(
            settings.SolutionRoot,
            "src",
            "Jornada.Linkage.SyntheticCorpus",
            "Jornada.Linkage.SyntheticCorpus.csproj");
        await RunProcessAsync(
            settings.SolutionRoot,
            "dotnet",
            [
                "run",
                "--project", project,
                "--configuration", "Release",
                "--no-build",
                "--",
                "generate-ingestion",
                "--reference-root", settings.ReferenceRoot,
                "--out", settings.GeneratedDirectory,
                "--people", settings.People.ToString(CultureInfo.InvariantCulture),
                "--seed", settings.Seed.ToString(CultureInfo.InvariantCulture),
                "--error-profile", settings.ErrorProfile,
                "--gestores", "4",
                "--pessoa-schema-versao", "4",
                "--data-referencia", settings.DataReferencia.ToString("O", CultureInfo.InvariantCulture),
                "--pseudonymization-key-env", settings.PseudonymizationKeyEnvironment
            ],
            environment: null,
            cancellationToken);
    }

    private static async Task<SyntheticBridgeManifest> ReadBridgeManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("bridge-manifest.json não foi materializado.", path);

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SyntheticBridgeManifest>(
                   stream,
                   JsonReadOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("bridge-manifest.json inválido.");
    }

    private static void ValidateBridgeManifest(
        SyntheticBridgeManifest manifest,
        SyntheticCalibrationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(manifest.BridgeVersion)
            || string.IsNullOrWhiteSpace(manifest.GeneratorVersion)
            || string.IsNullOrWhiteSpace(manifest.RulesetVersion)
            || string.IsNullOrWhiteSpace(manifest.RngVersion)
            || !IsSha256(manifest.CorpusInputFingerprintSha256)
            || !IsSha256(manifest.PseudonymizationKeySha256))
        {
            throw new InvalidDataException("Proveniência incompleta no bridge-manifest.");
        }

        if (manifest.PessoaSchemaVersao != 4)
            throw new InvalidDataException(
                $"bridge-manifest deve materializar Pessoa v4; atual={manifest.PessoaSchemaVersao}.");
        if (manifest.DataReferencia != settings.DataReferencia)
            throw new InvalidDataException(
                $"dataReferencia do bridge diverge da configuração: manifest={manifest.DataReferencia:O}, " +
                $"config={settings.DataReferencia:O}.");

        if (manifest.MaterializedObservationCount <= 0
            || manifest.SourceObservationCount < manifest.MaterializedObservationCount
            || manifest.ExcludedObservationCount
                != manifest.SourceObservationCount - manifest.MaterializedObservationCount)
        {
            throw new InvalidDataException("Contagens inconsistentes no bridge-manifest.");
        }

        if (manifest.Packages is null || manifest.Packages.Length != 4)
            throw new InvalidDataException(
                $"O ensaio DEV exige exatamente quatro pacotes/rotas; encontrados={manifest.Packages?.Length ?? 0}.");

        var expectedRoutes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEHAB"] = "SEHAB",
            ["SMADS"] = "ASSISTENCIA",
            ["SMDET"] = "TRABALHO",
            ["SMS"] = "SAUDE"
        };
        foreach (var package in manifest.Packages)
        {
            if (!expectedRoutes.TryGetValue(package.GestorCodigo, out var expectedSystem)
                || !string.Equals(package.CodigoSistemaOrigem, expectedSystem, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Rota inesperada no bridge-manifest: {package.GestorCodigo}/{package.CodigoSistemaOrigem}.");
            }

            if (package.PeopleCount <= 0 || !IsSha256(package.Sha256))
                throw new InvalidDataException($"Pacote inválido no bridge-manifest: {package.FileName}.");
        }

        if (manifest.Packages.Select(x => x.GestorCodigo).Distinct(StringComparer.Ordinal).Count() != 4)
            throw new InvalidDataException("bridge-manifest contém Gestor duplicado.");

        var packagePeople = manifest.Packages.Sum(x => x.PeopleCount);
        if (packagePeople != manifest.MaterializedObservationCount)
        {
            throw new InvalidDataException(
                $"Soma PeopleCount dos pacotes ({packagePeople}) diverge de " +
                $"materializedObservationCount ({manifest.MaterializedObservationCount}).");
        }
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;

        foreach (var character in value)
        {
            var hex = character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }

        return true;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadDevelopmentCredentialsAsync(
        string path,
        IEnumerable<string> gestores,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<DevelopmentKeyFile>(
                       stream,
                       JsonReadOptions,
                       cancellationToken)
                   ?? throw new InvalidDataException("Arquivo de chaves DEV inválido.");

        if (!string.Equals(file.Environment, RequiredEnvironment, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Arquivo de chaves deve declarar environment={RequiredEnvironment}.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var gestor in gestores)
        {
            var matches = file.Credentials
                .Where(x => string.Equals(x.Type, "GESTOR", StringComparison.Ordinal)
                            && string.Equals(x.GestorCodigo, gestor, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Esperada exatamente uma credencial GESTOR DEV para {gestor}.");

            var credential = matches[0];
            var scopes = credential.Scopes.ToHashSet(StringComparer.Ordinal);
            if (!scopes.Contains("jornada.ingestao.write")
                || !scopes.Contains("jornada.ingestao.status"))
            {
                throw new InvalidDataException(
                    $"Credencial GESTOR DEV de {gestor} não possui scopes de ingestão/status.");
            }

            if (string.IsNullOrWhiteSpace(credential.AccessKey))
                throw new InvalidDataException($"Credencial GESTOR DEV de {gestor} não possui chave.");
            result[gestor] = credential.AccessKey;
        }

        return result;
    }

    private static async Task ValidatePackageHashesAsync(
        SyntheticCalibrationSettings settings,
        SyntheticBridgeManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var package in manifest.Packages)
        {
            var path = PackagePath(settings, package.FileName);
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 do pacote diverge do manifesto: {package.FileName}.");
        }
    }

    private static string PackagePath(SyntheticCalibrationSettings settings, string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            throw new InvalidDataException($"Nome de pacote inseguro no manifesto: {fileName}.");

        var path = Path.Combine(settings.GeneratedDirectory, "ingestion", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Pacote declarado no manifesto não existe: {fileName}.", path);
        return path;
    }

    private static ChildProcess StartRuntimeProcess(
        SyntheticCalibrationSettings settings,
        string label,
        string project,
        bool isApi)
    {
        var startInfo = NewProcessStartInfo(
            settings.SolutionRoot,
            "dotnet",
            isApi
                ?
                [
                    "run",
                    "--project", project,
                    "--configuration", "Release",
                    "--no-build",
                    "--no-launch-profile"
                ]
                :
                [
                    "run",
                    "--project", project,
                    "--configuration", "Release",
                    "--no-build"
                ],
            RuntimeEnvironment(settings));

        if (isApi)
            startInfo.Environment["ASPNETCORE_URLS"] = settings.ApiAuthority;

        return ChildProcess.Start(label, startInfo);
    }

    private static Dictionary<string, string> RuntimeEnvironment(SyntheticCalibrationSettings settings)
        => new(StringComparer.Ordinal)
        {
            ["ConnectionStrings__Jornada"] = settings.ConnectionString,
            ["Database__Provider"] = "SqlServer",
            ["DOTNET_ENVIRONMENT"] = RequiredEnvironment,
            ["ASPNETCORE_ENVIRONMENT"] = RequiredEnvironment,
            ["BronzeStorage__Provider"] = "FileSystem",
            ["BronzeStorage__RootPath"] = settings.RuntimeBronzeDirectory,
            ["IngestionStaging__RootPath"] = settings.RuntimeStagingDirectory,
            ["Processor__PollingMilliseconds"] = "100"
        };

    private static async Task WaitForApiReadyAsync(
        HttpClient http,
        SyntheticCalibrationSettings settings,
        ChildProcess api,
        ChildProcess processor,
        CancellationToken cancellationToken)
    {
        var ready = new Uri(new Uri(settings.ApiAuthority, UriKind.Absolute), "/health/ready");
        var deadline = DateTimeOffset.UtcNow + settings.ApiStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            api.ThrowIfExited();
            processor.ThrowIfExited();
            try
            {
                using var response = await http.GetAsync(ready, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Processo ainda subindo.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException($"API não ficou ready em {settings.ApiStartupTimeout}.");
    }

    private static async Task<Guid> PostPackageAsync(
        HttpClient http,
        SyntheticCalibrationSettings settings,
        SyntheticBridgePackage package,
        string path,
        string accessKey,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = package.FileName
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.IngestionEndpoint)
        {
            Content = content
        };
        request.Headers.Add("X-Jornada-Gestor", package.GestorCodigo);
        request.Headers.Add("X-Jornada-Access-Key", accessKey);
        request.Headers.Add(
            "Idempotency-Key",
            $"synthetic-{package.GestorCodigo.ToLowerInvariant()}-{package.Sha256[..16].ToLowerInvariant()}");

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode != 202)
            throw new InvalidOperationException(
                $"Ingestão de {package.GestorCodigo}/{package.FileName} retornou HTTP {(int)response.StatusCode}: {body}");

        return ReadGuidProperty(body, "entregaId");
    }

    private static async Task<string> WaitForDeliveryAsync(
        HttpClient http,
        SyntheticCalibrationSettings settings,
        string gestor,
        string accessKey,
        Guid deliveryId,
        ChildProcess processor,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(settings.IngestionEndpoint.ToString().TrimEnd('/') + "/" + deliveryId.ToString("D"));
        var deadline = DateTimeOffset.UtcNow + settings.DrainTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            processor.ThrowIfExited();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add("X-Jornada-Gestor", gestor);
            request.Headers.Add("X-Jornada-Access-Key", accessKey);
            using var response = await http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 429)
            {
                await Task.Delay(settings.StatusPollInterval, cancellationToken);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Status da Entrega {deliveryId:D} retornou HTTP {(int)response.StatusCode}: {body}");

            var status = ReadStringProperty(body, "status");
            if (string.Equals(status, "PROCESSADA", StringComparison.Ordinal))
                return status;
            if (string.Equals(status, "REJEITADA", StringComparison.Ordinal)
                || string.Equals(status, "QUARENTENA", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Entrega {deliveryId:D} terminou em {status}.");
            }

            await Task.Delay(settings.StatusPollInterval, cancellationToken);
        }

        throw new TimeoutException($"Timeout aguardando Entrega {deliveryId:D}.");
    }

    private static async Task RunNameFrequencySnapshotLoaderAsync(
        SyntheticCalibrationSettings settings,
        CancellationToken cancellationToken)
    {
        var env = ParameterWorkerEnvironment(settings);
        env["LinkageParameters__Operation"] = "LOAD_NAME_FREQUENCY_SNAPSHOT";
        env["LinkageParameters__RunOnce"] = "true";
        env["NameFrequencySnapshot__ManifestPath"] = Path.Combine(
            settings.ReferenceRoot,
            "manifest.json");

        await RunParametersWorkerAsync(settings, env, cancellationToken);
    }

    private async Task RunGenerateDraftAsync(
        SyntheticCalibrationSettings settings,
        CancellationToken cancellationToken)
    {
        var env = ParameterWorkerEnvironment(settings);
        env["LinkageParameters__Operation"] = "GENERATE_DRAFT";
        env["LinkageParameters__RunOnce"] = "true";

        foreach (var key in ForwardedParameterSettings)
        {
            var value = configuration[$"LinkageParameters:{key}"];
            if (!string.IsNullOrWhiteSpace(value))
                env[$"LinkageParameters__{key}"] = value;
        }

        foreach (var key in new[] { "Seed", "ValidationBasisPoints", "TestBasisPoints" })
        {
            var value = configuration[$"LinkageParameters:DecisionCalibration:{key}"];
            if (!string.IsNullOrWhiteSpace(value))
                env[$"LinkageParameters__DecisionCalibration__{key}"] = value;
        }

        foreach (var key in new[] { "MinimumConditionedPairs", "MinimumConditionedPairsPerPass" })
        {
            var value = configuration[$"LinkageParameters:NominalUConvergence:{key}"];
            if (!string.IsNullOrWhiteSpace(value))
                env[$"LinkageParameters__NominalUConvergence__{key}"] = value;
        }

        await RunParametersWorkerAsync(settings, env, cancellationToken);
    }

    private static Dictionary<string, string> ParameterWorkerEnvironment(SyntheticCalibrationSettings settings)
        => new(StringComparer.Ordinal)
        {
            ["ConnectionStrings__Jornada"] = settings.ConnectionString,
            ["Database__Provider"] = "SqlServer",
            ["DOTNET_ENVIRONMENT"] = RequiredEnvironment
        };

    private static async Task RunParametersWorkerAsync(
        SyntheticCalibrationSettings settings,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var project = Path.Combine(
            settings.SolutionRoot,
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Jornada.Linkage.Parameters.Worker.csproj");
        await RunProcessAsync(
            settings.SolutionRoot,
            "dotnet",
            [
                "run",
                "--project", project,
                "--configuration", "Release",
                "--no-build"
            ],
            environment,
            cancellationToken);
    }

    private async Task<int> ReadMaxModelVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        return checked((int)await ScalarInt64Async(
            connection,
            "SELECT ISNULL(MAX(CONVERT(bigint,versao)),0) FROM identidade.modelo_linkage;",
            cancellationToken));
    }

    private async Task<SyntheticDraftModelEvidence> ReadSingleNewDraftAsync(
        int versionBefore,
        CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT modelo_id,versao,status,algoritmo_versao,normalizacao_versao,base_referencia,
                   amostra_metodo,ISNULL(amostra_pool_tamanho,0),ISNULL(amostra_m_tamanho,0),
                   ISNULL(amostra_u_tamanho,0),registros_lidos,pessoas_unicas
            FROM identidade.modelo_linkage
            WHERE versao>@before
            ORDER BY versao;
            """;
        AddParameter(command, "@before", versionBefore);

        var rows = new List<SyntheticDraftModelEvidence>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SyntheticDraftModelEvidence(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        }

        if (rows.Count != 1)
            throw new InvalidOperationException(
                $"SYNTHETIC_CALIBRATION_DEV exige exatamente um modelo novo; encontrados={rows.Count}.");
        if (!string.Equals(rows[0].Status, "RASCUNHO", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Modelo do ensaio deve permanecer RASCUNHO; atual={rows[0].Status}.");

        return rows[0];
    }

    private static async Task RunProcessAsync(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var startInfo = NewProcessStartInfo(
            workingDirectory,
            fileName,
            arguments,
            environment);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} terminou com exit code {process.ExitCode}: {string.Join(' ', arguments)}");
    }

    private static ProcessStartInfo NewProcessStartInfo(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    private static Guid ReadGuidProperty(string json, string property)
    {
        var value = ReadStringProperty(json, property);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Campo {property} não contém GUID válido.");
    }

    private static string ReadStringProperty(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
                return item.Value.GetString()
                       ?? throw new InvalidDataException($"Campo {property} nulo.");
        }

        throw new InvalidDataException($"Campo {property} ausente.");
    }

    private static async Task<string?> ScalarStringAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<long> ScalarInt64Async(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string FindSolutionRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Jornada.sln")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "SYNTHETIC_CALIBRATION_DEV exige execução a partir de uma árvore fonte contendo Jornada.sln.");
    }

    private sealed record SyntheticCalibrationSettings(
        string SolutionRoot,
        string ConnectionString,
        Uri IngestionEndpoint,
        string ApiAuthority,
        string RunDirectory,
        string GeneratedDirectory,
        string RuntimeBronzeDirectory,
        string RuntimeStagingDirectory,
        string ReferenceRoot,
        string DevelopmentKeysPath,
        string PseudonymizationKeyEnvironment,
        int People,
        ulong Seed,
        string ErrorProfile,
        DateTimeOffset DataReferencia,
        TimeSpan ApiStartupTimeout,
        TimeSpan DrainTimeout,
        TimeSpan StatusPollInterval)
    {
        public static SyntheticCalibrationSettings FromConfiguration(
            IConfiguration configuration,
            EnsaioRuntimeOptions options,
            string solutionRoot)
        {
            var endpoint = new Uri(options.IngestionEndpoint, UriKind.Absolute);
            var runRoot = Path.GetFullPath(
                configuration["Ensaio:SyntheticCalibration:OutputRoot"]
                ?? Path.Combine(options.OutputDirectory, "synthetic-calibration"));
            var people = Math.Max(
                100,
                configuration.GetValue("Ensaio:SyntheticCalibration:People", 20_000));
            var seed = configuration.GetValue<ulong>("Ensaio:SyntheticCalibration:Seed", 42UL);
            var errorProfile = configuration["Ensaio:SyntheticCalibration:ErrorProfile"]?.Trim()
                               ?? "correlated";
            var dataReferenciaRaw = configuration["Ensaio:SyntheticCalibration:DataReferencia"];
            if (!DateTimeOffset.TryParse(
                    dataReferenciaRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dataReferencia))
            {
                throw new InvalidOperationException(
                    "Ensaio:SyntheticCalibration:DataReferencia deve ser ISO-8601 explícita com offset.");
            }

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture);
            var runDirectory = Path.Combine(runRoot, $"{stamp}-seed-{seed}");
            var generated = Path.Combine(runDirectory, "generated");
            var runtime = Path.Combine(runDirectory, "runtime");
            var authority = endpoint.GetLeftPart(UriPartial.Authority);

            return new SyntheticCalibrationSettings(
                solutionRoot,
                options.ConnectionString,
                endpoint,
                authority,
                runDirectory,
                generated,
                Path.Combine(runtime, "bronze"),
                Path.Combine(runtime, "staging"),
                Path.GetFullPath(
                    configuration["Ensaio:SyntheticCalibration:ReferenceRoot"]
                    ?? Path.Combine(solutionRoot, "data", "reference", "ibge-nomes-2022")),
                Path.GetFullPath(
                    configuration["Ensaio:SyntheticCalibration:DevelopmentKeysPath"]
                    ?? Path.Combine(solutionRoot, "config", "security", "test-access-keys.json")),
                configuration["Ensaio:SyntheticCalibration:PseudonymizationKeyEnvironment"]?.Trim()
                    ?? DefaultKeyEnvironment,
                people,
                seed,
                errorProfile,
                dataReferencia,
                TimeSpan.FromSeconds(Math.Max(
                    5,
                    configuration.GetValue("Ensaio:SyntheticCalibration:ApiStartupTimeoutSeconds", 120))),
                TimeSpan.FromMinutes(Math.Max(
                    1,
                    configuration.GetValue("Ensaio:SyntheticCalibration:DrainTimeoutMinutes", 60))),
                TimeSpan.FromSeconds(Math.Max(
                    4,
                    configuration.GetValue("Ensaio:SyntheticCalibration:StatusPollSeconds", 4))));
        }
    }

    private sealed record SyntheticBridgeManifest(
        string BridgeVersion,
        string GeneratorVersion,
        string RulesetVersion,
        string RngVersion,
        int SourceObservationCount,
        int MaterializedObservationCount,
        int ExcludedObservationCount,
        int PessoaSchemaVersao,
        DateTimeOffset DataReferencia,
        string CorpusInputFingerprintSha256,
        string PseudonymizationKeySha256,
        SyntheticBridgePackage[] Packages);

    private sealed record SyntheticBridgePackage(
        string GestorCodigo,
        string CodigoSistemaOrigem,
        string FileName,
        string Sha256,
        int PeopleCount);

    private sealed record DevelopmentKeyFile(
        string Environment,
        DevelopmentCredential[] Credentials);

    private sealed record DevelopmentCredential(
        string Type,
        string GestorCodigo,
        string AccessKey,
        string[] Scopes);

    private sealed record SyntheticDeliveryEvidence(
        string GestorCodigo,
        string CodigoSistemaOrigem,
        string FileName,
        string Sha256,
        int PeopleCount,
        Guid EntregaId,
        string Status);

    private sealed record SyntheticCalibrationBaseline(
        long TotalObservations,
        long SyntheticOrigins,
        long SyntheticObservations,
        long ScaleOrigins,
        long DraftModels);

    private sealed record SyntheticMaterializedCounts(
        long SyntheticOrigins,
        long SyntheticObservations);

    private sealed record SyntheticDraftModelEvidence(
        Guid ModelId,
        int Version,
        string Status,
        string AlgorithmVersion,
        string NormalizationVersion,
        string BaseReference,
        string? SampleMethod,
        int SamplePoolSize,
        int MatchedSampleSize,
        int USampleSize,
        long? RecordsRead,
        long? UniquePeople);

    private sealed record SyntheticCalibrationDevEvidence(
        string ReportVersion,
        string EnvironmentProfile,
        DateTimeOffset GeneratedAtUtc,
        ulong Seed,
        int People,
        string ErrorProfile,
        string BridgeVersion,
        string GeneratorVersion,
        string RulesetVersion,
        string RngVersion,
        string CorpusInputFingerprintSha256,
        string PseudonymizationKeySha256,
        int SourceObservationCount,
        int MaterializedObservationCount,
        int ExcludedObservationCount,
        SyntheticCalibrationBaseline Baseline,
        SyntheticMaterializedCounts Materialized,
        IReadOnlyList<SyntheticDeliveryEvidence> Deliveries,
        SyntheticDraftModelEvidence Model,
        bool SyntheticTruthConsumed,
        bool ModelPromotionAttempted);

    private sealed class ChildProcess : IAsyncDisposable
    {
        private readonly string label;
        private readonly Process process;

        private ChildProcess(string label, Process process)
        {
            this.label = label;
            this.process = process;
        }

        public static ChildProcess Start(string label, ProcessStartInfo startInfo)
        {
            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Não foi possível iniciar {label}.");
            }

            return new ChildProcess(label, process);
        }

        public void ThrowIfExited()
        {
            if (process.HasExited)
                throw new InvalidOperationException($"{label} encerrou prematuramente com exit code {process.ExitCode}.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process.Dispose();
        }
    }
}

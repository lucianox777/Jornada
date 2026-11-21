using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Standalone independent TEST evaluation API: its only caller-supplied
/// argument is the SHA-pinned winner model. TEST owns its independently generated corpus,
/// predeclared protocol and disposable SQL; it reads no TRAIN/VALIDATION
/// plan, candidate set, scores, chosen thresholds, or selection envelope.
/// No fit, winner selection, promotion or activation can run here.
/// </summary>
public static class V9IndependentTestCli
{
    public const string IsolationContract = "V9_INDEPENDENT_DISPOSABLE_SQL_V1";

    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string[] args)
    {
        if ((args.Length != 2 && args.Length != 4)
            || args[0] != "--v9-independent-test"
            || (args.Length == 4 && args[2] != "--test-repetitions"))
            throw new ArgumentException(
                "Uso: --v9-independent-test <sha256.operational.json> [--test-repetitions <inteiro>]. Banco externo e permitido apenas como referencia estatistica opcional do PREPARE.");
        var winnerFile = Path.GetFullPath(args[1]);
        var name = Path.GetFileName(winnerFile);
        if (!name.EndsWith(V9ValidatedOperationalModelFile.Suffix, StringComparison.Ordinal)
            || name.Length != 64 + V9ValidatedOperationalModelFile.Suffix.Length)
            throw new InvalidDataException("TEST exige arquivo operacional final da VALIDATION, endereçado por SHA-256.");
        var winnerSha = name[..64];
        if (!winnerSha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Identidade SHA-256 do vencedor inválida.");
        // TEST's ONLY model input is the exact file name emitted by
        // independent VALIDATION. Validate its full TF4/81 operational JSON
        // schema and SHA before requesting its own sealed corpus.
        var validatedFileWinner = await V9ValidatedOperationalModelFile.ReadPinnedAsync(winnerFile);
        if (validatedFileWinner.ContentSha256 != winnerSha)
            throw new InvalidDataException("TEST recebeu arquivo diferente do vencedor VALIDATION.");
        var testDir = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_CORPUS_DIR");
        var output = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(testDir) || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("Camada TEST exige exclusivamente seu próprio corpus e diretório de resultados.");
        var fullTestDir = Path.GetFullPath(testDir);
        var fullOutput = Path.GetFullPath(output);
        if (Directory.Exists(fullOutput) || File.Exists(fullOutput)
            || string.Equals(fullTestDir, Path.GetDirectoryName(winnerFile), StringComparison.Ordinal)
            || fullOutput.StartsWith(fullTestDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || fullTestDir.StartsWith(fullOutput + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Diretório de resultados TEST deve ser novo, separado da coorte e do modelo.");
        await using var connection = await OpenDisposableSqlAsync();
        var runtime = await LoadExactOperationalRuntimeAsync(connection, validatedFileWinner);
        var winner = runtime.FrozenWinner;
        var operationalModelId = runtime.ModelId;
        var conversion = V9OperationalSqlModelConverter.VerifyConversion(winner);
        var frozenPrefixLength = V9CalibrationBlockingProjection.ResolveFrozenPrefixLength(
            winner.Payload.ProjectionSchemaVersion, winner.Payload.ProjectionFingerprintSha256);
        // A one-off DEV-only physical regression may lift the conservative incidence
        // admission cap, without changing the scientific TEST default or winner.
        var diagnosticCap = V9PhysicalSqlAdaptiveCalibrationProbe.HardPairIncidenceCap;
        var diagnosticCapText = Environment.GetEnvironmentVariable("JORNADA_V9_DIAGNOSTIC_PAIR_INCIDENCE_CAP");
        if (diagnosticCapText is not null)
        {
            if (diagnosticCapText != "1500000"
                || Environment.GetEnvironmentVariable("JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC") != "true"
                || Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE") != "DEV"
                || Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF") is not string workflowRef
                || !workflowRef.Contains("/.github/workflows/v9-one-time-physical-release-preflight.yml@", StringComparison.Ordinal))
                throw new InvalidDataException("Cap diagnóstico exige workflow físico autorizado em DEV e valor exato 1500000.");
            diagnosticCap = 1_500_000;
            Console.WriteLine("V9_PHYSICAL_DIAGNOSTIC_ONLY_PAIR_INCIDENCE_CAP=1500000; SCIENTIFIC_DEFAULT_UNCHANGED");
        }
        // Diagnostic-only repetition override; the scientific protocol remains 20.
        var repetitions = 20;
        var diagnostic = Environment.GetEnvironmentVariable("JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC") == "true";
        var requestedRepetitions = args.Length == 4 ? args[3] : null;
        if (requestedRepetitions is not null)
        {
            if (!diagnostic || !int.TryParse(requestedRepetitions, out repetitions)
                || repetitions is < 1 or > 200)
                throw new InvalidDataException("Alterar repetições exige diagnóstico TEST explícito em DEV.");
            Console.WriteLine($"V9_TEST_REPETITIONS={repetitions}; SCIENTIFIC_PROTOCOL_MINIMUM=20");
        }
        // Only inspect the cohort after validating the exact winner, SQL scope and protocol.
        var corpus = await V9IndependentTestCorpusReader.ReadAsync(fullTestDir);
        var execution = new V9PhysicalAdaptiveExecution(
            MaximumPrefixLength: frozenPrefixLength,
            EndToEndRepetitions: repetitions, TotalComparedConfigurations: 1,
            FsBlindGroupingOnly: true,
            MaximumCandidatePairIncidences: diagnosticCap);
        var evaluated = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync(
            connection, corpus.Observations, winner, runtime, execution,
            maximumAcceptedPerIdentity: 4, CancellationToken.None);
        if (evaluated.Refitted || evaluated.SnapshotSha256 != winnerSha
            || evaluated.Metric.BonferroniComparedConfigurations != 1 || evaluated.TestReplayEvidence is null)
            throw new InvalidDataException("TEST alterou o modelo vencedor ou multiplicidade.");
        var report = new
        {
            schema = "V9_TF4_INDEPENDENT_TEST_ONE_WINNER_PHYSICAL_V1",
            status = "TEST_MEASURED_NOT_CERTIFIED_OR_ACTIVATED",
            winnerSnapshotSha256 = winnerSha,
            validatedOperationalModelFileName = name,
            validatedOperationalJsonSchema = V9FittedSnapshot.SupportSchemaVersion,
            sameOperationalJsonBytesAsProductionSideSqlPayload = true,
            operationalModelContract = V9OperationalSqlModelConverter.ContractVersion,
            operationalModelId,
            operationalModelByteExactRoundTrip = conversion.ByteExactRoundTrip,
            testSqlIsolationContract = IsolationContract,
            tfSlotCount = conversion.TfSlotCount,
            jointCellCount = conversion.JointCellCount,
            blockingPassCount = conversion.BlockingPassCount,
            projectionFingerprintSha256 = conversion.ProjectionFingerprintSha256,
            fitFingerprintSha256 = conversion.FitFingerprintSha256,
            operationalModelActive = false,
            operationalModelPublished = false,
            productionRunnerFullPipelineParityCertified = false,
            testCorpusManifestSha256 = corpus.CorpusManifestSha256,
            testObservationsSha256 = corpus.ObservationsSha256,
            testSeed = corpus.Seed,
            baseIdentities = corpus.BaseIdentities,
            selectedTestBaseIdentities = corpus.SelectedBaseIdentities,
            selectedTestBaseIdentityFraction = (double)corpus.SelectedBaseIdentities / corpus.BaseIdentities,
            evaluatedTestObservations = corpus.Observations.Length,
            configurationsEvaluated = 1,
            testEndToEndRepetitions = repetitions,
            diagnosticNonScientific = diagnostic,
            evaluationProtocol = evaluated.LatencyProtocol,
            p95Milliseconds = evaluated.P95Milliseconds,
            falseDegreeP99 = evaluated.FalseDegreeP99,
            replayEvidence = evaluated.TestReplayEvidence,
            falseNegativeSourceObservations = evaluated.Metric.EligibleSourceObservationsBeforeBlocking
                - evaluated.Metric.TrueAcceptedSourceObservations,
            metric = evaluated.Metric with { IdentityOutcomes = null, SourceRankInternal = null, SourceGapAttribution = null },
            validationResultsRead = false,
            trainPlanRead = false,
            refitted = false,
            testUsed = true,
            certified = false,
            activated = false,
            // Distinct seed by itself is insufficient to certify population IID.
            independentlyCertifiedIid = false,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(report, ReportJsonOptions);
        Directory.CreateDirectory(fullOutput);
        await using var result = new FileStream(Path.Combine(fullOutput, "independent-test.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await result.WriteAsync(json);
        Console.WriteLine(JsonSerializer.Serialize(new {
            status = report.status, winnerSnapshotSha256 = winnerSha, testUsed = true,
            refitted = false, certified = false, activated = false
        }));
    }

    public static async Task PreflightAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--v9-independent-test-preflight")
            throw new ArgumentException("Uso: --v9-independent-test-preflight <sha256.operational.json>.");

        var winner = await V9ValidatedOperationalModelFile.ReadPinnedAsync(args[1]);
        _ = V9CalibrationBlockingProjection.ResolveFrozenPrefixLength(
            winner.Payload.ProjectionSchemaVersion, winner.Payload.ProjectionFingerprintSha256);
        await using var connection = await OpenDisposableSqlAsync();
        var receipt = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        var runtime = await LoadExactOperationalRuntimeAsync(connection, winner);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "V9_TEST_WINNER_READY_BEFORE_COHORT",
            testSqlIsolationContract = IsolationContract,
            winnerSnapshotSha256 = runtime.WinnerSha256,
            operationalModelId = runtime.ModelId,
            projectionFingerprintSha256 = receipt.ProjectionFingerprintSha256,
            tfSlotCount = receipt.TfSlotCount,
            jointCellCount = receipt.JointCellCount,
            byteExactRoundTrip = receipt.ByteExactRoundTrip,
            testUsed = false,
            published = false,
            activated = false,
        }));
    }

    private static async Task<V9OperationalPinnedCohortRuntime> LoadExactOperationalRuntimeAsync(
        SqlConnection connection, V9FittedSnapshot validatedFileWinner)
    {
        var winnerSha = validatedFileWinner.ContentSha256;
        var modelId = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winnerSha);
        var runtime = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(connection, modelId, winnerSha);
        var winner = runtime.FrozenWinner;
        var conversion = V9OperationalSqlModelConverter.VerifyConversion(winner);
        if (!conversion.ByteExactRoundTrip || conversion.ModelId != modelId
            || conversion.SourceSnapshotSha256 != winnerSha
            || !winner.SerializeUtf8().AsSpan().SequenceEqual(validatedFileWinner.SerializeUtf8()))
            throw new InvalidDataException("TEST exige JSON byte-exato: arquivo VALIDATION, SQL operacional e runtime não coincidem.");
        return runtime;
    }

    private static async Task<SqlConnection> OpenDisposableSqlAsync()
    {
        if (Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE") != "DEV")
            throw new InvalidDataException("TEST independente só executa em DEV isolado.");
        var password = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_SQL_PASSWORD");
        var marker = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_SQL_MARKER");
        if (string.IsNullOrWhiteSpace(password) || marker is not { Length: 48 }
            || !marker.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("TEST exige credencial efêmera e marcador de isolamento.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = "127.0.0.1,1433",
            InitialCatalog = "JornadaHarness",
            UserID = "sa",
            Password = password,
            TrustServerCertificate = true,
            Pooling = false,
        };
        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONVERT(nvarchar(48), value)
                FROM sys.extended_properties
                WHERE class = 0 AND name = N'JornadaV9IndependentTest'
                  AND DB_NAME() = N'JornadaHarness'
                  AND SCHEMA_ID(N'gold') IS NULL AND SCHEMA_ID(N'serving') IS NULL;
                """;
            if (!string.Equals(await command.ExecuteScalarAsync() as string, marker, StringComparison.Ordinal))
                throw new InvalidDataException("SQL TEST não corresponde ao banco recém-inicializado e isolado.");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

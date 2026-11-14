using System.Security.Cryptography;
using Jornada.Linkage.Runner;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Standalone independent TEST evaluation API: its only caller-supplied
/// argument is the SHA-pinned winner model. TEST owns its sealed corpus,
/// predeclared protocol and disposable SQL; it reads no TRAIN/VALIDATION
/// plan, candidate set, scores, chosen thresholds, or selection envelope.
/// No fit, winner selection, promotion or activation can run here.
/// </summary>
public static class V9IndependentTestCli
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--v9-independent-test")
            throw new ArgumentException("Uso: --v9-independent-test <sha256.fitted.json> (modelo vencedor único).");
        var winnerFile = Path.GetFullPath(args[1]);
        var name = Path.GetFileName(winnerFile);
        if (!name.EndsWith(".fitted.json", StringComparison.Ordinal)
            || name.Length != 64 + ".fitted.json".Length)
            throw new InvalidDataException("TEST exige nome do modelo vencedor endereçado por SHA-256.");
        var winnerSha = name[..64];
        if (!winnerSha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Identidade SHA-256 do vencedor inválida.");
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
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("SQL próprio da camada TEST não configurado.");
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
            throw new InvalidDataException("TEST físico aceita somente SQL descartável JornadaHarness.");
        // TEST receives just the pinned winner identity. It does NOT load
        // calibration JSON for scoring: it reads the already-converted V9
        // operational model from its own SQL, via the SAME loader as the Runner.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var operationalModelId = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winnerSha);
        // TEST and the production-side Runner use the very same pinned
        // cohort runtime; the legacy operational model is never a fallback.
        var runtime = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, operationalModelId, winnerSha);
        var winner = runtime.FrozenWinner;
        var conversion = V9OperationalSqlModelConverter.VerifyConversion(winner);
        if (!conversion.ByteExactRoundTrip || conversion.ModelId != operationalModelId
            || conversion.SourceSnapshotSha256 != winnerSha)
            throw new InvalidDataException("TEST exige o mesmo modelo vencedor convertido e lido do SQL operacional.");
        // The TEST corpus is opened only AFTER the operational winner passes
        // its SHA/round-trip check. No VALIDATION measurements are read.
        var corpus = await V9IndependentTestCorpusReader.ReadAsync(fullTestDir);
        Directory.CreateDirectory(fullOutput);
        var execution = new V9PhysicalAdaptiveExecution(
            EndToEndRepetitions: 20, TotalComparedConfigurations: 1,
            FsBlindGroupingOnly: true);
        var evaluated = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync(
            connection, corpus.Observations, winner, runtime, execution,
            maximumAcceptedPerIdentity: 4, CancellationToken.None);
        if (evaluated.Refitted || evaluated.SnapshotSha256 != winnerSha
            || evaluated.Metric.BonferroniComparedConfigurations != 1)
            throw new InvalidDataException("TEST alterou o modelo vencedor ou multiplicidade.");
        var report = new
        {
            schema = "V9_TF4_INDEPENDENT_TEST_ONE_WINNER_PHYSICAL_V1",
            status = "TEST_MEASURED_NOT_CERTIFIED_OR_ACTIVATED",
            winnerSnapshotSha256 = winnerSha,
            operationalModelContract = V9OperationalSqlModelConverter.ContractVersion,
            operationalModelId,
            operationalModelByteExactRoundTrip = conversion.ByteExactRoundTrip,
            operationalModelActive = false,
            operationalModelPublished = false,
            productionRunnerFullPipelineParityCertified = false,
            testCorpusManifestSha256 = corpus.CorpusManifestSha256,
            testObservationsSha256 = corpus.ObservationsSha256,
            testSeed = corpus.Seed,
            baseIdentities = corpus.BaseIdentities,
            configurationsEvaluated = 1,
            evaluationProtocol = evaluated.LatencyProtocol,
            p95Milliseconds = evaluated.P95Milliseconds,
            falseDegreeP99 = evaluated.FalseDegreeP99,
            metric = evaluated.Metric with { IdentityOutcomes = null, SourceRankInternal = null },
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
        await using var result = new FileStream(Path.Combine(fullOutput, "independent-test.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await result.WriteAsync(json);
        Console.WriteLine(JsonSerializer.Serialize(new {
            status = report.status, winnerSnapshotSha256 = winnerSha, testUsed = true,
            refitted = false, certified = false, activated = false
        }));
    }
}

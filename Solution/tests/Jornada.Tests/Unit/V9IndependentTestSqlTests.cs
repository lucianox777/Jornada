using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

[TestFixture, NonParallelizable, Category("Unit")]
public sealed class V9IndependentTestSqlTests
{
    [Test]
    public async Task Isolated_worker_consumes_exact_validation_file_and_reports_fixture_without_publication()
    {
        var password = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_SQL_PASSWORD");
        var marker = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_SQL_MARKER");
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(marker))
        {
            Assert.Ignore("O smoke requer SQL efêmero inicializado pelo workflow.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "jornada-v9-test-fixture-" + Guid.NewGuid().ToString("N"));
        var corpus = Path.Combine(root, "synthetic-fixture");
        var output = Path.Combine(root, "report");
        var env = new Dictionary<string, string?>();
        void Set(string name, string? value)
        {
            env.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            var winner = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
            var winnerDir = Path.Combine(root, "winner");
            var exported = await V9ValidatedOperationalModelFile.ExportAsync(winner, winnerDir);
            var file = Path.Combine(winnerDir, exported.FileName);
            var preflight = new[] { "--v9-independent-test-preflight", file };

            Set("JORNADA_RUNTIME_MODE", "PROD");
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() => V9IndependentTestCli.PreflightAsync(preflight)));
            Set("JORNADA_RUNTIME_MODE", "DEV");
            Set("JORNADA_V9_TEST_SQL_MARKER", new string('0', 48));
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() => V9IndependentTestCli.PreflightAsync(preflight)));
            Set("JORNADA_V9_TEST_SQL_MARKER", marker);
            Set("JORNADA_V9_TEST_CORPUS_DIR", corpus);
            Set("JORNADA_V9_TEST_OUTPUT_DIR", output);
            Set("JORNADA_V9_TEST_RESERVED_SEED", "7"); // Handwritten fixture; no ledger or generator.
            await V9IndependentTestCli.PreflightAsync(preflight);
            Assert.That(Directory.Exists(corpus), Is.False);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() => V9IndependentTestCli.PreflightAsync(preflight)));

            await File.AppendAllTextAsync(file, " ");
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() => V9IndependentTestCli.RunAsync(["--v9-independent-test", file])));
            await File.WriteAllBytesAsync(file, winner.SerializeUtf8());

            await WriteFixtureAsync(corpus);
            Set("JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC", "false");
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() => V9IndependentTestCli.RunAsync(
                ["--v9-independent-test", file, "--test-repetitions", "2"])));
            Assert.That(Directory.Exists(output), Is.False);

            Set("JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC", "true");
            await V9IndependentTestCli.RunAsync(["--v9-independent-test", file, "--test-repetitions", "2"]);
            var reportPath = Path.Combine(output, "independent-test.json");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var report = result.RootElement;
            var replay = report.GetProperty("replayEvidence");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.GetProperty("winnerSnapshotSha256").GetString(), Is.EqualTo(winner.ContentSha256));
                Assert.That(report.GetProperty("testSqlIsolationContract").GetString(), Is.EqualTo(V9IndependentTestCli.IsolationContract));
                Assert.That(report.GetProperty("tfSlotCount").GetInt32(), Is.EqualTo(4));
                Assert.That(report.GetProperty("jointCellCount").GetInt32(), Is.EqualTo(81));
                Assert.That(report.GetProperty("evaluatedTestObservations").GetInt32(), Is.EqualTo(4));
                Assert.That(report.GetProperty("selectedTestBaseIdentities").GetInt32(), Is.EqualTo(2));
                Assert.That(report.GetProperty("diagnosticNonScientific").GetBoolean(), Is.True);
                Assert.That(report.GetProperty("productionRunnerFullPipelineParityCertified").GetBoolean(), Is.False);
                Assert.That(report.GetProperty("operationalModelPublished").GetBoolean(), Is.False);
                Assert.That(report.GetProperty("activated").GetBoolean(), Is.False);
                Assert.That(report.GetProperty("metric").TryGetProperty("IdentityOutcomes", out _), Is.False);
                Assert.That(replay.GetProperty("DistinctCandidatePairs").GetInt32(), Is.EqualTo(6));
                Assert.That(replay.GetProperty("ObservationDecisionsSha256").GetString(), Has.Length.EqualTo(64));
            }

            var connection = new SqlConnectionStringBuilder
            {
                DataSource = "127.0.0.1,1433", InitialCatalog = "JornadaHarness", UserID = "sa",
                Password = password, TrustServerCertificate = true,
            };
            var runnerLoader = new SqlProbabilisticIdentityLinkage(null!,
                new OperationalSqlAdapter(connection.ConnectionString), NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
            var runnerRuntime = await runnerLoader.LoadPinnedV9GroupedCohortRuntimeAsync(
                exported.OperationalModelId, winner.ContentSha256, CancellationToken.None);
            Assert.That(runnerRuntime.FrozenWinner.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));

            var evidence = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_SMOKE_REPORT");
            if (!string.IsNullOrWhiteSpace(evidence))
                File.Copy(reportPath, evidence, overwrite: false);
        }
        finally
        {
            foreach (var (name, value) in env)
                Environment.SetEnvironmentVariable(name, value);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteFixtureAsync(string path)
    {
        Directory.CreateDirectory(path);
        var observations = """
            base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf
            P1,TEST,G1,MARIA SILVA,ANA COSTA,1980-05-06,
            P1,TEST,G2,MARIA SILVA,ANA COSTA,1980-05-06,
            P2,TEST,G3,MARIA SILVA,ANA COSTA,1980-05-06,
            P2,TEST,G4,MARIA SILVA,ANA COSTA,1980-05-06,
            P2,TEST,G5,MARIA SILVA,ANA COSTA,1980-05-06,CPF_PRESENT
            P3,VALIDATION,G3,MARIA SILVA,ANA COSTA,1980-05-06,
            P4,TRAIN,G4,MARIA SILVA,ANA COSTA,1980-05-06,
            """;
        var files = new Dictionary<string, string>
        {
            ["observacoes.csv"] = observations,
            ["pessoas_verdade.csv"] = "fixture only",
            ["gabarito.json"] = "{}",
        };
        var outputs = new List<object>();
        foreach (var (file, data) in files)
        {
            var target = Path.Combine(path, file);
            await File.WriteAllTextAsync(target, data);
            outputs.Add(new { path = file, sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(target))) });
        }

        await File.WriteAllTextAsync(Path.Combine(path, "generation-manifest.json"), JsonSerializer.Serialize(new
        {
            schema_version = 1, seed = 7, synthetic_population = 4,
            partition_percentages = new { train = 60, validation = 20, test = 20 },
            population_profile = "DEMOGRAPHIC_PRIMARY_V1", error_profile = "clean",
            reference_code = "CENSO2022_NOMES_BRASIL_V1", outputs,
        }));
    }
}

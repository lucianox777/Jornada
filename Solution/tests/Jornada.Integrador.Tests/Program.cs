internal static class Program
{
    public static async Task<int> Main()
    {
        var failures = new List<string>();

        Check(failures, "recognizes /help", JornadaIntegrator.IsHelpRequest(["/help"]));
        Check(failures, "recognizes --help", JornadaIntegrator.IsHelpRequest(["--help"]));
        Check(failures, "recognizes -h", JornadaIntegrator.IsHelpRequest(["-h"]));
        Check(failures, "empty args are not explicit help", !JornadaIntegrator.IsHelpRequest([]));
        Check(failures, "help with extra args is rejected as explicit help", !JornadaIntegrator.IsHelpRequest(["--help", "extra"]));

        Check(
            failures,
            "accepts exact ZIP filename",
            JornadaIntegrator.ValidateZipFileName("ENTREGA_SEHAB_SEHAB_v2_exemplo.zip") == "ENTREGA_SEHAB_SEHAB_v2_exemplo.zip");

        Check(failures, "rejects SHA-256", ThrowsArgument(() => JornadaIntegrator.ValidateZipFileName(new string('a', 64))));
        Check(failures, "rejects Unix path", ThrowsArgument(() => JornadaIntegrator.ValidateZipFileName("/tmp/entrega.zip")));
        Check(failures, "rejects relative path", ThrowsArgument(() => JornadaIntegrator.ValidateZipFileName("pasta/entrega.zip")));
        Check(failures, "rejects Windows path", ThrowsArgument(() => JornadaIntegrator.ValidateZipFileName("C:\\temp\\entrega.zip")));
        Check(failures, "rejects non-ZIP name", ThrowsArgument(() => JornadaIntegrator.ValidateZipFileName("entrega.json")));

        var config = CreateConfig("https://resultado.example/api/v1/ingestao/resultados/{nomeArquivo}");
        JornadaIntegrator.ValidateConfig(config);
        Check(
            failures,
            "builds result endpoint from ZIP filename",
            JornadaIntegrator.BuildResultEndpoint(config, "ENTREGA TESTE.zip") ==
            "https://resultado.example/api/v1/ingestao/resultados/ENTREGA%20TESTE.zip");

        var legacyConfig = CreateConfig("https://resultado.example/api/v1/ingestao/resultados/{identificador}");
        Check(failures, "rejects legacy identifier placeholder", ThrowsInvalidData(() => JornadaIntegrator.ValidateConfig(legacyConfig)));

        var parsed = JornadaIntegrator.ParseArgs(new[]
        {
            "--resultado",
            "ENTREGA_SEHAB_SEHAB_v2_exemplo.zip",
            "--config",
            "config.json",
            "--saida",
            "resultado.json"
        });
        Check(failures, "parses result command", parsed.Mode == "--resultado");
        Check(failures, "parses ZIP filename", parsed.Value == "ENTREGA_SEHAB_SEHAB_v2_exemplo.zip");
        Check(failures, "parses config path", parsed.ConfigPath == "config.json");
        Check(failures, "parses output path", parsed.OutputPath == "resultado.json");

        var parsedBatch = JornadaIntegrator.ParseArgs(new[]
        {
            "--enviar-todos",
            "--config",
            "config-lote.json"
        });
        Check(failures, "parses batch command", parsedBatch.Mode == "--enviar-todos");
        Check(failures, "batch command has no positional value", parsedBatch.Value == string.Empty);
        Check(failures, "parses batch config path", parsedBatch.ConfigPath == "config-lote.json");
        Check(failures, "rejects --saida in batch mode", ThrowsArgument(() => JornadaIntegrator.ParseArgs(new[] { "--enviar-todos", "--saida", "x.json" })));

        await TestBatchEnumerationAndMoveAsync(failures);
        await TestBatchKeepsFailedZipAsync(failures);
        await TestBatchDoesNotOverwriteArchivedZipAsync(failures);

        if (failures.Count == 0)
        {
            Console.WriteLine("Jornada.Integrador.Tests: OK");
            return 0;
        }

        foreach (var failure in failures)
            Console.Error.WriteLine($"FALHA: {failure}");
        return 1;
    }

    private static async Task TestBatchEnumerationAndMoveAsync(List<string> failures)
    {
        var root = CreateTempDirectory();
        try
        {
            var zipA = Path.Combine(root, "a.zip");
            var zipB = Path.Combine(root, "b.ZIP");
            File.WriteAllText(zipA, "a");
            File.WriteAllText(zipB, "b");
            File.WriteAllText(Path.Combine(root, "ignorar.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Enviados"));
            File.WriteAllText(Path.Combine(root, "Enviados", "antigo.zip"), "old");

            var enumerated = JornadaIntegrator.EnumerateZipFilesForBatch(root)
                .Select(Path.GetFileName)
                .ToArray();
            Check(failures, "enumerates only top-level ZIPs case-insensitively", enumerated.SequenceEqual(new[] { "a.zip", "b.ZIP" }));

            var sent = new List<string>();
            var result = await JornadaIntegrator.SendAllFromDirectoryAsync(
                root,
                path =>
                {
                    sent.Add(Path.GetFileName(path));
                    return Task.CompletedTask;
                });

            Check(failures, "batch reports two successful sends", result == new BatchSendResult(2, 0));
            Check(failures, "batch invokes sender for every ZIP", sent.SequenceEqual(new[] { "a.zip", "b.ZIP" }));
            Check(failures, "moves first successful ZIP to Enviados", File.Exists(Path.Combine(root, "Enviados", "a.zip")) && !File.Exists(zipA));
            Check(failures, "moves second successful ZIP to Enviados", File.Exists(Path.Combine(root, "Enviados", "b.ZIP")) && !File.Exists(zipB));
            Check(failures, "does not reprocess ZIP already in Enviados", File.Exists(Path.Combine(root, "Enviados", "antigo.zip")) && !sent.Contains("antigo.zip"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestBatchKeepsFailedZipAsync(List<string> failures)
    {
        var root = CreateTempDirectory();
        try
        {
            var failZip = Path.Combine(root, "falha.zip");
            var okZip = Path.Combine(root, "ok.zip");
            File.WriteAllText(failZip, "fail");
            File.WriteAllText(okZip, "ok");

            var result = await JornadaIntegrator.SendAllFromDirectoryAsync(
                root,
                path => Path.GetFileName(path) == "falha.zip"
                    ? Task.FromException(new InvalidOperationException("falha simulada"))
                    : Task.CompletedTask);

            Check(failures, "batch continues after one send failure", result == new BatchSendResult(1, 1));
            Check(failures, "failed ZIP remains in source folder", File.Exists(failZip));
            Check(failures, "failed ZIP is not moved to Enviados", !File.Exists(Path.Combine(root, "Enviados", "falha.zip")));
            Check(failures, "successful ZIP is still archived when another fails", File.Exists(Path.Combine(root, "Enviados", "ok.zip")) && !File.Exists(okZip));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestBatchDoesNotOverwriteArchivedZipAsync(List<string> failures)
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "duplicado.zip");
            var sentDirectory = Path.Combine(root, "Enviados");
            Directory.CreateDirectory(sentDirectory);
            File.WriteAllText(source, "novo");
            File.WriteAllText(Path.Combine(sentDirectory, "duplicado.zip"), "antigo");

            var senderCalled = false;
            var result = await JornadaIntegrator.SendAllFromDirectoryAsync(
                root,
                _ =>
                {
                    senderCalled = true;
                    return Task.CompletedTask;
                });

            Check(failures, "archive collision is reported as failure", result == new BatchSendResult(0, 1));
            Check(failures, "archive collision prevents duplicate send", !senderCalled);
            Check(failures, "archive collision preserves source ZIP", File.Exists(source));
            Check(failures, "archive collision preserves existing archived ZIP", File.ReadAllText(Path.Combine(sentDirectory, "duplicado.zip")) == "antigo");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jornada-integrador-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IntegratorConfig CreateConfig(string resultEndpoint) => new()
    {
        Gestor = "SEHAB",
        AccessKey = "test-key",
        Endpoints = new EndpointConfig
        {
            Envio = "https://envio.example/api/v1/ingestao/entregas",
            Resultado = resultEndpoint
        },
        Polling = new PollingConfig
        {
            IntervalSeconds = 1,
            TimeoutSeconds = 10
        },
        DiretorioSaida = "resultados"
    };

    private static bool ThrowsArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool ThrowsInvalidData(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static void Check(List<string> failures, string name, bool condition)
    {
        if (!condition) failures.Add(name);
    }
}

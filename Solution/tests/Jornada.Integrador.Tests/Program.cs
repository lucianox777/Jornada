internal static class Program
{
    public static int Main()
    {
        var failures = new List<string>();

        Check(
            failures,
            "accepts exact ZIP filename",
            JornadaIntegrator.ValidateZipFileName("ENTREGA_SEHAB_HabitaSampa_v2_exemplo.zip") == "ENTREGA_SEHAB_HabitaSampa_v2_exemplo.zip");

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
            "ENTREGA_SEHAB_HabitaSampa_v2_exemplo.zip",
            "--config",
            "config.json",
            "--saida",
            "resultado.json"
        });
        Check(failures, "parses result command", parsed.Mode == "--resultado");
        Check(failures, "parses ZIP filename", parsed.Value == "ENTREGA_SEHAB_HabitaSampa_v2_exemplo.zip");
        Check(failures, "parses config path", parsed.ConfigPath == "config.json");
        Check(failures, "parses output path", parsed.OutputPath == "resultado.json");

        if (failures.Count == 0)
        {
            Console.WriteLine("Jornada.Integrador.Tests: OK");
            return 0;
        }

        foreach (var failure in failures)
            Console.Error.WriteLine($"FALHA: {failure}");
        return 1;
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

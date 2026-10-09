using System.Text.Json;

// C3.3d: append-only audit of PRIVATE worker lifecycle commands. This is
// neither a PROD audit store nor a substitute for SQL/OS observability.
// NEVER write arbitrary HTTP paths, secrets, person fields or log details.
sealed class IsolatedWorkerAuditJournal(
    IWebHostEnvironment environment,
    IsolatedWorkerSupervisorStatusReader reader,
    ConsoleRuntimeMode runtime)
{
    private readonly object gate = new();
    private static readonly HashSet<string> AllowedWorkers =
        new(["processor", "operations-maintenance", "bronze-maintenance"],
            StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedOutcomes =
        new(["ADMITIDO", "SUCESSO", "ERRO"], StringComparer.Ordinal);

    public void Record(string category, string operation, string outcome)
    {
        if (!reader.Enabled(runtime))
            throw new InvalidOperationException("Auditoria de workers disponível somente no CI DEV privado.");
        if (!AllowedOutcomes.Contains(outcome))
            throw new ArgumentException("Resultado de auditoria inválido.", nameof(outcome));
        if (category is "SUPERVISAO")
        {
            if (operation is not ("ON" or "OFF"))
                throw new ArgumentException("Modo de auditoria inválido.", nameof(operation));
        }
        else if (category is "RUN_ONCE" or "PARAR_PROCESSO")
        {
            if (!AllowedWorkers.Contains(operation))
                throw new ArgumentException("Worker de auditoria não permitido.", nameof(operation));
        }
        else
        {
            throw new ArgumentException("Evento de auditoria não autorizado.", nameof(category));
        }

        var run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID")!;
        var attempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")!;
        var solutionRoot = DevConsolePaths.FindSolutionRoot(environment.ContentRootPath);
        // Fixed CI-only path, NEVER provided by the HTTP client or config.
        var folder = Path.Combine(solutionRoot, ".local", "e2e",
            "c3-3d-private-worker-audit");
        var file = Path.Combine(folder, "events.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            project = $"jornada-workers-e2e-ci{run}{attempt}",
            category,
            operation,
            outcome
        });
        lock (gate)
        {
            Directory.CreateDirectory(folder);
            // Flush each event to disk before continuing a Docker mutation.
            // CI jobs archive .local/e2e; a DevConsole restart must not clear it.
            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.WriteThrough);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }
}

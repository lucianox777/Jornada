using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Opt-in experimental entry point. Never starts the operational host.</summary>
public static class V9AdaptiveCalibrationStudyCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--v9-adaptive-study")
            throw new ArgumentException("Uso: --v9-adaptive-study <plano.json> <observacoes.csv> <novo-diretorio>");
        var planPath = Path.GetFullPath(args[1]);
        var inputPath = Path.GetFullPath(args[2]);
        var outputPath = Path.GetFullPath(args[3]);
        if (Directory.Exists(outputPath) || File.Exists(outputPath))
            throw new InvalidOperationException("Estudo exige um diretório novo; resultados anteriores são imutáveis.");
        if (new FileInfo(planPath).Length is <= 0 or > 10_000_000)
            throw new InvalidDataException("Plano ausente, vazio ou grande demais.");
        var plan = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(await File.ReadAllBytesAsync(planPath), Json)
            ?? throw new InvalidDataException("Plano V9 ausente.");
        V9AdaptiveCalibrationCoordinator.ValidatePlan(plan);
        // The programmatic coordinator accepts a pinned TF provider per ruleset.
        // This file-only entry point must never silently replace positive TF with zero.
        if (plan.ModelOptions.Any(x => x.TfWeight > 0m))
            throw new InvalidDataException("CLI requer TF=0; TF positiva usa RunAsync com referência IBGE fixada por frame.");
        // This shared entry point accepts the same >=4-row frame as the physical
        // coordinator. The historical seed42 CLI retains its 1000-row default.
        // Statistical eligibility is enforced downstream, never inferred from this floor.
        var observations = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(inputPath,
            minimumObservationCount: 4);
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão de JornadaHarness descartável ausente.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException("Estudo físico aceita exclusivamente o banco descartável JornadaHarness.");

        Directory.CreateDirectory(outputPath);
        // CreateNew is also the concurrency guard for two invocations targeting the same directory.
        await using var ownership = new FileStream(Path.Combine(outputPath, "study.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await WriteNewJsonAsync(Path.Combine(outputPath, "plan.json"), plan);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var result = await V9AdaptiveCalibrationCoordinator.RunAsync(connection,
                observations, plan, CancellationToken.None);
            foreach (var configuration in result.FrozenConfigurations)
            {
                var path = Path.Combine(outputPath, configuration.Snapshot.ContentSha256 + ".fitted.json");
                // Each configuration has a unique content hash; refuse any divergent duplicate.
                if (File.Exists(path))
                    _ = await V9FittedSnapshotArtifact.ReadPinnedAsync(path, configuration.Snapshot.ContentSha256);
                else await V9FittedSnapshotArtifact.WriteNewAsync(path, configuration.Snapshot);
            }
            await WriteNewJsonAsync(Path.Combine(outputPath, "study.json"), result);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                output = outputPath, result.PlanSha256,
                configurations = result.Pareto.Table.Count,
                result.Pareto.Status, result.Pareto.InitialSuggestedOptionId,
                testUsed = false, published = false, activated = false
            }));
        }
        catch (Exception exception)
        {
            // Retain the plan and an interrupted marker; never erase or reroll a failed study.
            // Do not write exception messages, which could contain connection credentials.
            await WriteNewJsonAsync(Path.Combine(outputPath, "interrupted.json"), new
            {
                exceptionType = exception.GetType().Name, completed = false,
                testUsed = false, published = false, activated = false
            });
            throw;
        }
    }

    private static async Task WriteNewJsonAsync<T>(string path, T value)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, Json);
    }
}

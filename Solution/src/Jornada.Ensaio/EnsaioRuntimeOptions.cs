using Microsoft.Extensions.Configuration;

namespace Jornada.Ensaio;

public sealed record EnsaioRuntimeOptions(
    string ConnectionString,
    string PackagesDirectory,
    string OutputDirectory,
    string IngestionEndpoint,
    string ApiKey,
    string BaselineSha,
    string? SingleStage,
    TimeSpan DrainTimeout)
{
    public static EnsaioRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Jornada")
            ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");

        return new EnsaioRuntimeOptions(
            connectionString,
            configuration["Ensaio:PacotesDir"] ?? AppContext.BaseDirectory,
            configuration["Ensaio:SaidaDir"] ?? Path.Combine(AppContext.BaseDirectory, "ensaio"),
            configuration["Ensaio:Endpoints:IngestaoEntregas"] ?? "http://localhost:8080/api/v1/ingestao/entregas",
            configuration["Ensaio:ApiKey"] ?? string.Empty,
            configuration["Ensaio:BaselineSha"] ?? "(não informado)",
            configuration["Ensaio:Etapa"],
            TimeSpan.FromMinutes(configuration.GetValue("Ensaio:DrenagemTimeoutMinutos", 120)));
    }
}

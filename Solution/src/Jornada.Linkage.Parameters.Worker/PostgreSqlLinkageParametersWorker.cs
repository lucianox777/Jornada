using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Executor PostgreSQL isolado. Não registra scorer operacional nem oferece ativação automática.</summary>
public sealed class PostgreSqlLinkageParametersWorker(
    ILogger<PostgreSqlLinkageParametersWorker> logger,
    IConfiguration configuration,
    IOperationalDatabaseAdapter database,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var operation = configuration.GetValue("LinkageParameters:Operation", "GENERATE_DRAFT")!
            .Trim().ToUpperInvariant();
        var calibrator = new PostgreSqlLinkageCalibrator(database);
        try
        {
            if (operation == "VALIDATE")
            {
                var version = configuration.GetValue<int?>("LinkageParameters:TargetVersion")
                    ?? throw new InvalidOperationException("TargetVersion é obrigatório para VALIDATE.");
                var modelId = await calibrator.ValidateDraftAsync(version, stoppingToken);
                logger.LogInformation("Calibração PostgreSQL validada. Modelo={ModelId}; versão={Version}.",modelId,version);
            }
            else if (operation == "GENERATE_DRAFT")
            {
                var options = PostgreSqlCalibrationOptions.FromConfiguration(configuration);
                do
                {
                    var draft = await calibrator.GenerateDraftAsync(options, stoppingToken);
                    logger.LogInformation("Rascunho PostgreSQL gerado. Modelo={ModelId}; versão={Version}; população={Population}; m={M}; u={U}.",
                        draft.ModelId,draft.Version,draft.Population,draft.MatchedPairs,draft.UnmatchedPairs);
                    if (configuration.GetValue("LinkageParameters:RunOnce",true)) break;
                    var minutes = Math.Max(1,configuration.GetValue("LinkageParameters:RefreshMinutes",10_080));
                    await Task.Delay(TimeSpan.FromMinutes(minutes),stoppingToken);
                } while (!stoppingToken.IsCancellationRequested);
            }
            else
            {
                throw new InvalidOperationException("Operação PostgreSQL não autorizada. Use GENERATE_DRAFT ou VALIDATE; ACTIVATE exige uma etapa governada separada.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento solicitado pelo host.
        }
        catch (Exception ex)
        {
            // O log não inclui nomes, CPF, SQL parametrizado nem o texto da exceção.
            logger.LogError("Falha na calibração PostgreSQL. Operação={Operation}; tipo={ErrorType}.",operation,ex.GetType().Name);
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}

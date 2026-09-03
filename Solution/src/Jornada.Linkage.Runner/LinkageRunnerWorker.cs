using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public sealed class LinkageRunnerWorker(
    LinkageRunOptions options,
    IConfiguration configuration,
    IProbabilisticLinkageBatchRunner runner,
    IHostApplicationLifetime lifetime,
    ILogger<LinkageRunnerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runSw = Stopwatch.StartNew();
        try
        {
            if (await IsInitialLoadModeActiveAsync(configuration, stoppingToken))
            {
                logger.LogWarning("Runner não iniciado porque controle.modo_carga_inicial está ativo. Processor deve drenar a primeira carga antes do linkage.");
                return;
            }

            var summary = await runner.RunAsync(new ProbabilisticLinkageRunRequest(
                options.Mode,
                options.ModelVersion,
                options.PessoaObservacaoId,
                options.GestorCodigo,
                options.Since,
                options.BatchSize,
                options.MaxParallelism,
                options.MaxRecords,
                options.RequestedBy,
                options.Reason,
                options.CorrelationId,
                options.Publish), stoppingToken);

            logger.LogInformation(
                "Runner concluído. RunId={RunId}; Modelo={ModeloVersao}; Status={Status}; Avaliados={Avaliados}",
                summary.RunId, summary.ModeloVersao, summary.Status, summary.Avaliados);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Execução do linkage cancelada pelo host.");
            Environment.ExitCode = 2;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no Jornada.Linkage.Runner.");
            Environment.ExitCode = 1;
        }
        finally
        {
            runSw.Stop();
            JornadaTelemetry.RecordLinkageRun(runSw.Elapsed.TotalMilliseconds, options.Mode.ToString());
            lifetime.StopApplication();
        }
    }
    private static async Task<bool> IsInitialLoadModeActiveAsync(IConfiguration configuration, CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("Jornada")
            ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ativo FROM controle.modo_carga_inicial WHERE estado_id=1;";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

}

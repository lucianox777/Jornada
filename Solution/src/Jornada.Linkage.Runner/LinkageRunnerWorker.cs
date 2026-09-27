using System.Diagnostics;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public sealed class LinkageRunnerWorker(
    LinkageRunOptions options,
    IProbabilisticLinkageBatchRunner runner,
    IHostApplicationLifetime lifetime,
    ILogger<LinkageRunnerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runSw = Stopwatch.StartNew();
        try
        {
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
}

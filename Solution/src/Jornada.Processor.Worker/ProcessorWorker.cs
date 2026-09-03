namespace Jornada.Processor.Worker;

internal sealed class ProcessorWorker(
    IngestionProcessor processor,
    SqlProcessorRepository repository,
    ProcessorOptions options,
    ILogger<ProcessorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Jornada Processor iniciado com lease/heartbeat e retry controlado.");

        await repository.RecoverExpiredLeasesAsync(options.MaxProcessingAttempts, stoppingToken);
        var recoveryLoop = RecoveryLoopAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, options.PollingMilliseconds)), stoppingToken);
            }
        }
        finally
        {
            try { await recoveryLoop; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task RecoveryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.RecoveryScanSeconds)));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var recovered = await repository.RecoverExpiredLeasesAsync(options.MaxProcessingAttempts, ct);
                if (recovered > 0)
                    logger.LogWarning("Processor recuperou {Count} lote(s) com lease expirado.", recovered);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao executar varredura de leases expirados.");
            }
        }
    }
}

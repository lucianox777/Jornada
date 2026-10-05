using System.Diagnostics;
using Jornada.Contracts;

namespace Jornada.Processor.Worker;

internal sealed class ProcessorWorker(
    IngestionProcessor processor,
    IProcessorRepository repository,
    ProcessorOptions options,
    ILogger<ProcessorWorker> logger) : BackgroundService
{
    private const int MaxLoopFailureBackoffMilliseconds = 30_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Jornada Processor iniciado com lease/heartbeat e retry controlado.");

        Task? recoveryLoop = null;
        var initialRecoveryCompleted = false;
        var consecutiveFailures = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var cycleSw = Stopwatch.StartNew();
                try
                {
                    if (!initialRecoveryCompleted)
                    {
                        var recovered = await repository.RecoverExpiredLeasesAsync(
                            options.MaxProcessingAttempts, stoppingToken);
                        initialRecoveryCompleted = true;
                        recoveryLoop = RecoveryLoopAsync(stoppingToken);
                        if (recovered > 0)
                            logger.LogWarning(
                                "Processor recuperou {Count} lote(s) com lease expirado na inicialização.",
                                recovered);
                    }

                    var processed = await processor.ProcessNextAsync(stoppingToken);
                    consecutiveFailures = 0;
                    if (!processed)
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, options.PollingMilliseconds)), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    cycleSw.Stop();
                    JornadaTelemetry.RecordProcessorLoopCycle(
                        cycleSw.Elapsed.TotalMilliseconds, "LOOP_FAILURE");
                    var backoff = CalculateLoopFailureBackoff(
                        consecutiveFailures, options.PollingMilliseconds);
                    logger.LogError(
                        ex,
                        "Falha inesperada no loop operacional do Processor. FalhasConsecutivas={FailureCount}; BackoffMs={BackoffMilliseconds}.",
                        consecutiveFailures,
                        (long)backoff.TotalMilliseconds);
                    await Task.Delay(backoff, stoppingToken);
                }
            }
        }
        finally
        {
            if (recoveryLoop is not null)
            {
                try { await recoveryLoop; }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            }
        }
    }

    internal static TimeSpan CalculateLoopFailureBackoff(
        int consecutiveFailures,
        int pollingMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);
        var baseMilliseconds = Math.Max(250, pollingMilliseconds);
        var exponent = Math.Min(consecutiveFailures - 1, 6);
        var scaled = checked((long)baseMilliseconds << exponent);
        return TimeSpan.FromMilliseconds(Math.Min(MaxLoopFailureBackoffMilliseconds, scaled));
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

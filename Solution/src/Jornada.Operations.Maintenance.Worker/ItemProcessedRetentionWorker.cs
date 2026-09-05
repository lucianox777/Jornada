using Jornada.Operational.Sql;
using Microsoft.Extensions.Options;

namespace Jornada.Operations.Maintenance.Worker;

public sealed record ItemProcessedRetentionOptions
{
    // Fase 1: permanece false até a governança aprovar formalmente a janela granular.
    public bool Enabled { get; init; }
    public int DetailRetentionDays { get; init; }
    public int MaxRowsPerCycle { get; init; } = 100_000;
    public int IntervalMinutes { get; init; } = 60;
}

public sealed class ItemProcessedRetentionWorker(
    IOperationalSqlAdapter operationalSql,
    IOptions<ItemProcessedRetentionOptions> options,
    ILogger<ItemProcessedRetentionWorker> logger) : BackgroundService
{

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cfg = options.Value;
            if (cfg.Enabled)
            {
                try
                {
                    await RunCycleAsync(cfg, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha na consolidação/retenção de ingestao.item_processado; detalhe não será removido sem consolidação confirmada.");
                }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, cfg.IntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    internal async Task RunCycleAsync(ItemProcessedRetentionOptions cfg, CancellationToken ct)
    {
        if (cfg.DetailRetentionDays <= 0)
            throw new InvalidOperationException("ItemProcessedRetention:DetailRetentionDays deve ser > 0 quando a retenção estiver habilitada.");
        var cutoff = DateTimeOffset.UtcNow.AddDays(-cfg.DetailRetentionDays);
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "ingestao.sp_consolidar_expurgar_item_processado";
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.CommandTimeout = 900;
        command.Parameters.AddWithValue("@cutoff", cutoff);
        command.Parameters.AddWithValue("@max_rows", Math.Max(1, cfg.MaxRowsPerCycle));
        var affected = await command.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Retenção item_processado executada. Cutoff={Cutoff:O}; MaxRows={MaxRows}; retorno={Affected}.", cutoff, cfg.MaxRowsPerCycle, affected);
    }
}

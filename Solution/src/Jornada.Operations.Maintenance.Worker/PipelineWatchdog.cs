using Microsoft.Data.SqlClient;
using Jornada.Contracts;
using Microsoft.Extensions.Options;

namespace Jornada.Operations.Maintenance.Worker;

/// <summary>
/// Observa sinais de execução estagnada do pipeline. Não agenda workers, não altera estado,
/// não mata sessões e não libera locks. O scheduler permanece responsabilidade da infraestrutura.
/// </summary>
public sealed record PipelineWatchdogOptions
{
    public bool Enabled { get; init; }
    public int IntervalMinutes { get; init; } = 5;
    public int LinkageRunMaxMinutes { get; init; } = 120;
    public int ModelGenerationMaxMinutes { get; init; } = 120;
    public int ExpiredLeaseGraceMinutes { get; init; } = 5;
    public int PendingBacklogMaxAgeMinutes { get; init; } = 60;
    public int InitialLoadMaxHours { get; init; } = 24;
}

public sealed record PipelineWatchdogSnapshot(
    DateTimeOffset ObservedAt,
    long ActiveLinkageRuns,
    DateTimeOffset? OldestActiveLinkageRunStartedAt,
    long GeneratingModels,
    DateTimeOffset? OldestGeneratingModelStartedAt,
    long ExpiredActiveLeases,
    DateTimeOffset? OldestExpiredLeaseAt,
    long PendingLots,
    DateTimeOffset? OldestPendingLotCreatedAt,
    bool InitialLoadActive,
    DateTimeOffset? InitialLoadActivatedAt);

public sealed record PipelineWatchdogFinding(
    string Code,
    long Count,
    double AgeMinutes,
    string Detail);

public static class PipelineWatchdogEvaluator
{
    public static IReadOnlyList<PipelineWatchdogFinding> Evaluate(
        PipelineWatchdogSnapshot snapshot,
        PipelineWatchdogOptions options)
    {
        var findings = new List<PipelineWatchdogFinding>();

        AddAgeFinding(
            findings,
            snapshot.ObservedAt,
            snapshot.ActiveLinkageRuns,
            snapshot.OldestActiveLinkageRunStartedAt,
            TimeSpan.FromMinutes(options.LinkageRunMaxMinutes),
            "LINKAGE_RUN_STALE",
            "Há linkage_run em PREPARANDO/EXECUTANDO além do limite operacional.");

        AddAgeFinding(
            findings,
            snapshot.ObservedAt,
            snapshot.GeneratingModels,
            snapshot.OldestGeneratingModelStartedAt,
            TimeSpan.FromMinutes(options.ModelGenerationMaxMinutes),
            "LINKAGE_MODEL_GENERATION_STALE",
            "Há modelo_linkage em GERANDO além do limite operacional.");

        AddAgeFinding(
            findings,
            snapshot.ObservedAt,
            snapshot.ExpiredActiveLeases,
            snapshot.OldestExpiredLeaseAt,
            TimeSpan.FromMinutes(options.ExpiredLeaseGraceMinutes),
            "PROCESSOR_LEASE_EXPIRED",
            "Há lote em VALIDANDO/PROCESSANDO com lease expirado além da tolerância.");

        AddAgeFinding(
            findings,
            snapshot.ObservedAt,
            snapshot.PendingLots,
            snapshot.OldestPendingLotCreatedAt,
            TimeSpan.FromMinutes(options.PendingBacklogMaxAgeMinutes),
            "PROCESSOR_BACKLOG_OLD",
            "O lote PENDENTE mais antigo excede a idade operacional configurada.");

        if (snapshot.InitialLoadActive && snapshot.InitialLoadActivatedAt is { } initialLoadAt)
        {
            var age = snapshot.ObservedAt - initialLoadAt;
            if (age > TimeSpan.FromHours(options.InitialLoadMaxHours))
            {
                findings.Add(new PipelineWatchdogFinding(
                    "INITIAL_LOAD_MODE_STALE",
                    1,
                    Math.Max(0, age.TotalMinutes),
                    "controle.modo_carga_inicial permanece ativo além do limite operacional."));
            }
        }

        return findings;
    }

    private static void AddAgeFinding(
        ICollection<PipelineWatchdogFinding> findings,
        DateTimeOffset now,
        long count,
        DateTimeOffset? oldestAt,
        TimeSpan threshold,
        string code,
        string detail)
    {
        if (count <= 0 || oldestAt is null) return;
        var age = now - oldestAt.Value;
        if (age <= threshold) return;

        findings.Add(new PipelineWatchdogFinding(
            code,
            count,
            Math.Max(0, age.TotalMinutes),
            detail));
    }
}

public sealed class PipelineWatchdogWorker(
    IConfiguration configuration,
    IOptions<PipelineWatchdogOptions> options,
    ILogger<PipelineWatchdogWorker> logger) : BackgroundService
{
    private readonly string connectionString = configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");

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
                    logger.LogError(ex, "PIPELINE_WATCHDOG_PROBE_FAILED: falha ao observar o estado do pipeline; nenhuma ação corretiva foi executada.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, cfg.IntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    internal async Task<IReadOnlyList<PipelineWatchdogFinding>> RunCycleAsync(
        PipelineWatchdogOptions cfg,
        CancellationToken ct)
    {
        var snapshot = await ReadSnapshotAsync(ct);
        await RecordIdentityPendingAgeMetricsAsync(ct);
        var findings = PipelineWatchdogEvaluator.Evaluate(snapshot, cfg);

        foreach (var finding in findings)
        {
            logger.LogWarning(
                "PIPELINE_WATCHDOG {Code}; Count={Count}; AgeMinutes={AgeMinutes:F1}; Detail={Detail}; ObservedAt={ObservedAt:O}. " +
                "O watchdog é somente observacional: não agenda, não cancela e não altera estado.",
                finding.Code,
                finding.Count,
                finding.AgeMinutes,
                finding.Detail,
                snapshot.ObservedAt);
        }

        if (findings.Count == 0)
        {
            logger.LogDebug(
                "PIPELINE_WATCHDOG_OK ObservedAt={ObservedAt:O}; PendingLots={PendingLots}; ActiveLinkageRuns={ActiveLinkageRuns}; GeneratingModels={GeneratingModels}; ExpiredActiveLeases={ExpiredActiveLeases}; InitialLoadActive={InitialLoadActive}.",
                snapshot.ObservedAt,
                snapshot.PendingLots,
                snapshot.ActiveLinkageRuns,
                snapshot.GeneratingModels,
                snapshot.ExpiredActiveLeases,
                snapshot.InitialLoadActive);
        }

        return findings;
    }

    private async Task RecordIdentityPendingAgeMetricsAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT vf.status,
                   DATEDIFF_BIG(MINUTE,MIN(po.source_as_of),SYSUTCDATETIME()) AS idade_minutos
            FROM identidade.v_vinculo_corrente vf
            JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id
            WHERE vf.status IN('NAO_RESOLVIDO','CONFLITO')
            GROUP BY vf.status;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            JornadaTelemetry.RecordIdentityPendingAge(Math.Max(0, reader.GetInt64(1)), reader.GetString(0));
    }

    private async Task<PipelineWatchdogSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();

            SELECT
                @agora AS observado_em,
                (SELECT COUNT_BIG(*) FROM identidade.linkage_run WHERE status IN('PREPARANDO','EXECUTANDO')) AS linkage_runs_ativos,
                (SELECT MIN(iniciado_em) FROM identidade.linkage_run WHERE status IN('PREPARANDO','EXECUTANDO')) AS linkage_run_mais_antigo,
                (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status='GERANDO') AS modelos_gerando,
                (SELECT MIN(gerado_em) FROM identidade.modelo_linkage WHERE status='GERANDO') AS modelo_gerando_mais_antigo,
                (SELECT COUNT_BIG(*) FROM ingestao.lote WHERE status IN('VALIDANDO','PROCESSANDO') AND lease_expira_em<@agora) AS leases_expirados,
                (SELECT MIN(lease_expira_em) FROM ingestao.lote WHERE status IN('VALIDANDO','PROCESSANDO') AND lease_expira_em<@agora) AS lease_expirado_mais_antigo,
                (SELECT COUNT_BIG(*) FROM ingestao.lote WHERE status='PENDENTE') AS lotes_pendentes,
                (SELECT MIN(criado_em) FROM ingestao.lote WHERE status='PENDENTE') AS lote_pendente_mais_antigo,
                CAST(COALESCE((SELECT TOP(1) ativo FROM controle.modo_carga_inicial WHERE estado_id=1),0) AS bit) AS carga_inicial_ativa,
                (SELECT TOP(1) ativado_em FROM controle.modo_carga_inicial WHERE estado_id=1) AS carga_inicial_ativada_em;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Consulta do watchdog não retornou snapshot.");

        return new PipelineWatchdogSnapshot(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetInt64(1),
            GetNullableDateTimeOffset(reader, 2),
            reader.GetInt64(3),
            GetNullableDateTimeOffset(reader, 4),
            reader.GetInt64(5),
            GetNullableDateTimeOffset(reader, 6),
            reader.GetInt64(7),
            GetNullableDateTimeOffset(reader, 8),
            reader.GetBoolean(9),
            GetNullableDateTimeOffset(reader, 10));
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

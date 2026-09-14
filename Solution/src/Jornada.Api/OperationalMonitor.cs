using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal sealed record RuntimeComponentStatus(
    string NodeId,
    string Component,
    string MachineName,
    string Status,
    bool Online,
    long HeartbeatAgeSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    DateTimeOffset? StoppedAt,
    string? Version,
    string? ConfigurationBundleVersion,
    string? SolutionSchemaExpected);

internal sealed record QueueStatus(string Status, long Count);

internal sealed record ActiveProcessingStatus(
    Guid LotId,
    Guid DeliveryId,
    int LotSequence,
    int LotTotal,
    int People,
    int Records,
    string Status,
    string? LeaseOwner,
    DateTimeOffset? LeaseAcquiredAt,
    DateTimeOffset? HeartbeatAt);

internal sealed record RecentDeliveryStatus(
    Guid DeliveryId,
    string Status,
    string Manager,
    string SourceSystem,
    string FileName,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt);

internal sealed record BronzeMaintenanceStatus(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ObjectsExamined,
    int OrphansRemoved,
    int TemporariesRemoved,
    int LocksNotAcquired,
    int StorageFailures);

internal sealed record LinkageRunStatus(
    Guid RunId,
    string RunType,
    string Status,
    int ModelVersion,
    long Eligible,
    long Evaluated,
    long Resolved,
    long NotResolved,
    long Conflicts,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

internal sealed record ConfigurationBundleHealthStatus(
    string Status,
    string? ExpectedBundleVersion,
    string? ExpectedSolutionSchema,
    string? DatabaseSolutionSchema,
    IReadOnlyList<string> OnlineBundleVersions,
    IReadOnlyList<string> OnlineExpectedSchemas,
    string Detail);

internal sealed record OperationalMonitorSnapshot(
    DateTimeOffset GeneratedAt,
    string OverallStatus,
    ConfigurationBundleHealthStatus ConfigurationHealth,
    IReadOnlyList<RuntimeComponentStatus> Components,
    IReadOnlyList<QueueStatus> Queue,
    IReadOnlyList<ActiveProcessingStatus> Processing,
    IReadOnlyList<RecentDeliveryStatus> RecentDeliveries,
    BronzeMaintenanceStatus? BronzeMaintenance,
    IReadOnlyList<LinkageRunStatus> LinkageRuns);

internal sealed class OperationalMonitorService(IOperationalSqlAdapter connections)
{
    public async Task<OperationalMonitorSnapshot> GetAsync(CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = new SqlCommand("""
            DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();

            SELECT node_id,componente,machine_name,status,
                   CASE WHEN status=N'RUNNING' AND heartbeat_em>=DATEADD(SECOND,-35,@agora) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END online,
                   CASE WHEN heartbeat_em>@agora THEN 0 ELSE DATEDIFF_BIG(SECOND,heartbeat_em,@agora) END heartbeat_age_seconds,
                   iniciado_em,heartbeat_em,encerrado_em,versao,configuration_bundle_version,solution_schema_expected
            FROM controle.runtime_componente
            ORDER BY node_id,componente;

            SELECT status,COUNT_BIG(*) quantidade
            FROM ingestao.lote
            GROUP BY status
            ORDER BY status;

            SELECT TOP(20) lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,lease_owner,lease_adquirido_em,heartbeat_em
            FROM ingestao.lote
            WHERE status IN(N'VALIDANDO',N'PROCESSANDO')
            ORDER BY lease_adquirido_em,lote_id;

            SELECT TOP(20) e.entrega_id,e.status,g.codigo,so.codigo,b.nome_arquivo,e.recebido_em,e.ultima_atualizacao
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
            ORDER BY e.recebido_em DESC,e.entrega_id DESC;

            SELECT TOP(1) iniciado_em,finalizado_em,objetos_examinados,orfaos_removidos,temporarios_removidos,locks_nao_adquiridos,falhas_storage
            FROM controle.bronze_manutencao_ciclo
            ORDER BY finalizado_em DESC,ciclo_id DESC;

            SELECT TOP(5) linkage_run_id,tipo_run,status,modelo_versao,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,iniciado_em,finalizado_em
            FROM identidade.linkage_run
            ORDER BY iniciado_em DESC,linkage_run_id DESC;

            SELECT CONVERT(NVARCHAR(32),(
                SELECT value
                FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.SolutionSchema'));
            """, connection)
        {
            CommandTimeout = 5
        };

        var components = new List<RuntimeComponentStatus>();
        var queue = new List<QueueStatus>();
        var processing = new List<ActiveProcessingStatus>();
        var deliveries = new List<RecentDeliveryStatus>();
        BronzeMaintenanceStatus? bronze = null;
        var linkageRuns = new List<LinkageRunStatus>();
        string? databaseSolutionSchema = null;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            components.Add(new RuntimeComponentStatus(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetInt64(5),
                ReadDateTimeOffset(reader, 6),
                ReadDateTimeOffset(reader, 7),
                ReadNullableDateTimeOffset(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            queue.Add(new QueueStatus(reader.GetString(0), reader.GetInt64(1)));

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            processing.Add(new ActiveProcessingStatus(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), ReadNullableDateTimeOffset(reader, 8), ReadNullableDateTimeOffset(reader, 9)));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            deliveries.Add(new RecentDeliveryStatus(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                ReadDateTimeOffset(reader, 5), ReadDateTimeOffset(reader, 6)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            bronze = new BronzeMaintenanceStatus(
                ReadDateTimeOffset(reader, 0), ReadDateTimeOffset(reader, 1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            linkageRuns.Add(new LinkageRunStatus(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), ReadDateTimeOffset(reader, 9), ReadNullableDateTimeOffset(reader, 10)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct) && !reader.IsDBNull(0))
            databaseSolutionSchema = reader.GetString(0);

        var configurationHealth = EvaluateConfigurationHealth(components, databaseSolutionSchema);
        var onlineCount = components.Count(x => x.Online);
        var overall = components.Count == 0 || onlineCount == 0
            ? "FALHA"
            : onlineCount == components.Count
                ? "NORMAL"
                : "ATENCAO";
        if (configurationHealth.Status == "DIVERGENTE")
            overall = "FALHA";

        return new OperationalMonitorSnapshot(
            DateTimeOffset.UtcNow,
            overall,
            configurationHealth,
            components,
            queue,
            processing,
            deliveries,
            bronze,
            linkageRuns);
    }

    public static bool IsSchemaUnavailable(SqlException ex) => ex.Number is 207 or 208;

    private static ConfigurationBundleHealthStatus EvaluateConfigurationHealth(
        IReadOnlyCollection<RuntimeComponentStatus> components,
        string? databaseSolutionSchema)
    {
        var expectedBundle = NormalizeOptional(Environment.GetEnvironmentVariable("JORNADA_CONFIGURATION_BUNDLE_VERSION"));
        var expectedSchema = NormalizeOptional(Environment.GetEnvironmentVariable("JORNADA_SOLUTION_SCHEMA_VERSION"));
        var online = components.Where(x => x.Online).ToArray();
        var bundleVersions = online
            .Select(x => NormalizeOptional(x.ConfigurationBundleVersion) ?? "NAO_INFORMADO")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedSchemas = online
            .Select(x => NormalizeOptional(x.SolutionSchemaExpected) ?? "NAO_INFORMADO")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (expectedBundle is null || expectedSchema is null)
        {
            return new ConfigurationBundleHealthStatus(
                "NAO_CONFIGURADO",
                expectedBundle,
                expectedSchema,
                databaseSolutionSchema,
                bundleVersions,
                expectedSchemas,
                "A API não recebeu JORNADA_CONFIGURATION_BUNDLE_VERSION/JORNADA_SOLUTION_SCHEMA_VERSION; a checagem de paridade permanece informativa até a instalação cluster versionada.");
        }

        var mismatchedComponent = online.Any(x =>
            !string.Equals(NormalizeOptional(x.ConfigurationBundleVersion), expectedBundle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormalizeOptional(x.SolutionSchemaExpected), expectedSchema, StringComparison.OrdinalIgnoreCase));
        var mismatchedDatabase = !string.Equals(databaseSolutionSchema, expectedSchema, StringComparison.OrdinalIgnoreCase);

        if (mismatchedComponent || mismatchedDatabase)
        {
            var reason = mismatchedComponent && mismatchedDatabase
                ? "Processos residentes e SQL divergem do bundle esperado."
                : mismatchedComponent
                    ? "Ao menos um processo residente está em bundle/schema diferente do esperado."
                    : "Jornada.SolutionSchema no SQL diverge do schema esperado pelo bundle.";
            return new ConfigurationBundleHealthStatus(
                "DIVERGENTE",
                expectedBundle,
                expectedSchema,
                databaseSolutionSchema,
                bundleVersions,
                expectedSchemas,
                reason);
        }

        return new ConfigurationBundleHealthStatus(
            "OK",
            expectedBundle,
            expectedSchema,
            databaseSolutionSchema,
            bundleVersions,
            expectedSchemas,
            online.Length == 0
                ? "Bundle configurado; aguardando heartbeat de processos residentes."
                : "Todos os processos online e o SQL reportam a identidade técnica esperada.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static DateTimeOffset ReadDateTimeOffset(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidDataException($"Valor temporal inesperado no ordinal {ordinal}: {value.GetType().FullName}.")
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);
}

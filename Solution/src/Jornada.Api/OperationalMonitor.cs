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
    string? Version);

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

internal sealed record OperationalMonitorSnapshot(
    DateTimeOffset GeneratedAt,
    string OverallStatus,
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
                   iniciado_em,heartbeat_em,encerrado_em,versao
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
                reader.IsDBNull(9) ? null : reader.GetString(9)));
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

        var onlineCount = components.Count(x => x.Online);
        var overall = components.Count == 0 || onlineCount == 0
            ? "FALHA"
            : onlineCount == components.Count
                ? "NORMAL"
                : "ATENCAO";

        return new OperationalMonitorSnapshot(
            DateTimeOffset.UtcNow,
            overall,
            components,
            queue,
            processing,
            deliveries,
            bronze,
            linkageRuns);
    }

    public static bool IsSchemaUnavailable(SqlException ex) => ex.Number == 208;

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

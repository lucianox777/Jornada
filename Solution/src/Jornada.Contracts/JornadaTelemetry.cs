using System.Diagnostics.Metrics;

namespace Jornada.Contracts;

/// <summary>
/// Catálogo técnico de instrumentos. Thresholds/SLOs são homologados fora do código em config/observability.
/// Nomes são estáveis e deliberadamente não carregam CPF, e-mail, telefone, token ou identificadores de pessoa.
/// </summary>
public static class JornadaTelemetry
{
    private static readonly Meter Meter = new("Jornada", "1.0.0");
    private static readonly Histogram<double> ApiRequestDuration = Meter.CreateHistogram<double>("jornada.api.request.duration", "ms");
    private static readonly Histogram<double> ApiAuditPersistenceDuration = Meter.CreateHistogram<double>("jornada.api.audit.persistence.duration", "ms");
    private static readonly Counter<long> BronzeMaintenanceScanned = Meter.CreateCounter<long>("jornada.bronze.maintenance.scanned", "objects");
    private static readonly Counter<long> BronzeMaintenanceDeleted = Meter.CreateCounter<long>("jornada.bronze.maintenance.deleted", "objects");
    private static readonly Counter<long> BronzeMaintenanceLockMiss = Meter.CreateCounter<long>("jornada.bronze.maintenance.lock_miss", "objects");
    private static readonly Counter<long> BronzeMaintenanceStorageError = Meter.CreateCounter<long>("jornada.bronze.maintenance.storage_error", "errors");
    private static readonly Histogram<double> ProcessorDeliveryDuration = Meter.CreateHistogram<double>("jornada.processor.delivery.duration", "ms");
    private static readonly Counter<long> PipelineLostToken = Meter.CreateCounter<long>("jornada.pipeline.lost_token", "events");
    private static readonly Histogram<double> LinkageRunDuration = Meter.CreateHistogram<double>("jornada.linkage.run.duration", "ms");
    private static readonly Histogram<double> IdentityPendingAge = Meter.CreateHistogram<double>("jornada.identity.pending.age", "minutes");

    public static void RecordApiRequest(double milliseconds, string route, string method, int statusCode) =>
        ApiRequestDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("statusClass", $"{Math.Clamp(statusCode / 100, 1, 5)}xx"));

    public static void RecordApiAuditPersistence(double milliseconds) => ApiAuditPersistenceDuration.Record(milliseconds);

    public static void RecordBronzeMaintenance(long scanned, long deleted, long lockMisses, long storageErrors)
    {
        if (scanned > 0) BronzeMaintenanceScanned.Add(scanned);
        if (deleted > 0) BronzeMaintenanceDeleted.Add(deleted);
        if (lockMisses > 0) BronzeMaintenanceLockMiss.Add(lockMisses);
        if (storageErrors > 0) BronzeMaintenanceStorageError.Add(storageErrors);
    }

    public static void RecordProcessorDelivery(double milliseconds, string result) =>
        ProcessorDeliveryDuration.Record(milliseconds, new KeyValuePair<string, object?>("result", result));

    public static void RecordPipelineLostToken(string operation) =>
        PipelineLostToken.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordLinkageRun(double milliseconds, string mode) =>
        LinkageRunDuration.Record(milliseconds, new KeyValuePair<string, object?>("mode", mode));

    public static void RecordIdentityPendingAge(double minutes, string state) =>
        IdentityPendingAge.Record(minutes, new KeyValuePair<string, object?>("state", state));
}

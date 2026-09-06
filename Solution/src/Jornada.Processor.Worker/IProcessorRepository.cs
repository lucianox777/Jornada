namespace Jornada.Processor.Worker;

/// <summary>
/// Contrato operacional consumido pelo Worker. Cada provider preserva sua própria implementação
/// de SQL, transações, locking e materialização; parsing, Bronze, retry e telemetria permanecem
/// compartilhados no Processor.
/// </summary>
internal interface IProcessorRepository
{
    Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct);

    Task<ReservedBatch?> ReserveNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct);

    Task<bool> HeartbeatAsync(
        ReservedBatch batch,
        TimeSpan leaseDuration,
        CancellationToken ct);

    Task PersistValidatedAsync(
        ReservedBatch batch,
        ParsedPackage package,
        CancellationToken ct);

    Task MarkRejectedAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct);

    Task MarkQuarantineAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct);

    Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        ReservedBatch batch,
        string errorCode,
        int maxAttempts,
        TimeSpan retryBase,
        TimeSpan retryMax,
        CancellationToken ct);
}

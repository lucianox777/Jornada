namespace Jornada.Processor.Worker;

/// <summary>
/// Contrato de ciclo de vida do lote consumido pelo Worker. Cada provider preserva sua própria
/// implementação de reserva concorrente, fencing, heartbeat, recuperação e retry/poison.
/// </summary>
internal interface IProcessorLeaseRepository
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

/// <summary>
/// Contrato completo do Processor. Além do ciclo de lease, exige a publicação transacional do
/// pacote validado em Silver/Identidade/Gold. SQL, transações, locking e materialização continuam
/// específicos de cada provider; parsing, Bronze, retry e telemetria permanecem compartilhados.
/// </summary>
internal interface IProcessorRepository : IProcessorLeaseRepository
{
    Task PersistValidatedAsync(
        ReservedBatch batch,
        ParsedPackage package,
        CancellationToken ct);
}

namespace Jornada.Processor.Worker;

/// <summary>
/// Adapter fino do repositório SQL Server/Fabric para o contrato neutro do Worker.
/// Não contém regra de negócio nem SQL: apenas delega para a implementação já existente.
/// </summary>
internal sealed class SqlProcessorRepositoryAdapter(SqlProcessorRepository inner) : IProcessorRepository
{
    private readonly SqlProcessorRepository inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct) =>
        inner.RecoverExpiredLeasesAsync(maxAttempts, ct);

    public Task<ReservedBatch?> ReserveNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct) =>
        inner.ReserveNextAsync(leaseOwner, leaseDuration, ct);

    public Task<bool> HeartbeatAsync(
        ReservedBatch batch,
        TimeSpan leaseDuration,
        CancellationToken ct) =>
        inner.HeartbeatAsync(batch, leaseDuration, ct);

    public Task PersistValidatedAsync(
        ReservedBatch batch,
        ParsedPackage package,
        CancellationToken ct) =>
        inner.PersistValidatedAsync(batch, package, ct);

    public Task MarkRejectedAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct) =>
        inner.MarkRejectedAsync(batch, errorCode, ct);

    public Task MarkQuarantineAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct) =>
        inner.MarkQuarantineAsync(batch, errorCode, ct);

    public Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        ReservedBatch batch,
        string errorCode,
        int maxAttempts,
        TimeSpan retryBase,
        TimeSpan retryMax,
        CancellationToken ct) =>
        inner.ScheduleRetryOrPoisonAsync(batch, errorCode, maxAttempts, retryBase, retryMax, ct);
}

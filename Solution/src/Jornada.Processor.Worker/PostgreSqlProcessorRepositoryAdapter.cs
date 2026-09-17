namespace Jornada.Processor.Worker;

/// <summary>
/// Fronteira explícita do provider PostgreSQL. O cutover Pessoa v4 (origem opcional e
/// identificadores 0..N) é normativo no SQL Server e ainda não possui persistência
/// PostgreSQL equivalente. Falha antes de qualquer escrita em vez de reinterpretar
/// ausencia de codigoPessoaOrigem pelo contrato legado.
/// </summary>
internal sealed class PostgreSqlProcessorRepositoryAdapter(PostgreSqlProcessorRepository inner) : IProcessorRepository
{
    private readonly PostgreSqlProcessorRepository inner = inner ?? throw new ArgumentNullException(nameof(inner));

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
        CancellationToken ct)
    {
        if (batch.PessoaSchemaVersao >= 4 || package.Manifest.PessoaSchemaVersao >= 4)
            throw new InvalidDataException(
                "Pessoa schema v4 exige o runtime operacional SQL Server; o adapter PostgreSQL não implementa origem opcional/identificadores 0..N.");

        return inner.PersistValidatedAsync(batch, package, ct);
    }

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

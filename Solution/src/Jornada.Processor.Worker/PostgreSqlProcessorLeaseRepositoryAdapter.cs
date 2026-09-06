using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Processor.Worker;

/// <summary>
/// Adapta o ciclo operacional PostgreSQL já implementado em Operational.Sql para o contrato
/// neutro do Processor. Esta classe deliberadamente não implementa IProcessorRepository:
/// a publicação Silver/Identidade/Gold PostgreSQL ainda deve ser concluída antes de habilitar
/// o Worker completo nesse provider.
/// </summary>
internal sealed class PostgreSqlProcessorLeaseRepositoryAdapter(PostgreSqlProcessorLeaseStore inner)
    : IProcessorLeaseRepository
{
    private readonly PostgreSqlProcessorLeaseStore inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct) =>
        inner.RecoverExpiredLeasesAsync(maxAttempts, ct);

    public async Task<ReservedBatch?> ReserveNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct)
    {
        var lease = await inner.ReserveNextAsync(leaseOwner, leaseDuration, ct);
        return lease is null ? null : ToReservedBatch(lease);
    }

    public Task<bool> HeartbeatAsync(
        ReservedBatch batch,
        TimeSpan leaseDuration,
        CancellationToken ct) =>
        inner.HeartbeatAsync(ToPostgreSqlLease(batch), leaseDuration, ct);

    public Task MarkRejectedAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct) =>
        inner.MarkFailedAsync(ToPostgreSqlLease(batch), "REJEITADO", errorCode, ct);

    public Task MarkQuarantineAsync(
        ReservedBatch batch,
        string errorCode,
        CancellationToken ct) =>
        inner.MarkFailedAsync(ToPostgreSqlLease(batch), "QUARENTENA", errorCode, ct);

    public async Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        ReservedBatch batch,
        string errorCode,
        int maxAttempts,
        TimeSpan retryBase,
        TimeSpan retryMax,
        CancellationToken ct)
    {
        var outcome = await inner.ScheduleRetryOrPoisonAsync(
            ToPostgreSqlLease(batch),
            errorCode,
            maxAttempts,
            retryBase,
            retryMax,
            ct);

        return outcome == PostgreSqlProcessingFailureOutcome.Poison
            ? ProcessingFailureOutcome.Poison
            : ProcessingFailureOutcome.RetryScheduled;
    }

    private static ReservedBatch ToReservedBatch(PostgreSqlProcessorLease lease)
    {
        IntegrationNature? natureza = null;
        if (!string.IsNullOrWhiteSpace(lease.Natureza))
        {
            if (!Enum.TryParse<IntegrationNature>(lease.Natureza, ignoreCase: false, out var parsed))
                throw new InvalidDataException($"Natureza de integração PostgreSQL inválida: {lease.Natureza}.");
            natureza = parsed;
        }

        if (string.IsNullOrWhiteSpace(lease.PessoaSchemaRef) || lease.PessoaSchemaSha256 is null)
            throw new InvalidDataException("Contrato cadastral PostgreSQL ativo sem schema/SHA-256 aprovado.");

        return new ReservedBatch(
            lease.LoteId,
            lease.EntregaId,
            lease.LeaseId,
            lease.LeaseOwner,
            lease.AttemptNumber,
            lease.GestorCodigo,
            lease.GestorId,
            lease.SistemaOrigemId,
            lease.CodigoSistemaOrigem,
            lease.GestorPessoaVersaoId,
            natureza,
            lease.TipoRegistroId,
            lease.TipoRegistroVersaoId,
            lease.CodigoTipo,
            lease.TipoVersao,
            lease.PessoaSchemaVersao,
            lease.DataReferencia,
            lease.PayloadSha256,
            lease.NomeArquivo,
            lease.ObjetoChave,
            lease.TamanhoBytes,
            lease.PessoaSchemaRef,
            lease.PessoaSchemaSha256,
            lease.RegistroSchemaRef,
            lease.RegistroSchemaSha256,
            lease.QcStatus,
            lease.OriginaEnderecoCasaAbrigoSigilosa,
            lease.DataInicioPermitidaConcessao,
            lease.DataFimPermitidaConcessao,
            lease.RegimeVigencia);
    }

    private static PostgreSqlProcessorLease ToPostgreSqlLease(ReservedBatch batch) =>
        new(
            batch.LoteId,
            batch.EntregaId,
            batch.LeaseId,
            batch.LeaseOwner,
            batch.AttemptNumber,
            batch.GestorCodigo,
            batch.GestorId,
            batch.SistemaOrigemId,
            batch.CodigoSistemaOrigem,
            batch.GestorPessoaVersaoId,
            batch.PessoaSchemaVersao,
            batch.Natureza?.ToString(),
            batch.TipoRegistroId,
            batch.TipoRegistroVersaoId,
            batch.CodigoTipo,
            batch.TipoVersao,
            batch.DataReferencia,
            batch.PayloadSha256,
            batch.NomeArquivo,
            batch.ObjetoChave,
            batch.PayloadBytes,
            batch.PessoaSchemaRef,
            batch.PessoaSchemaSha256,
            batch.RegistroSchemaRef,
            batch.RegistroSchemaSha256,
            batch.QcStatus,
            batch.OriginaEnderecoCasaAbrigoSigilosa,
            batch.DataInicioPermitidaConcessao,
            batch.DataFimPermitidaConcessao,
            batch.RegimeVigencia);
}

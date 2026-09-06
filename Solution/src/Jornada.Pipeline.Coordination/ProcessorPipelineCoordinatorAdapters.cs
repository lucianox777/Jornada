namespace Jornada.Pipeline.Coordination;

/// <summary>
/// Expõe a coordenação SQL Server/Fabric pela fronteira neutra consumida pelo Processor,
/// sem alterar o contrato concreto usado pelos demais jobs e testes.
/// </summary>
public sealed class SqlProcessorPipelineCoordinatorAdapter(SqlPipelineCoordinator inner)
    : IProcessorPipelineCoordinator
{
    private readonly SqlPipelineCoordinator inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IProcessorPipelineLease?> TryAcquireProcessorBatchAsync(CancellationToken cancellationToken)
    {
        var lease = await inner.TryAcquireProcessorBatchAsync(cancellationToken);
        return lease is null ? null : new SqlProcessorPipelineLeaseAdapter(lease);
    }

    private sealed class SqlProcessorPipelineLeaseAdapter(PipelineCoordinationLease inner)
        : IProcessorPipelineLease
    {
        private readonly PipelineCoordinationLease inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public int SessionId => inner.SessionId;
        public CancellationToken LostToken => inner.LostToken;
        public bool IsLost => inner.IsLost;
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>
/// Expõe advisory locks PostgreSQL pela mesma fronteira do Processor. A persistência Silver/Gold
/// PostgreSQL permanece uma implementação separada do repositório e não é simulada por este adapter.
/// </summary>
public sealed class PostgreSqlProcessorPipelineCoordinatorAdapter(PostgreSqlPipelineCoordinator inner)
    : IProcessorPipelineCoordinator
{
    private readonly PostgreSqlPipelineCoordinator inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IProcessorPipelineLease?> TryAcquireProcessorBatchAsync(CancellationToken cancellationToken)
    {
        var lease = await inner.TryAcquireProcessorBatchAsync(cancellationToken);
        return lease is null ? null : new PostgreSqlProcessorPipelineLeaseAdapter(lease);
    }

    private sealed class PostgreSqlProcessorPipelineLeaseAdapter(PostgreSqlPipelineCoordinationLease inner)
        : IProcessorPipelineLease
    {
        private readonly PostgreSqlPipelineCoordinationLease inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public int SessionId => inner.SessionId;
        public CancellationToken LostToken => inner.LostToken;
        public bool IsLost => inner.IsLost;
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

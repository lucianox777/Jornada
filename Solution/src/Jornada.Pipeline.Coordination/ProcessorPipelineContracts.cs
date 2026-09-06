namespace Jornada.Pipeline.Coordination;

/// <summary>
/// Lease neutro consumido pelo Processor. A implementação concreta continua responsável
/// pela semântica de sessão, heartbeat e liberação de locks de cada provider.
/// </summary>
public interface IProcessorPipelineLease : IAsyncDisposable
{
    int SessionId { get; }
    CancellationToken LostToken { get; }
    bool IsLost { get; }
}

/// <summary>
/// Fronteira mínima entre o Processor e a coordenação do corpus integrado.
/// SQL Server/Fabric e PostgreSQL mantêm implementações de locking próprias.
/// </summary>
public interface IProcessorPipelineCoordinator
{
    Task<IProcessorPipelineLease?> TryAcquireProcessorBatchAsync(CancellationToken cancellationToken);
}

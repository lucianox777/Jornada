namespace Jornada.Contracts;

/// <summary>
/// Códigos de saída de processos da Jornada para execução finita e orquestração.
/// Um processo residente continua sendo gerenciado pelo host; estes códigos
/// não transformam erros de negócio em sucesso de infraestrutura.
/// </summary>
public static class JornadaExitCodes
{
    public const int OK = 0;
    public const int FAILURE = 1;
    public const int VERIFICATION_FAILED = 2;
    public const int INCOMPLETE = 3;
    public const int INVALID_PRECONDITION = 4;
    public const int INVALID_ARGS = 64;
    public const int CANCELLED = 130;
}

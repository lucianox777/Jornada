namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Fail-closed gate evaluated before registering or starting the draft worker.
/// The reference is loaded explicitly by environment bootstrap, never by this gate.
/// </summary>
public static class GenerateDraftIbgePrecondition
{
    public static async Task RequireActiveAsync(Func<Task<bool>> hasActiveReference)
    {
        ArgumentNullException.ThrowIfNull(hasActiveReference);
        if (!await hasActiveReference())
            throw new InvalidOperationException(
                "GENERATE_DRAFT exige referência IBGE ATIVA carregada por ENSURE_NAME_FREQUENCY_SNAPSHOT explícito.");
    }
}

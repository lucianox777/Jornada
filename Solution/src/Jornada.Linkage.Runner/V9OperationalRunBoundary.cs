namespace Jornada.Linkage.Runner;

/// <summary>
/// Typed handoff of the verified, immutable V9 cohort runtime.
/// The productive executor must receive this object, never reload scientific
/// parameters from SQL. No SQL, run creation, scoring or publication occurs here.
/// </summary>
public sealed class V9OperationalRunBoundary
{
    public V9OperationalPinnedCohortRuntime Runtime { get; }

    public V9OperationalRunBoundary(V9OperationalPinnedCohortRuntime runtime)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (string.IsNullOrWhiteSpace(runtime.WinnerSha256)
            || runtime.WinnerSha256.Length != 64)
            throw new InvalidDataException("RUNNER_V9_INVALID_RUNTIME: vencedor imutável inválido.");
    }

    /// <summary>
    /// Mandatory fail-closed gate until full operational graph/cohort parity,
    /// SQL physical tests and explicit productive certification are approved.
    /// </summary>
    public void EnsureProductiveCertified() =>
        throw new InvalidOperationException(
            "RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED: runtime V9 imutável preparado, mas adaptador produtivo não certificado; fallback SQL proibido.");
}

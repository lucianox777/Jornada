namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Detailed errors ONLY for interactive local Development.
/// Never put raw exceptions/connection strings in public GitHub Actions or production logs.
/// The preallocated calibrador.log and CI artifacts continue to contain safe codes only.
/// </summary>
public static class V9WorkerDevelopmentDiagnostics
{
    public static bool Enabled(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        return string.Equals(get("DOTNET_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(get("ASPNETCORE_ENVIRONMENT"))
                || string.Equals(get("ASPNETCORE_ENVIRONMENT"), "Development",
                    StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(get("CI"))
            && string.IsNullOrWhiteSpace(get("GITHUB_ACTIONS"))
            && !string.Equals(get("JORNADA_RUNTIME_MODE"), "PROD", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(get("JORNADA_RUNTIME_MODE"), "PRODUCTION", StringComparison.OrdinalIgnoreCase);
    }

    public static void WriteException(Exception error, TextWriter? destination = null,
        Func<string, string?>? get = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Enabled(get)) return;
        try { (destination ?? Console.Error).WriteLine(error.ToString()); }
        catch { /* Diagnostics must never change failure or trigger unhandled dump. */ }
    }
}

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Full exception detail follows Development or an explicitly authorized private,
/// Homologation diagnostic sink. Production always exposes stable error codes only.
/// CI is not an environment classification; diagnostic destinations must be access-controlled.
/// The 10 KiB reserved calibrador.log remains safe-code-only in every mode.
/// </summary>
public static class V9WorkerDevelopmentDiagnostics
{
    private static bool IsHomologation(string? environment) =>
        string.Equals(environment, "Staging", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Homologation", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Homologacao", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "HML", StringComparison.OrdinalIgnoreCase);

    private static bool IsProduction(string? environment) =>
        string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Prod", StringComparison.OrdinalIgnoreCase);

    public static bool Enabled(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        var dotnet = get("DOTNET_ENVIRONMENT");
        var aspnet = get("ASPNETCORE_ENVIRONMENT");
        var mode = get("JORNADA_RUNTIME_MODE");

        // Fail closed for any contradictory, explicitly production environment.
        if (IsProduction(dotnet) || IsProduction(aspnet) || IsProduction(mode))
            return false;

        bool development = string.Equals(dotnet, "Development", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(aspnet)
                || string.Equals(aspnet, "Development", StringComparison.OrdinalIgnoreCase))
;
        if (development) return true;

        // HML must be private by deployment configuration, never a CI/stdout
        // artifact masquerading as a diagnostic sink. No implicit opt-in.
        bool homologation = IsHomologation(dotnet)
            && (string.IsNullOrWhiteSpace(aspnet) || IsHomologation(aspnet))
            && (string.IsNullOrWhiteSpace(mode) || IsHomologation(mode));
        return homologation
            && string.Equals(get("JORNADA_PRIVATE_DIAGNOSTICS"), "1",
                StringComparison.Ordinal);
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

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Full exception detail follows Development and Homologation runtime modes. Production always exposes stable error codes only.
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

        // Only existing runtime environment variables govern diagnostics.
        // Missing or contradictory declarations fail closed as Production.
        var declared = new[] { dotnet, aspnet, mode }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (declared.Length == 0) return false;
        bool development = declared.All(value =>
            string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "DEV", StringComparison.OrdinalIgnoreCase));
        bool homologation = declared.All(IsHomologation);
        return development || homologation;
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

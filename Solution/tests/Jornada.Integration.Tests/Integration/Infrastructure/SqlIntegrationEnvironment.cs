namespace Jornada.Tests.Integration.Infrastructure;

internal static class SqlIntegrationEnvironment
{
    internal const string ConnectionStringVariable = "JORNADA_TEST_SQL_CONNECTION";
    internal const string ImageVariable = "JORNADA_TEST_SQL_IMAGE";
    internal const string UseExistingDatabaseVariable = "JORNADA_TEST_SQL_USE_EXISTING_DATABASE";
    internal const string TargetVariable = "JORNADA_TEST_SQL_TARGET";
    internal const string SqlServer2022Target = "SQL_SERVER_2022";
    internal const string FabricSqlDatabaseTarget = "FABRIC_SQL_DATABASE";

    // SQL Server 2022 CU26 / Ubuntu 22.04, fixado por tag + digest.
    // Microsoft Artifact Registry, publicado em 2026-07-16.
    internal const string DefaultSqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@" +
        "sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable)
        ?? throw new InvalidOperationException(
            $"{ConnectionStringVariable} não foi inicializada pela fixture de integração.");

    internal static string SqlServerImage =>
        Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } configured
            ? configured
            : DefaultSqlServerImage;

    internal static bool UseExistingExternalDatabase =>
        bool.TryParse(Environment.GetEnvironmentVariable(UseExistingDatabaseVariable), out var value)
        && value;

    internal static string Target
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(TargetVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return SqlServer2022Target;
            }

            var normalized = configured.Trim().ToUpperInvariant();
            return normalized switch
            {
                SqlServer2022Target => SqlServer2022Target,
                FabricSqlDatabaseTarget => FabricSqlDatabaseTarget,
                _ => throw new InvalidOperationException(
                    $"{TargetVariable} inválido: '{configured}'. Valores aceitos: " +
                    $"{SqlServer2022Target} ou {FabricSqlDatabaseTarget}."),
            };
        }
    }

    internal static bool IsFabricSqlDatabase =>
        string.Equals(Target, FabricSqlDatabaseTarget, StringComparison.Ordinal);
}

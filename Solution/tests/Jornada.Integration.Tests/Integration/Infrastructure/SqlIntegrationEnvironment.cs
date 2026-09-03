namespace Jornada.Tests.Integration.Infrastructure;

internal static class SqlIntegrationEnvironment
{
    internal const string ConnectionStringVariable = "JORNADA_TEST_SQL_CONNECTION";
    internal const string ImageVariable = "JORNADA_TEST_SQL_IMAGE";
    internal const string UseExistingDatabaseVariable = "JORNADA_TEST_SQL_USE_EXISTING_DATABASE";

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
}

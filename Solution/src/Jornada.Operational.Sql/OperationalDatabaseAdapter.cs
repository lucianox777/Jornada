using System.Data.Common;

namespace Jornada.Operational.Sql;

public static class OperationalDatabaseProviders
{
    public const string SqlServer = "SqlServer";
}

/// <summary>
/// Fronteira ADO.NET do banco operacional da Jornada.
/// A candidata v5.00 suporta um único runtime relacional: Microsoft SQL Server.
/// </summary>
public interface IOperationalDatabaseAdapter
{
    string Provider { get; }

    DbConnection CreateConnection();

    DbConnection CreateDedicatedSessionConnection();

    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);

    Task<DbConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default);
}

public static class OperationalDatabaseAdapterFactory
{
    public static IOperationalDatabaseAdapter Create(string? provider, string connectionString)
    {
        var normalized = string.IsNullOrWhiteSpace(provider)
            ? OperationalDatabaseProviders.SqlServer
            : provider.Trim();

        if (string.Equals(normalized, OperationalDatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "MicrosoftSql", StringComparison.OrdinalIgnoreCase))
            return new OperationalSqlAdapter(connectionString);

        throw new ArgumentOutOfRangeException(
            nameof(provider),
            provider,
            $"Database:Provider não suportado nesta distribuição. Use {OperationalDatabaseProviders.SqlServer}.");
    }
}

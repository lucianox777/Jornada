using System.Data.Common;

namespace Jornada.Operational.Sql;

public static class OperationalDatabaseProviders
{
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSql";
}

/// <summary>
/// Fronteira ADO.NET neutra para o banco operacional da Jornada.
///
/// Ela não tenta esconder diferenças de dialeto. O objetivo é neutralizar criação, abertura e
/// ciclo de vida das conexões; SQL, locking, bulk e DDL específicos continuam atrás de componentes
/// próprios de cada provider.
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
            || string.Equals(normalized, "MicrosoftSql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Fabric", StringComparison.OrdinalIgnoreCase))
            return new OperationalSqlAdapter(connectionString);

        if (string.Equals(normalized, OperationalDatabaseProviders.PostgreSql, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Postgres", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return new PostgreSqlOperationalAdapter(connectionString);

        throw new ArgumentOutOfRangeException(
            nameof(provider),
            provider,
            $"Database:Provider não suportado. Use {OperationalDatabaseProviders.SqlServer} ou {OperationalDatabaseProviders.PostgreSql}.");
    }
}

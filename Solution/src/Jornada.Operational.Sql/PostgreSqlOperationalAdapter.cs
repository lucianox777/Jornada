using System.Data.Common;
using Npgsql;

namespace Jornada.Operational.Sql;

/// <summary>
/// Adapter operacional PostgreSQL da Jornada.
///
/// A conexão dedicada desabilita pooling e enlistment automático para que advisory locks e outros
/// recursos vinculados à sessão física tenham ciclo de vida determinístico.
/// </summary>
public sealed class PostgreSqlOperationalAdapter : IOperationalDatabaseAdapter
{
    private readonly string connectionString;
    private readonly string dedicatedSessionConnectionString;

    public PostgreSqlOperationalAdapter(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string operacional da Jornada é obrigatória.", nameof(connectionString));

        var normal = new NpgsqlConnectionStringBuilder(connectionString);
        this.connectionString = normal.ConnectionString;

        var dedicated = new NpgsqlConnectionStringBuilder(this.connectionString)
        {
            Pooling = false,
            Enlist = false
        };
        dedicatedSessionConnectionString = dedicated.ConnectionString;
    }

    public string Provider => OperationalDatabaseProviders.PostgreSql;

    public DbConnection CreateConnection() => new NpgsqlConnection(connectionString);

    public DbConnection CreateDedicatedSessionConnection() => new NpgsqlConnection(dedicatedSessionConnectionString);

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<DbConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateDedicatedSessionConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Fronteira legada fortemente tipada para a família Microsoft SQL.
///
/// Novos componentes portáveis devem preferir IOperationalDatabaseAdapter. Esta interface permanece
/// para que a migração para múltiplos providers seja incremental e não altere o comportamento SQL Server
/// já homologado.
/// </summary>
public interface IOperationalSqlAdapter
{
    SqlConnection CreateConnection();

    SqlConnection CreateDedicatedSessionConnection();

    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);

    Task<SqlConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter operacional Microsoft SQL. Atende a interface legada fortemente tipada e a nova fronteira
/// ADO.NET neutra. SQL Database in Microsoft Fabric continua usando este provider enquanto mantiver
/// compatibilidade com o protocolo/driver Microsoft SQL.
/// </summary>
public sealed class OperationalSqlAdapter : IOperationalSqlAdapter, IOperationalDatabaseAdapter
{
    private readonly string connectionString;
    private readonly string dedicatedSessionConnectionString;

    public OperationalSqlAdapter(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string operacional da Jornada é obrigatória.", nameof(connectionString));

        var normal = new SqlConnectionStringBuilder(connectionString);
        this.connectionString = normal.ConnectionString;

        var dedicated = new SqlConnectionStringBuilder(this.connectionString)
        {
            Pooling = false,
            Enlist = false
        };
        dedicatedSessionConnectionString = dedicated.ConnectionString;
    }

    public string Provider => OperationalDatabaseProviders.SqlServer;

    public SqlConnection CreateConnection() => new(connectionString);

    public SqlConnection CreateDedicatedSessionConnection() => new(dedicatedSessionConnectionString);

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
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

    public async Task<SqlConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default)
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

    DbConnection IOperationalDatabaseAdapter.CreateConnection() => CreateConnection();

    DbConnection IOperationalDatabaseAdapter.CreateDedicatedSessionConnection() => CreateDedicatedSessionConnection();

    async Task<DbConnection> IOperationalDatabaseAdapter.OpenAsync(CancellationToken cancellationToken) =>
        await OpenAsync(cancellationToken);

    async Task<DbConnection> IOperationalDatabaseAdapter.OpenDedicatedSessionAsync(CancellationToken cancellationToken) =>
        await OpenDedicatedSessionAsync(cancellationToken);
}

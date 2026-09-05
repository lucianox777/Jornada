using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Fronteira única entre os componentes operacionais da Jornada e a família Microsoft SQL.
///
/// A abstração é intencionalmente estreita: SQL Server e SQL Database in Microsoft Fabric usam
/// o mesmo protocolo/driver e o mesmo T-SQL de aplicação na maior parte do núcleo. Diferenças reais
/// de plataforma devem ser encapsuladas aqui (ou em adaptadores especializados) somente quando forem
/// demonstradas por teste, sem introduzir branches de plataforma na lógica funcional.
/// </summary>
public interface IOperationalSqlAdapter
{
    /// <summary>Cria uma conexão normal, preservando as propriedades configuradas pelo ambiente.</summary>
    SqlConnection CreateConnection();

    /// <summary>
    /// Cria uma sessão dedicada sem pooling e sem alistamento automático. Use para locks com
    /// LockOwner='Session' e outros mecanismos cuja vida deve coincidir com a sessão física.
    /// </summary>
    SqlConnection CreateDedicatedSessionConnection();

    /// <summary>Cria e abre uma conexão normal.</summary>
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Cria e abre uma sessão dedicada.</summary>
    Task<SqlConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter operacional Microsoft SQL. Nesta versão existe uma única implementação para SQL Server;
/// a mesma fronteira será usada no teste de compatibilidade com SQL Database in Microsoft Fabric.
/// Não existe comportamento específico de Fabric enquanto uma diferença concreta não for comprovada.
/// </summary>
public sealed class OperationalSqlAdapter : IOperationalSqlAdapter
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
}

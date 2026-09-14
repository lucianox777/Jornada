using System.Data.Common;
using System.Globalization;

namespace Jornada.Ensaio;

public interface IPipelineDrainProbe
{
    Task<long> CountPendingAsync(CancellationToken cancellationToken);
}

public sealed class SqlServerPipelineDrainProbe(Func<DbConnection> openConnection) : IPipelineDrainProbe
{
    private const string PendingItemsSql = """
        SELECT COUNT(*)
        FROM ingestao.item_processado
        WHERE status NOT IN ('PROCESSADO','ERRO','DESCARTADO')
        """;

    public async Task<long> CountPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = PendingItemsSql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0L, CultureInfo.InvariantCulture);
    }
}

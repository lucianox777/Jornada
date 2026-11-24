using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>Shared read-only authentication of the exact disposable V9 DEV attempt.</summary>
public static class V9DisposableAttemptDatabase
{
    public static async Task VerifyAsync(SqlConnection connection, Guid attemptId,
        CancellationToken cancellationToken, SqlTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (attemptId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_ATTEMPT_MISSING");
        if (connection.State != ConnectionState.Open
            || (transaction is not null && !ReferenceEquals(transaction.Connection, connection)))
            throw new InvalidOperationException("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED");

        await using var check = new SqlCommand("""
            SELECT DB_NAME(),
                (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties
                 WHERE class=0 AND name=N'Jornada.EnvironmentProfile'),
                (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties
                 WHERE class=0 AND name=N'Jornada.V9.DevAttempt');
            """, connection, transaction);
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)
            || !string.Equals(reader.GetString(0), "JornadaV9Dev_" + attemptId.ToString("N"), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), "Development", StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), attemptId.ToString("D"), StringComparison.Ordinal))
            throw new InvalidOperationException("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED");
    }
}

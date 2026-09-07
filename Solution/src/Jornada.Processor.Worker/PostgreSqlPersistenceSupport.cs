using System.Data;
using System.Data.Common;

namespace Jornada.Processor.Worker;

/// <summary>Provider-specific temporal binding and best-effort transaction cleanup.</summary>
internal static class PostgreSqlPersistenceSupport
{
    internal static object NormalizeParameterValue(DbType type, object? value)
    {
        if (value is null || value is DBNull)
            return DBNull.Value;

        // PostgreSQL timestamptz represents an instant, not the source's time-zone.
        // Npgsql requires DateTimeOffset values to have offset zero. Preserve the
        // instant rather than changing the clock time or the source calendar date.
        return type == DbType.DateTimeOffset && value is DateTimeOffset timestamp
            ? timestamp.ToUniversalTime()
            : value;
    }

    internal static async Task RollbackPreservingOriginalAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Npgsql may already have disposed/aborted the transaction after a
            // failed command. The original exception is the diagnostic to retain.
            // Disposing the connection below still releases the transaction.
        }
    }
}

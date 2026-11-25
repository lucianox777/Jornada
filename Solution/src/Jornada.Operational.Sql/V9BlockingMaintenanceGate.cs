using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// RF #1936 C07: write admission for the controlled Runner
/// V9 bootstrap/refresh. No productive activation; no DML in this class.
/// Call with effective host environment and explicit configuration, on the
/// actual write transaction, before ANY V9 projection mutation.
/// </summary>
public static class V9BlockingMaintenanceGate
{
    public const string EnabledSetting = "Linkage:V9BlockingMaintenance:Enabled";
    public const string AttemptSetting = "Linkage:V9BlockingMaintenance:DevAttempt";
    public const string ProductiveNotCertified = "RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED";

    public static bool IsExplicitDevOptIn(string? effectiveEnvironment, bool enabled, Guid attemptId) =>
        enabled && attemptId != Guid.Empty
        && string.Equals(effectiveEnvironment, "Development", StringComparison.Ordinal);

    public static async Task RequireAsync(string? effectiveEnvironment, bool enabled, Guid attemptId,
        SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Deny all default/unknown/HML/PROD states before accessing SQL.
        if (!IsExplicitDevOptIn(effectiveEnvironment, enabled, attemptId))
            throw new InvalidOperationException(ProductiveNotCertified + ": V9 blocking maintenance denied.");
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED");

        // Reuse the Runner's physical verification, not a parallel weaker rule.
        await V9DisposableAttemptDatabase.VerifyAsync(connection, attemptId, cancellationToken, transaction);
        // Hold this shared lock until the caller commits/rolls back. Reversal
        // and migration take the same exclusive lock; no race with disabling.
        await using var check = new SqlCommand("""
            DECLARE @lock_result int;
            EXEC @lock_result=sys.sp_getapplock
                @Resource=N'Jornada.V9.BlockingSchema', @LockMode=N'Shared',
                @LockOwner=N'Transaction', @LockTimeout=10000;
            SELECT CASE WHEN @lock_result>=0
                AND OBJECT_ID(N'identidade.blocking_chave_v9',N'U') IS NOT NULL
                AND OBJECT_ID(N'identidade.blocking_chave_v9_cobertura',N'U') IS NOT NULL
                AND EXISTS(SELECT 1 FROM sys.extended_properties
                           WHERE class=0 AND name=N'Jornada.V9.BlockingSchemaEnabled'
                             AND CONVERT(nvarchar(10),value)=N'1')
                THEN 1 ELSE 0 END;
            """, connection, transaction);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_SCHEMA_NOT_AVAILABLE");
        // Recheck resident identity after acquiring the lock. All later
        // writes must remain in this same transaction; no connection reuse.
        await V9DisposableAttemptDatabase.VerifyAsync(connection, attemptId, cancellationToken, transaction);
    }
}

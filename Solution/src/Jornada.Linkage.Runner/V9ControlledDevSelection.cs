using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fail-closed admission for a single V9 stage-3 attempt in disposable DEV SQL.
/// This does NOT authorize Program.cs activation or RunAsync publication.
/// The caller must still use the existing Runner lifecycle and publication path.
/// </summary>
public static class V9ControlledDevSelection
{
    public static async Task<V9OperationalRunBoundary> VerifyAsync(
        IConfiguration configuration,
        IOperationalSqlAdapter operationalSql,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(operationalSql);
        if (attemptId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_ATTEMPT_MISSING");

        // A typed, SHA-verified selection is mandatory. Never fall back to
        // identity.model_linkage SQL parameters or an implicit active model.
        if (configuration.GetValue<int?>("Linkage:SelectedModelVersion") != 9
            || string.IsNullOrWhiteSpace(configuration["Linkage:V9SelectionFile"]))
            throw new InvalidOperationException("RUNNER_V9_DEV_SELECTION_REQUIRED");

        var runtime = await V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(
            configuration["Linkage:V9SelectionFile"]!, cancellationToken);
        var boundary = new V9OperationalRunBoundary(runtime);

        // Database name AND two database-resident properties must be bound to
        // this exact disposable attempt; a DEV process flag is insufficient.
        // No SQL mutation or run creation is permitted by this preflight.
        var databaseName = "JornadaV9Dev_" + attemptId.ToString("N");
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var check = new SqlCommand(
            """
            SELECT CASE WHEN DB_NAME()=@database
              AND EXISTS (
                SELECT 1 FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                  AND CONVERT(nvarchar(200),value)=N'Development')
              AND EXISTS (
                SELECT 1 FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.V9.DevAttempt'
                  AND CONVERT(nvarchar(200),value)=@attempt)
              THEN 1 ELSE 0 END;
            """, connection);
        check.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = databaseName;
        check.Parameters.Add("@attempt", SqlDbType.NVarChar, 36).Value =
            attemptId.ToString("D");
        if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED");
        return boundary;
    }
}

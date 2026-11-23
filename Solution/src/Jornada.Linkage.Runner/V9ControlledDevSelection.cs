using System.Data;
using Jornada.Contracts;
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
    /// <summary>
    /// Bind the run ledger model identity to the file-pinned winner before
    /// any run is created. The ordinary SQL model metadata is never trusted
    /// to choose another V9 winner or an alternate blocking projection.
    /// </summary>
    public static void VerifyRunModelPin(
        ProbabilisticLinkageModelRef model, string winnerSha256)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(winnerSha256)
            || winnerSha256.Length != 64
            || winnerSha256.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new InvalidDataException("RUNNER_V9_DEV_WINNER_SHA_INVALID");
        if (model.Version != 9
            || model.ModelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winnerSha256)
            || model.BlockingContract is null
            || !string.Equals(model.BlockingContract.RuleSetFingerprintSha256,
                winnerSha256, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_DEV_MODEL_PIN_MISMATCH");
    }

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

        // No SQL mutation or run creation is permitted by this preflight.
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await VerifyDisposableAttemptDatabaseAsync(connection, attemptId, cancellationToken);
        return boundary;
    }
    /// <summary>
    /// Physical, read-only guard for the exact disposable DEV attempt.
    /// Also exposed to opt-in SQL-negative tests without requiring scientific
    /// model selection, so a rejected connection cannot start a V9 run.
    /// </summary>
    public static async Task VerifyDisposableAttemptDatabaseAsync(
        SqlConnection connection, Guid attemptId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (attemptId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_ATTEMPT_MISSING");
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED");
        var databaseName = "JornadaV9Dev_" + attemptId.ToString("N");
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
    }

}

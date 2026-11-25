using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Immutable projection pin for a single explicitly authorized DEV run.</summary>
internal sealed record V9ControlledDevBlockingMaintenance(string EffectiveEnvironment,
    Guid AttemptId, int MaximumPrefixLength, string ProjectionFingerprintSha256)
{
    internal Task VerifyIntegrityAsync(SqlConnection db, SqlTransaction tx, CancellationToken ct) =>
        BlockingProjectionPersistence.VerifyPinnedV9IndexIntegrityAsync(db, tx,
            EffectiveEnvironment, true, AttemptId, MaximumPrefixLength, ProjectionFingerprintSha256, ct);

    internal async Task VerifyCoverageAsync(SqlConnection db, SqlTransaction tx, CancellationToken ct)
    {
        await V9BlockingMaintenanceGate.RequireAsync(EffectiveEnvironment, true, AttemptId, db, tx, ct);
        await BlockingProjectionPersistence.VerifyPinnedV9ProjectionAsync(db, tx,
            AttemptId, MaximumPrefixLength, ProjectionFingerprintSha256, ct);
    }

    internal Task RefreshAsync(SqlConnection db, SqlTransaction tx, CancellationToken ct) =>
        BlockingProjectionPersistence.RefreshPinnedV9Async(db, tx,
            EffectiveEnvironment, true, AttemptId, MaximumPrefixLength, ProjectionFingerprintSha256, ct);
}

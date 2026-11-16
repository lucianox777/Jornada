using System.Data;
using System.Data.Common;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Runner-owned nonpublishing TEST cohort execution of the SQL-pinned V9 model.
/// TEST and the production-side Runner share this SAME canonical frozen
/// TF4/81-cell scorer, complete-link grouping and Resolve implementation.
/// Candidate edges must already be counted against the complete physical SQL
/// candidate graph by the caller. This is NOT yet the production batch Runner:
/// SQL materialization, batching/publication and full end-to-end parity remain
/// independently gated. No Gold, TRAIN scores, refit, publication or activation.
/// </summary>
public sealed class V9OperationalPinnedCohortRuntime
{
    // Hard cap on read-only diagnostic candidate parity; never used in TRAIN/VALIDATION.
    public const int MaximumAuditObservations = 128;

    private readonly V9FittedSnapshot winner;

    public Guid ModelId { get; }
    public string WinnerSha256 => winner.ContentSha256;

    // Exposing the frozen snapshot for TEST's physical metering does not permit
    // refit, alternate winner selection or fallback to an unpinned model.
    public V9FittedSnapshot FrozenWinner => winner;

    private V9OperationalPinnedCohortRuntime(V9FittedSnapshot winner, Guid modelId)
    {
        this.winner = winner;
        ModelId = modelId;
    }

    public static async Task<V9OperationalPinnedCohortRuntime> LoadPinnedAsync(
        DbConnection connection, Guid modelId, string expectedWinnerSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Loader V9 exige conexão SQL aberta.");
        // ReadPinnedAsync enforces exactly one TEST_ONLY row and fails closed on
        // SHA, model-id, schema, TF4 slots, 81 joint cells, and byte round-trip.
        var pinned = await V9OperationalSqlModelConverter.ReadPinnedAsync(
            connection, modelId, expectedWinnerSha256, cancellationToken);
        var verified = V9OperationalSqlModelConverter.VerifyConversion(pinned);
        if (verified.ModelId != modelId || verified.SourceSnapshotSha256 != expectedWinnerSha256
            || !verified.ByteExactRoundTrip || verified.Active || verified.Published)
            throw new InvalidDataException("Runtime V9 não corresponde ao único vencedor SQL TEST_ONLY.");
        return new V9OperationalPinnedCohortRuntime(pinned, modelId);
    }

    /// <summary>
    /// Common fail-closed execution-plan contract used by the TEST worker and
    /// the SQL-backed nonpublishing batch scorer. This checks support, not
    /// physical SQL candidate parity or permission to activate a model.
    /// </summary>
    public void RequireSupportedFrozenExecutionPlan()
    {
        var plan = winner.Payload.CandidateGrouping;
        if (plan is null
            || plan.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || plan.MaximumObservationsPerIdentity <= 0
            || plan.MinimumPairPosterior is < 0m or > 1m
            || string.IsNullOrWhiteSpace(winner.Payload.ProjectionSchemaVersion)
            || winner.Payload.ProjectionFingerprintSha256 is not { Length: 64 })
            throw new InvalidDataException(
                "V9_TEST_EXECUTION_PLAN_UNSUPPORTED: plano do vencedor não compatível com o motor agrupado.");
    }

    /// <summary>
    /// The SAME complete-link/FS/Resolve executable shared by the physical
    /// independent TEST Worker. Caller must provide the complete physical SQL
    /// graph with a reconciled row count, never truncated or synthesized.
    /// </summary>
    public V9FrozenSourceReplayResult ReplayTest(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges)
    {
        return V9FrozenSourceResolveReplay.ReplayTest(winner, test, completePhysicalSqlEdges);
    }
}

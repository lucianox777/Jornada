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
    /// Build the identical nonpublishing cohort scorer directly from a verified
    /// immutable file snapshot. No SQL model lookup, staging or fallback.
    /// This does not authorize production batch execution or publication.
    /// </summary>
    public static V9OperationalPinnedCohortRuntime FromVerifiedFileSnapshot(V9FittedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var verified = V9OperationalSqlModelConverter.VerifyConversion(snapshot);
        if (!verified.ByteExactRoundTrip || verified.Active || verified.Published
            || !string.Equals(verified.SourceSnapshotSha256, snapshot.ContentSha256, StringComparison.Ordinal))
            throw new InvalidDataException("V9: artefato não corresponde ao contrato imutável validado.");
        return new V9OperationalPinnedCohortRuntime(snapshot, verified.ModelId);
    }

    /// <summary>
    /// The SAME complete-link/FS/Resolve executable shared by the physical
    /// independent TEST Worker. Caller must provide the complete physical SQL
    /// graph with a reconciled row count, never truncated or synthesized.
    /// </summary>
    /// <summary>
    /// Accept only the complete, reconciled operational candidate graph.
    /// The independent physical SQL graph is a mandatory completeness oracle;
    /// never replace missing edges, silently truncate, or score per batch.
    /// This is diagnostic and does not certify the production RunAsync.
    /// </summary>
    public V9FrozenSourceReplayResult ReplayOperationalGraph(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        IReadOnlyList<(int A, int B)> operationalEdges,
        IReadOnlyList<(int A, int B)> physicalSqlEdges)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(operationalEdges);
        ArgumentNullException.ThrowIfNull(physicalSqlEdges);
        if (test.Count is < 2 or > MaximumAuditObservations)
            throw new InvalidDataException("V9_OPERATIONAL_GRAPH_COHORT_INVALID");
        if (test.Any(x => x is null || x.Partition != "TEST"
            || x.ObservationId <= 0 || string.IsNullOrWhiteSpace(x.Gestor)))
            throw new InvalidDataException("V9_OPERATIONAL_GRAPH_INVALID_OBSERVATION");
        var byId = test.ToDictionary(x => x.ObservationId, x => x);
        if (byId.Count != test.Count)
            throw new InvalidDataException("V9_OPERATIONAL_GRAPH_DUPLICATE_OBSERVATION");

        HashSet<(int A, int B)> Validate(IReadOnlyList<(int A, int B)> edges)
        {
            var result = new HashSet<(int A, int B)>();
            foreach (var edge in edges)
                if (edge.A >= edge.B || !byId.TryGetValue(edge.A, out var left) || !byId.TryGetValue(edge.B, out var right)
                    || string.Equals(left.Gestor, right.Gestor, StringComparison.Ordinal)
                    || !result.Add(edge))
                    throw new InvalidDataException("V9_OPERATIONAL_GRAPH_INVALID_EDGE");
            return result;
        }

        var actual = Validate(operationalEdges);
        var expected = Validate(physicalSqlEdges);
        if (!actual.SetEquals(expected))
            throw new InvalidDataException("V9_OPERATIONAL_GRAPH_INCOMPLETE_OR_DRIFTED");
        var ordered = actual.OrderBy(x => x.A).ThenBy(x => x.B).ToArray();
        return ReplayTest(test, ordered);
    }

    public V9FrozenSourceReplayResult ReplayTest(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges)
    {
        return V9FrozenSourceResolveReplay.ReplayTest(winner, test, completePhysicalSqlEdges);
    }
}

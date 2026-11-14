using System.Data;
using System.Data.Common;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Explicit, NONPUBLISHING V9 TEST-only runner rehearsal. From one immutable
/// SQL TEST_ONLY winner it executes the actual operational candidate SQL in
/// batches, reconstructs the full graph, then globally FS-scores/groups and
/// compares it with the independent physical TEST SQL graph. No legacy run
/// table, Gold, actual TEST seed, status changes or publication path.
/// NOT ProbabilisticLinkageBatchRunner.RunAsync / production certification.
/// </summary>
public static class V9ReadOnlyTestBatchRunner
{
    public static async Task<V9OperationalCandidateGraphParityReceipt> RunAsync(
        DbConnection connection,
        Guid pinnedModelId,
        string pinnedWinnerSha256,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> syntheticTest,
        IReadOnlyList<(int A, int B)> physicalSqlEdges,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(syntheticTest);
        ArgumentNullException.ThrowIfNull(physicalSqlEdges);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("V9 TEST somente leitura exige conexão SQL aberta.");
        if (syntheticTest.Count is < 2 or > V9OperationalPinnedCohortRuntime.MaximumAuditObservations)
            throw new InvalidDataException("Ensaio físico V9 tem coorte pequena estritamente limitada.");
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            batchSize, V9OperationalPinnedCohortRuntime.MaximumAuditObservations);

        // Fail BEFORE accessing a model table on any non-harness database.
        await using (var guard = connection.CreateCommand())
        {
            guard.CommandText = "SELECT DB_NAME();";
            var database = (string?)await guard.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(database, "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Runner V9 de ensaio exclusivamente autorizado no SQL descartável JornadaHarness.");
        }

        var runtime = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, pinnedModelId, pinnedWinnerSha256, cancellationToken);
        var receipt = await V9OperationalCandidateGraphParityAudit.CompareTestAsync(
            connection, runtime, syntheticTest, physicalSqlEdges,
            cancellationToken, batchSize);
        if (receipt.WinnerSha256 != pinnedWinnerSha256
            || receipt.PhysicalSqlEdgeCount != receipt.OperationalSqlEdgeCount
            || receipt.ProductionBatchPipelineCertified
            || receipt.DecisionsSha256 is not { Length: 64 }
            || receipt.IdentityGroupsSha256 is not { Length: 64 })
            throw new InvalidDataException("Recibo de ensaio V9 sem proveniência e paridade completas.");
        return receipt;
    }
}

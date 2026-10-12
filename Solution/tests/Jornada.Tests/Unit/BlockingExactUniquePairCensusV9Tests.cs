using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingExactUniquePairCensusV9Tests
{
    private static BlockingPassPairIncidenceV9 Pass(
        string id, long groups, decimal full, decimal adjacent) =>
        new(id, groups, groups, full, adjacent);

    [Test]
    public void CountsDistinctUnorderedPhysicalPairsAfterPassAndAliasDuplication()
    {
        // Three people in two identical physical signatures create six
        // incidences but only THREE different unordered candidate pairs.
        var planned = BlockingExactUniquePairCensusGateV9.Preflight(
            [Pass("P1", 1, 3m, 1m), Pass("P2", 1, 3m, 1m)], 10);
        var result = BlockingExactUniquePairCensusGateV9.WithDistinctCount(planned, 3);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Status, Is.EqualTo(
                BlockingExactUniquePairCensusGateV9.DiagnosticOnly));
            Assert.That(result.PairIncidences, Is.EqualTo(6m));
            Assert.That(result.DistinctPairs, Is.EqualTo(3));
            Assert.That(result.DuplicateIncidences, Is.EqualTo(3m));
            Assert.That(result.MaximumAllowedIncidences, Is.EqualTo(10));
        }));
    }

    [Test]
    public void CapIsEvaluatedOnAllIncidencesBeforeQuadraticSqlCanExecute()
    {
        var under = BlockingExactUniquePairCensusGateV9.Preflight(
            [Pass("ONE", 1, 6m, 2m)], 6);
        var over = BlockingExactUniquePairCensusGateV9.Preflight(
            [Pass("ONE", 1, 6m, 2m)], 5);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(under.Status, Is.EqualTo(BlockingExactUniquePairCensusGateV9.Ready));
            Assert.That(over.Status, Is.EqualTo(BlockingExactUniquePairCensusGateV9.CapExceeded));
            Assert.That(over.DistinctPairs, Is.Null);
            Assert.That(over.DuplicateIncidences, Is.Null);
            Assert.That(over.PairIncidences, Is.EqualTo(6m));
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.WithDistinctCount(over, 3)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void ReadyEmptyCandidateFrameMayCertifyZeroDistinctPairs()
    {
        var planned = BlockingExactUniquePairCensusGateV9.Preflight(
            [new BlockingPassPairIncidenceV9("NO_PAIRS", 10, 0, 0m, 0m)], 100);
        var exact = BlockingExactUniquePairCensusGateV9.WithDistinctCount(planned, 0);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(exact.DistinctPairs, Is.EqualTo(0));
            Assert.That(exact.DuplicateIncidences, Is.Zero);
            Assert.That(exact.Status, Does.Contain("DIAGNOSTIC_ONLY"));
        }));
    }

    [Test]
    public void ImpossibleOrInvalidCountsFailClosed()
    {
        var planned = BlockingExactUniquePairCensusGateV9.Preflight(
            [Pass("EXACT", 1, 3m, 1m)], 100);
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.WithDistinctCount(planned, -1)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.WithDistinctCount(planned, 4)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    [Pass("EXACT", 1, 3m, 4m)], 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    [Pass("EXACT", 1, 3m, 1m), Pass("EXACT", 1, 3m, 1m)], 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    Array.Empty<BlockingPassPairIncidenceV9>(), 100)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void DiagnosticHardCapCannotBeBypassedByConfiguration()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    [Pass("P", 1, 1m, 0m)], 0)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    [Pass("P", 1, 1m, 0m)],
                    BlockingExactUniquePairCensusGateV9.HardMaximumIncidences + 1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() =>
                BlockingExactUniquePairCensusGateV9.Preflight(
                    [Pass("P", 1, 1_000_001m, 0m)],
                    BlockingExactUniquePairCensusGateV9.HardMaximumIncidences)),
                Throws.Nothing);
        }));
    }
}

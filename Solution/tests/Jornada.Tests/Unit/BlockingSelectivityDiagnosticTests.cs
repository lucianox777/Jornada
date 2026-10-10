using Jornada.Linkage.Parameters.Worker;
using static Jornada.Linkage.Parameters.Worker.BlockingSelectivityDiagnostic;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BlockingSelectivityDiagnosticTests
{
    [Test]
    public void MissingPassMeasurementsAreNotMisreportedAsSelective()
    {
        Assert.That(Assess(Array.Empty<Pass>(), 10).State, Is.EqualTo(Status.NoEvidence));
    }

    [Test]
    public void BudgetBoundaryAndConservativeSum()
    {
        var passes = new[] { new Pass("name", 4), new Pass("mother", 6) };
        Assert.That(Assess(passes, 10).State, Is.EqualTo(Status.WithinBudget));
        Assert.That(Assess(passes, 9).State, Is.EqualTo(Status.NeedsUnionMeasurement));
    }

    [Test]
    public void RejectsNegativeCountsAndInvalidBudget()
    {
        Assert.Throws<ArgumentException>((System.Action)(() => Assess(new[] { new Pass("name", -1) }, 10)));
        Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() => Assess(Array.Empty<Pass>(), 0)));
    }

    [Test]
    public void OverflowFailsClosed()
    {
        Assert.That(Assess(new[] { new Pass("a", long.MaxValue), new Pass("b", 1) }, 100).State,
            Is.EqualTo(Status.NeedsUnionMeasurement));
    }
    [Test]
    public void MeasuredUnionResolvesOverlappingPasses()
    {
        var passes = new[] { new Pass("name", 7), new Pass("mother", 7) };
        Assert.That(Assess(passes, 10).State, Is.EqualTo(Status.NeedsUnionMeasurement));
        Assert.That(AssessMeasuredUnion(passes, 8, 10).State, Is.EqualTo(Status.WithinBudget));
        Assert.That(AssessMeasuredUnion(passes, 11, 10).State, Is.EqualTo(Status.NeedsUnionMeasurement));
    }

    [Test]
    public void MeasuredUnionRejectsInconsistentMeasurements()
    {
        var passes = new[] { new Pass("name", 7), new Pass("mother", 5) };
        Assert.Throws<ArgumentException>((System.Action)(() => AssessMeasuredUnion(passes, 6, 10)));
        Assert.Throws<ArgumentException>((System.Action)(() => AssessMeasuredUnion(passes, 13, 20)));
    }
}

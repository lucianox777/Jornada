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
}

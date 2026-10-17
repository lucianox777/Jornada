using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework.Legacy;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PhysicalSqlCostQuantilesTests
{
    [Test]
    public void NearestRank_P95_P99_Uses_Measured_Values()
    {
        var result = V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(
            Enumerable.Range(1, 100).Reverse().Select(x => (double)x));
        ClassicAssert.That(result.SampleCount, Is.EqualTo(100));
        ClassicAssert.That(result.P95Milliseconds, Is.EqualTo(95));
        ClassicAssert.That(result.P99Milliseconds, Is.EqualTo(99));
        ClassicAssert.That(result.MaximumMilliseconds, Is.EqualTo(100));
    }

    [Test]
    public void Rejects_Missing_And_Invalid_Timings()
    {
        ClassicAssert.That(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([]),
            Throws.TypeOf<InvalidDataException>());
        ClassicAssert.That(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([double.NaN]),
            Throws.TypeOf<InvalidDataException>());
        ClassicAssert.That(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([-1d]),
            Throws.TypeOf<InvalidDataException>());
    }
}

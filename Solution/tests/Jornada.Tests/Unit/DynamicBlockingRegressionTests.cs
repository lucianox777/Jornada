using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class DynamicBlockingRegressionTests
{
    [Test]
    public void DynamicPlan_AdaptsToAvailableSourceEvidenceWithoutInventingInitials()
    {
        var withInitials = BirthBlockingPlan.Create(new DateOnly(1982, 4, 10), "Maria", "Ana", true, 1);
        var withoutInitials = BirthBlockingPlan.Create(new DateOnly(1982, 4, 10), " ", null, true, 1);
        var withInitialsMask = withInitials.Match(new DateOnly(1982, 4, 11), "Maria", "Outra");

        Assert.Multiple(() =>
        {
            Assert.That((withInitialsMask & BirthBlockingPass.MonthYearWithInitial) != 0, Is.True);
            Assert.That(withoutInitials.Match(new DateOnly(1982, 4, 11), "Maria", "Ana"),
                Is.EqualTo(BirthBlockingPass.None));
            Assert.That(withoutInitials.Match(new DateOnly(1982, 10, 4), "", ""),
                Is.EqualTo(BirthBlockingPass.TransposedDayMonth));
            Assert.That(withoutInitials.Match(new DateOnly(1983, 4, 10), "", ""),
                Is.EqualTo(BirthBlockingPass.NeighborYear));
        });
    }
}

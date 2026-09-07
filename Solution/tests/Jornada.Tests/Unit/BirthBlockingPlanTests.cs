using Jornada.Contracts;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BirthBlockingPlanTests
{
    private static BirthBlockingPlan Plan(bool v2 = true, int tolerance = 1, string name = "Maria", string mother = "Ana") =>
        BirthBlockingPlan.Create(new DateOnly(1982, 4, 10), name, mother, v2, tolerance);

    [TestCase(1982, 4, 10, 7)]
    [TestCase(1982, 4, 11, 2)]
    [TestCase(1982, 5, 10, 4)]
    [TestCase(1982, 10, 4, 8)]
    [TestCase(1983, 4, 10, 16)]
    [TestCase(1981, 4, 10, 16)]
    [TestCase(1984, 4, 10, 0)]
    [TestCase(1982, 5, 11, 0)]
    public void FivePasses_HaveExplicitMembership(int year, int month, int day, int expected)
    {
        Assert.That((int)Plan().Match(new DateOnly(year, month, day), "Maria", "Ana"), Is.EqualTo(expected));
    }

    [Test]
    public void InitialsAreComparedWithinTheirOwnFields()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Plan().Match(new DateOnly(1982, 4, 11), "Ana", "Maria"), Is.EqualTo(BirthBlockingPass.None));
            Assert.That(Plan().Match(new DateOnly(1982, 4, 11), "Maria", "Outra"), Is.EqualTo(BirthBlockingPass.MonthYearWithInitial));
            Assert.That(Plan().Match(new DateOnly(1982, 4, 11), "Outra", "Ana"), Is.EqualTo(BirthBlockingPass.MonthYearWithInitial));
            Assert.That(Plan(name: " ", mother: " ").Match(new DateOnly(1982, 4, 11), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.None));
            Assert.That(Plan(name: " ", mother: " ").Match(new DateOnly(1982, 10, 4), "", ""), Is.EqualTo(BirthBlockingPass.TransposedDayMonth));
        });
    }

    [Test]
    public void V1AndToleranceAreFrozenAndFailClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Plan(false).Match(new DateOnly(1982, 4, 10), "Outra", "Outra"), Is.EqualTo(BirthBlockingPass.ExactDate));
            Assert.That(Plan(false).Match(new DateOnly(1982, 4, 11), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.None));
            Assert.That(Plan(tolerance: 0).Match(new DateOnly(1983, 4, 10), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.None));
            Assert.That(Plan(tolerance: 2).Match(new DateOnly(1984, 4, 10), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.NeighborYear));
            Assert.That(Plan().ConfigurationFingerprint(), Is.Not.EqualTo(Plan(tolerance: 2).ConfigurationFingerprint()));
            Assert.That(Plan().ConfigurationFingerprint(), Is.EqualTo(Plan().ConfigurationFingerprint()));
            Assert.That(BirthBlockingPlan.PrimaryPass(BirthBlockingPass.ExactDate | BirthBlockingPass.DayYearWithInitial), Is.EqualTo(BirthBlockingPass.ExactDate));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(tolerance: 3));
    }

    [Test]
    public void LeapDaysAndCalendarBoundariesDoNotOverflow()
    {
        var leap = BirthBlockingPlan.Create(new DateOnly(2000, 2, 29), "Maria", "Ana", true, 2);
        Assert.Multiple(() =>
        {
            Assert.That(leap.NeighborYearDates, Is.Empty);
            Assert.That(leap.Match(new DateOnly(2001, 2, 28), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.None));
            Assert.That(leap.Match(new DateOnly(2000, 3, 29), "Maria", "Ana"), Is.EqualTo(BirthBlockingPass.DayYearWithInitial));
            Assert.That(BirthBlockingPlan.Create(DateOnly.MinValue, "M", "A", true, 2).NeighborYearDates.Count, Is.EqualTo(2));
            Assert.That(BirthBlockingPlan.Create(DateOnly.MaxValue, "M", "A", true, 2).NeighborYearDates.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void SqlUsesBoundValuesAndPreservesFieldSpecificInitials()
    {
        using var command = new NpgsqlCommand();
        var query = PostgreSqlBirthBlockingQuery.Build(command, Plan(), "g", "test");
        Assert.Multiple(() =>
        {
            Assert.That(query.Predicate, Does.Contain("g.data_nascimento"));
            Assert.That(query.Predicate, Does.Contain("g.nome_completo"));
            Assert.That(query.Predicate, Does.Contain("g.nome_mae"));
            Assert.That(query.Predicate, Does.Not.Contain("Maria"));
            Assert.That(query.PassMaskExpression, Does.Contain("CASE WHEN"));
            Assert.That(command.Parameters.Count, Is.GreaterThan(5));
            Assert.That(command.Parameters.Cast<NpgsqlParameter>().Count(x => x.Value is string), Is.EqualTo(2));
        });
        Assert.Throws<ArgumentException>(() => PostgreSqlBirthBlockingQuery.Build(command, Plan(), "g;DROP TABLE x", "test"));
        using var edge = new NpgsqlCommand();
        Assert.DoesNotThrow(() => PostgreSqlBirthBlockingQuery.Build(edge,
            BirthBlockingPlan.Create(DateOnly.MaxValue, "M", "A", true, 2)));
    }
}

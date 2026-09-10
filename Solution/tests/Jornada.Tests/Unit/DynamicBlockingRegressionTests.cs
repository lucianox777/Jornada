using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class DynamicBlockingRegressionTests
{
    [Test]
    public void SqlServerAndPostgreSql_EmitSameDynamicBlockingSemantics()
    {
        var plan = BirthBlockingPlan.Create(new DateOnly(1982, 4, 10), "Maria", "Ana", true, 1);
        using var pg = new NpgsqlCommand();
        using var sql = new SqlCommand();

        var pgQuery = PostgreSqlBirthBlockingQuery.Build(pg, plan, "g", "dyn");
        var sqlQuery = SqlServerBirthBlockingQuery.Build(sql, plan, "g", "dyn");

        Assert.Multiple(() =>
        {
            Assert.That(sqlQuery.Predicate, Is.EqualTo(pgQuery.Predicate));
            Assert.That(sqlQuery.PassMaskExpression, Is.EqualTo(pgQuery.PassMaskExpression));
            Assert.That(sql.Parameters.Count, Is.EqualTo(pg.Parameters.Count));
            Assert.That(sql.Parameters.Cast<SqlParameter>().Select(x => x.ParameterName),
                Is.EqualTo(pg.Parameters.Cast<NpgsqlParameter>().Select(x => x.ParameterName)));
            Assert.That(sql.Parameters.Cast<SqlParameter>().Select(x => x.Value),
                Is.EqualTo(pg.Parameters.Cast<NpgsqlParameter>().Select(x => x.Value)));
        });
    }

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

    [Test]
    public void CalibrationOptions_ValidateDynamicBlockingBounds()
    {
        var valid = new PostgreSqlCalibrationOptions(100, 100, 100, 0.5m, 0.95m, 0.03m, 60,
            BlockingUseComponents: true, BlockingYearTolerance: 2);
        var invalid = valid with { BlockingYearTolerance = 3 };

        Assert.DoesNotThrow(valid.Validate);
        Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
    }

    [Test]
    public void ProviderWrappers_FailClosedOnUnsafeIdentifiers()
    {
        var plan = BirthBlockingPlan.Create(new DateOnly(1982, 4, 10), "M", "A", true, 1);
        using var sql = new SqlCommand();
        using var pg = new NpgsqlCommand();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => SqlServerBirthBlockingQuery.Build(sql, plan, "g;DROP TABLE x", "dyn"));
            Assert.Throws<ArgumentException>(() => PostgreSqlBirthBlockingQuery.Build(pg, plan, "g", "dyn;bad"));
        });
    }
}

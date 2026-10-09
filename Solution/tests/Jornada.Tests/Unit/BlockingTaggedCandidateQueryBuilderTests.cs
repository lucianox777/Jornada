using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingTaggedCandidateQueryBuilderTests
{
    [Test]
    public void TaggedQuery_ReusesSharedBuilderWithSeparateDAndCSourcesAndBinding()
    {
        var d = new BlockingCandidatePassLookup("dynamic-name-year",
        [
            new BlockingCandidateClause(BlockingFeatureNames.FullName, ["MARIA SILVA"]),
            new BlockingCandidateClause(BlockingFeatureNames.BirthYear, ["1980"])
        ]);
        var c = new BlockingCandidatePassLookup("combined-exact",
        [
            new BlockingCandidateClause(BlockingFeatureNames.FullName, ["MARIA SILVA"]),
            new BlockingCandidateClause(BlockingFeatureNames.MotherFullName, ["ANA SILVA"]),
            new BlockingCandidateClause(BlockingFeatureNames.BirthYear, ["1980"]),
            new BlockingCandidateClause(BlockingFeatureNames.BirthMonth, ["01"]),
            new BlockingCandidateClause(BlockingFeatureNames.BirthDay, ["02"])
        ]);
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
            command, [d], [c],
            PersonResolutionProjectionContract.SchemaVersion,
            PersonResolutionProjectionContract.FingerprintSha256);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain(" UNION ALL "));
            Assert.That(sql, Does.Contain(" INTERSECT "));
            Assert.That(sql, Does.Contain("CAST(1 AS int) AS in_dynamic"));
            Assert.That(sql, Does.Contain("CAST(0 AS int) AS in_combined"));
            Assert.That(sql, Does.Contain("CAST(0 AS int) AS in_dynamic"));
            Assert.That(sql, Does.Contain("CAST(1 AS int) AS in_combined"));
            Assert.That(sql, Does.Contain("projection_schema_version=@blocking_projection_schema"));
            Assert.That(sql, Does.Contain("vigencia_fim IS NULL"),
                "Birth year/month/day must retain the temporal rule.");
            Assert.That(sql, Does.Not.Contain("MARIA SILVA"));
            Assert.That(sql, Does.Not.Contain("ANA SILVA"));
            Assert.That(command.Parameters.Cast<SqlParameter>()
                .Select(static p => p.Value), Does.Contain("ANA SILVA"));
            Assert.That(command.Parameters.Cast<SqlParameter>()
                .Select(static p => p.ParameterName).Distinct().Count(),
                Is.EqualTo(command.Parameters.Count));
        }));
    }

    [Test]
    public void TaggedQuery_WhenCombinedIsEmpty_KeepsDynamicProvenance()
    {
        var dynamicPass = new BlockingCandidatePassLookup("dynamic",
        [
            new BlockingCandidateClause(BlockingFeatureNames.FullName, ["JOSE SILVA"])
        ]);
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
            command, [dynamicPass], []);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain("CAST(1 AS int) AS in_dynamic"));
            Assert.That(sql, Does.Contain("CAST(0 AS int) AS in_combined"));
            Assert.That(sql, Does.Not.Contain(" UNION ALL "));
        }));
    }

    [Test]
    public void TaggedQuery_WhenNoPassIsEligible_IsFailClosedWithoutParameters()
    {
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
            command, [], []);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain("1=0"));
            Assert.That(sql, Does.Contain("in_dynamic"));
            Assert.That(sql, Does.Contain("in_combined"));
            Assert.That(command.Parameters, Is.Empty);
        }));
    }

    [Test]
    public void TaggedQuery_ParameterOverflowRejectsBothSourcesBeforeAddingParameters()
    {
        var d = new BlockingCandidatePassLookup("d",
        [
            new BlockingCandidateClause(BlockingFeatureNames.Surnames,
                ["A", "B", "C"])
        ]);
        var c = new BlockingCandidatePassLookup("c",
        [
            new BlockingCandidateClause(BlockingFeatureNames.MotherFullName,
                ["D", "E"])
        ]);
        using var command = new SqlCommand();
        Assert.That(() => BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
            command, [d], [c], maxParameters: 7),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("recusado"));
        Assert.That(command.Parameters, Is.Empty);
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests;

public sealed class BlockingProjectionCandidateQueryBuilderTests
{
    [Test]
    public void Build_UsesIntersectWithinPassAndUnionAcrossPasses()
    {
        var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
            "rs-query",
            "alg-query",
            new[]
            {
                LinkageBlockingPass.Create("name-year", new[]
                {
                    BlockingFeatureNames.FirstName,
                    BlockingFeatureNames.BirthYear
                }),
                LinkageBlockingPass.Create("mother-year", new[]
                {
                    BlockingFeatureNames.MotherFirstName,
                    BlockingFeatureNames.BirthYear
                })
            },
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(
            null,
            "NAO_INFORMADO",
            "Maria Silva",
            new DateOnly(1980, 5, 12),
            "Ana Souza");
        var passes = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);
        using var command = new SqlCommand();

        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain(" INTERSECT "));
            Assert.That(sql, Does.Contain(" UNION "));
            Assert.That(sql, Does.Not.Contain("MARIA"));
            Assert.That(sql, Does.Not.Contain("ANA"));
            Assert.That(command.Parameters.Cast<SqlParameter>().Select(static p => p.Value), Does.Contain("MARIA"));
            Assert.That(command.Parameters.Cast<SqlParameter>().Select(static p => p.Value), Does.Contain("ANA"));
        });
    }

    [Test]
    public void Build_KeepsHistoricalAliasesEligibleButStableBirthCurrentOnly()
    {
        var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
            "rs-temporal",
            "alg-temporal",
            new[]
            {
                LinkageBlockingPass.Create("surname-year", new[]
                {
                    BlockingFeatureNames.Surnames,
                    BlockingFeatureNames.BirthYear
                })
            },
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(
            null,
            "NAO_INFORMADO",
            "Maria Silva Souza",
            new DateOnly(1980, 5, 12),
            "Ana Lima");
        var passes = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);
        using var command = new SqlCommand();

        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes);

        Assert.That(Count(sql, "vigencia_fim IS NULL"), Is.EqualTo(1),
            "Somente birth_year deve exigir chave corrente; sobrenomes históricos continuam recuperáveis.");
        Assert.That(command.Parameters.Cast<SqlParameter>().Select(static p => p.Value), Does.Contain("SILVA"));
        Assert.That(command.Parameters.Cast<SqlParameter>().Select(static p => p.Value), Does.Contain("SOUZA"));
    }

    [Test]
    public void Build_EmptyPlan_IsFailClosed()
    {
        using var command = new SqlCommand();

        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
            command,
            Array.Empty<BlockingCandidatePassLookup>());

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("1=0"));
            Assert.That(command.Parameters, Is.Empty);
        });
    }

    [Test]
    public void Build_ParameterOverflow_FailsWithoutTruncatingPassOrValues()
    {
        var pass = new BlockingCandidatePassLookup(
            "many-values",
            new[]
            {
                new BlockingCandidateClause(
                    BlockingFeatureNames.Surnames,
                    new[] { "A", "B", "C" })
            });
        using var command = new SqlCommand();

        Assert.That(
            () => BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
                command,
                new[] { pass },
                maxParameters: 4),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(command.Parameters, Is.Empty,
            "Falha deve ocorrer antes de materializar consulta parcial.");
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}

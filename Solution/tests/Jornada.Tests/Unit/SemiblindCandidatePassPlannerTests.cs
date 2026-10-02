using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SemiblindCandidatePassPlannerTests
{
    private static LinkageDynamicRuleSet Rules() => LinkageDynamicRuleSet.CreateWithPasses(
        "semiblind-no-birth", "alg-test",
        [LinkageBlockingPass.Create("dynamic-full-name", [BlockingFeatureNames.FullName])],
        Array.Empty<KeyValuePair<string, decimal>>());

    [Test]
    public void Missing_birth_keeps_dynamic_pass_without_combined_blocking()
    {
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria Silva", null, "Ana Souza");
        var passes = SemiblindCandidatePassPlanner.Plan(Rules(), observation);
        Assert.That(passes.Select(pass => pass.PassId), Is.EqualTo(new[] { "dynamic-full-name" }));
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes);
        Assert.That(sql, Does.Not.Contain("1=0"));
        Assert.That(command.Parameters.Cast<SqlParameter>().Any(p => Equals(p.Value, "MARIA SILVA")), Is.True);
    }

    [Test]
    public void Birth_and_mother_union_dynamic_and_combined_passes()
    {
        var observation = new IdentityObservation(null, "NAO_INFORMADO",
            "Maria Silva", new DateOnly(1980, 1, 7), "Ana Souza");
        var passes = SemiblindCandidatePassPlanner.Plan(Rules(), observation);
        Assert.That(passes.Select(pass => pass.PassId), Does.Contain("dynamic-full-name"));
        Assert.That(passes.Select(pass => pass.PassId), Does.Contain("combined-exact"));
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes);
        Assert.That(sql, Does.Contain(" UNION "));
    }

    [Test]
    public void Missing_person_name_keeps_eligible_mother_and_birth_pass()
    {
        var rules = LinkageDynamicRuleSet.CreateWithPasses("mother-year", "alg-test",
            [LinkageBlockingPass.Create("mother-year",
                [BlockingFeatureNames.MotherFirstName, BlockingFeatureNames.BirthYear])],
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(null, "NAO_INFORMADO",
            null, new DateOnly(1975, 2, 11), "Maria da Anunciacao dos Anjos");

        var passes = SemiblindCandidatePassPlanner.Plan(rules, observation);

        Assert.That(passes.Select(pass => pass.PassId), Is.EqualTo(new[] { "mother-year" }));
    }

    [Test]
    public void Without_eligible_dynamic_pass_or_birth_query_is_empty()
    {
        var onlyYear = LinkageDynamicRuleSet.CreateWithPasses("year-only", "alg-test",
            [LinkageBlockingPass.Create("year", [BlockingFeatureNames.BirthYear])],
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria Silva", null, null);
        var passes = SemiblindCandidatePassPlanner.Plan(onlyYear, observation);
        Assert.That(passes, Is.Empty);
        using var command = new SqlCommand();
        Assert.That(BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes),
            Does.Contain("1=0"));
    }
}

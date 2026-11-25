using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9DevE2EFirstFullMembershipOracleTests
{
    [Test]
    public void Independent_odds_publish_posterior_and_log_odds_at_frozen_precision()
    {
        var score = V9DevE2EScalarOracle.EvaluateOdds(3d);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(score.Posterior, Is.EqualTo(.75m));
            Assert.That(score.LogOdds, Is.EqualTo(1.09861229m));
            Assert.That(V9DevE2EScalarOracle.EvaluateOdds(1d).Posterior, Is.EqualTo(.5m));
            Assert.Throws<InvalidDataException>(() => V9DevE2EScalarOracle.EvaluateOdds(0d));
        }));
    }

    [Test]
    public void Pass_intersection_and_union_are_not_single_feature_or_missing_wildcards()
    {
        LinkageBlockingPass[] passes =
        [
            new("one", ["birth_year", "name_first"]),
            new("two", ["name_last", "mother_name_last"])
        ];
        HashSet<BlockingProjectionKey> source =
        [
            new("birth_year", "1980"), new("name_first", "MARIA"),
            new("name_last", "SILVA"), new("mother_name_last", "COSTA")
        ];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9DevE2EFirstFullMembershipOracle.MatchesAnyPass(source,
                new HashSet<BlockingProjectionKey> { new("birth_year", "1980") }, passes), Is.False);
            Assert.That(V9DevE2EFirstFullMembershipOracle.MatchesAnyPass(source,
                new HashSet<BlockingProjectionKey> { new("birth_year", "1980"), new("name_first", "MARIA") }, passes), Is.True);
            Assert.That(V9DevE2EFirstFullMembershipOracle.MatchesAnyPass(source,
                new HashSet<BlockingProjectionKey> { new("name_last", "SILVA"), new("mother_name_last", "COSTA") }, passes), Is.True);
            Assert.That(V9DevE2EFirstFullMembershipOracle.MatchesAnyPass(
                new HashSet<BlockingProjectionKey> { new("name_last", "SILVA") },
                new HashSet<BlockingProjectionKey> { new("name_last", "SILVA"), new("mother_name_last", "COSTA") }, passes), Is.False);
        }));
    }
}

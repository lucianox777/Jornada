using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9DevE2EFirstFullMembershipOracleTests
{
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

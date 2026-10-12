using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalBlockingAuditV9Tests
{
    private static readonly DateOnly Birth = new(1988, 4, 22);

    private static IdentityTrainingPair Pair(
        string left, string right, string? motherLeft = null, string? motherRight = null)
        => new(left, Birth, motherLeft, right, Birth, motherRight);

    private static IReadOnlyList<IdentityTrainingPair> Matches() =>
    [
        Pair("Maria Aparecida da Silva", "Maria Aparecida da Silva", "Ana Costa", "ANA COSTA"),
        Pair("José da Silva", "José de Silva", "Maria Souza", "MARIA SOUZA")
    ];

    private static BlockingConditionedUnmatchedPairSample NonMatches(bool mothers = true) => new(
        [
            Pair("Maria Silva", "Maria Silvaa",
                mothers ? "Maria Costa" : null, mothers ? "Ana Costa" : null),
            Pair("João Silva", "Ana Almeida",
                mothers ? "Rita Cunha" : null, mothers ? "Carolina Mendes" : null)
        ],
        new Dictionary<string, long>(),
        CandidatePoolSize: 17,
        PassNominalSupport: Array.Empty<BlockingPassNominalUSupport>());

    private static readonly LinkageBlockingPass[] Passes =
    [
        LinkageBlockingPass.Create("b", ["birth_year", "name_first"]),
        LinkageBlockingPass.Create("a", ["mother_name_first"])
    ];

    private static CompositeNominalBlockingAuditV9 Build(
        BlockingConditionedUnmatchedPairSample? unmatched = null,
        IReadOnlyCollection<LinkageBlockingPass>? passes = null,
        IReadOnlyList<IdentityTrainingPair>? matched = null) =>
        CompositeNominalBlockingAuditBuilderV9.Build(
            matched ?? Matches(), unmatched ?? NonMatches(), passes ?? Passes,
            IdentityComparison.NormalizationVersion,
            "SCHEMA_FIXTURE_V1", new string('a', 64), 1m);

    [Test]
    public void UsesActualBlockedUAndExplicitlyRejectsUnverifiedMConditioning()
    {
        var audit = Build();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Available, Is.True);
            Assert.That(audit.Status, Is.EqualTo("DIAGNOSTIC_ONLY_M_NOT_BLOCK_CONDITIONED"));
            Assert.That(audit.CandidatePoolSize, Is.EqualTo(17));
            Assert.That(audit.MatchedInputCount, Is.EqualTo(2));
            Assert.That(audit.UnmatchedSampleCount, Is.EqualTo(2));
            Assert.That(audit.BlockingConfigurationSha256, Has.Length.EqualTo(64));
            Assert.That(audit.Attributes, Has.Count.EqualTo(2));
            Assert.That(audit.Attributes.Single(x => x.Attribute == "NOME_V9")
                .MatchedSupport[CompositeNominalLevelV9.ExactFull], Is.EqualTo(1));
        }));
    }

    [Test]
    public void CanonicalBlockingConfigurationIsOrderIndependentButVersionSensitive()
    {
        var audit = Build();
        var shuffled = Build(passes: Passes.Reverse().ToArray());
        var altered = Build(passes: [
            LinkageBlockingPass.Create("a", ["mother_name_first"]),
            LinkageBlockingPass.Create("b", ["birth_year"])
        ]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(shuffled.BlockingConfigurationSha256,
                Is.EqualTo(audit.BlockingConfigurationSha256));
            Assert.That(altered.BlockingConfigurationSha256,
                Is.Not.EqualTo(audit.BlockingConfigurationSha256));
        }));
    }

    [Test]
    public void NoMaternalObservedUProducesUnavailableNotFabricatedDistribution()
    {
        var audit = Build(unmatched: NonMatches(mothers: false));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Available, Is.False);
            Assert.That(audit.Status, Is.EqualTo(
                CompositeNominalBlockingAuditBuilderV9.MissingSupportStatus));
            Assert.That(audit.Attributes, Is.Empty);
            Assert.That(audit.UnmatchedSampleCount, Is.EqualTo(2));
        }));
    }

    [Test]
    public void RejectsPoolInconsistencyAndDuplicatedPassIds()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => Build(unmatched: NonMatches() with
            {
                CandidatePoolSize = 1
            })), Throws.InvalidOperationException);
            Assert.That((Action)(() => Build(passes: [
                LinkageBlockingPass.Create("x", ["birth_year"]),
                LinkageBlockingPass.Create("x", ["name_first"])
            ])), Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void ReturnedDiagnosticDoesNotContainRawNamesOrCandidateIdentifiers()
    {
        var audit = Build();
        var published = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(published, Does.Not.Contain("APARECIDA"));
            Assert.That(published, Does.Not.Contain("SILVA"));
            Assert.That(published, Does.Not.Contain("MARIA"));
            Assert.That(published, Does.Not.Contain("JOAO"));
            Assert.That(published, Does.Not.Contain("João"));
            Assert.That(published, Does.Not.Contain("MatchedPairs"));
            Assert.That(published, Does.Not.Contain("UnmatchedPairs"));
        }));
    }
}

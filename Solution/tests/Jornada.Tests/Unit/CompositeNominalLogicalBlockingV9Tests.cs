using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalLogicalBlockingV9Tests
{
    private static readonly DateOnly Birth = new(1985, 7, 3);

    private static IdentityTrainingPair Pair(
        string left, string right, string? motherLeft = null, string? motherRight = null)
        => new(left, Birth, motherLeft, right, Birth, motherRight);

    private static readonly LinkageBlockingPass[] FirstNamePass =
    [
        LinkageBlockingPass.Create("FIRST", [BlockingCandidateFeatureCatalog.FirstName])
    ];

    private static BlockingConditionedUnmatchedPairSample U() => new(
        [
            Pair("Maria Silva", "Maria Silveira", "Ana Costa", "Ana Almeida"),
            Pair("José Santos", "Paulo Santos", "Maria Cunha", "Beatriz Cunha")
        ],
        new Dictionary<string, long>(),
        CandidatePoolSize: 20,
        PassNominalSupport: Array.Empty<BlockingPassNominalUSupport>());

    private static CompositeNominalBlockingAuditV9 Build(
        IReadOnlyList<IdentityTrainingPair> matches,
        IReadOnlyCollection<LinkageBlockingPass>? passes = null) =>
        CompositeNominalBlockingAuditBuilderV9.Build(
            matches, U(), passes ?? FirstNamePass,
            IdentityComparison.NormalizationVersion, "PROJECTION_V9_TEST",
            new string('b', 64), 0.5m,
            conditionMatchedPairsOnLogicalBlocking: true);

    [Test]
    public void MIsFilteredUsingSelectedANDWithinPassORBetweenPasses()
    {
        var pairs = new[]
        {
            Pair("Maria Aparecida Silva", "Maria Aparecida Silva", "Ana Souza", "Ana Souza"),
            Pair("José da Costa", "Paulo Almeida", "Maria Souza", "Maria Souza")
        };

        var audit = Build(pairs);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Status, Is.EqualTo(
                CompositeNominalBlockingAuditBuilderV9.LogicalConditioningStatus));
            Assert.That(audit.MatchedInputCount, Is.EqualTo(2));
            Assert.That(audit.MatchedLogicallyRetained, Is.EqualTo(1));
            Assert.That(audit.UnmatchedSampleCount, Is.EqualTo(2));
            Assert.That(audit.Attributes.Single(a => a.Attribute == "NOME_V9")
                .MatchedObserved, Is.EqualTo(1));
            Assert.That(audit.Attributes.Single(a => a.Attribute == "NOME_V9")
                .MatchedSupport[CompositeNominalLevelV9.ExactFull], Is.EqualTo(1));
            Assert.That(audit.Available, Is.True);
        }));
    }

    [Test]
    public void MissingOrDisagreeingFieldsCannotMakeAPassMatch()
    {
        var pairs = new[]
        {
            Pair("Maria Santos", "Paulo Costa", "Ana Silva", "Maria Almeida")
        };
        var audit = Build(pairs);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Status, Is.EqualTo(
                CompositeNominalBlockingAuditBuilderV9.MissingMatchedSupportStatus));
            Assert.That(audit.Attributes, Is.Empty);
            Assert.That(audit.MatchedInputCount, Is.EqualTo(1));
            Assert.That(audit.MatchedLogicallyRetained, Is.Zero);
            Assert.That(audit.Available, Is.False);
        }));
    }

    [Test]
    public void CompositeANDRequiresEveryKeyWhileOtherPassMayRecover()
    {
        var pairs = new[]
        {
            Pair("Maria Silva", "MARIA Silva", "Ana Costa", "Paula Costa"),
            Pair("José da Silva", "Paulo da Silva", "Maria Souza", "MARIA SOUZA")
        };
        var passes =
            new[]
            {
                LinkageBlockingPass.Create("FIRST_AND_MOTHER",
                    [BlockingCandidateFeatureCatalog.FirstName,
                     BlockingCandidateFeatureCatalog.MotherFirstName]),
                LinkageBlockingPass.Create("MOTHER",
                    [BlockingCandidateFeatureCatalog.MotherFirstName])
            };
        var audit = Build(pairs, passes);
        // First is retained by FIRST_AND_MOTHER? No (mother first disagrees).
        // Second is retained by MOTHER, despite different first names.
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.MatchedInputCount, Is.EqualTo(2));
            Assert.That(audit.MatchedLogicallyRetained, Is.EqualTo(1));
            Assert.That(audit.Attributes.Single(a => a.Attribute == "NOME_V9")
                .MatchedObserved, Is.EqualTo(1));
        }));
    }

    [Test]
    public void UnknownBlockingFeatureIsRejectedRatherThanSilentlyExcluded()
    {
        Assert.That((Action)(() => Build(
            [Pair("Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa")],
            [LinkageBlockingPass.Create("UNKNOWN", ["NOT_CANONICAL_FEATURE"])])),
            Throws.InvalidOperationException.With.Message.Contains("Feature V9"));
    }

    [Test]
    public void PositiveAuditExposesNoNamesAndExplicitlyDeniesSqlMembershipCertification()
    {
        var audit = Build(
            [Pair("Maria Aparecida Silva", "MARIA APARECIDA SILVA", "Ana Costa", "Ana Costa")]);
        var json = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Status, Does.Contain("LOGICAL_RULESET"));
            Assert.That(audit.Status, Does.Not.Contain("CERTIFIED"));
            Assert.That(json, Does.Not.Contain("APARECIDA"));
            Assert.That(json, Does.Not.Contain("SILVA"));
            Assert.That(json, Does.Not.Contain("MARIA APARECIDA"));
            Assert.That(json, Does.Not.Contain("Pairs"));
        }));
    }
}

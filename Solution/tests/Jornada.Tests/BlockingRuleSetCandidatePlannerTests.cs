using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests;

public sealed class BlockingRuleSetCandidatePlannerTests
{
    [Test]
    public void LegacyRuleSet_IsExposedAsSingleLegacyPass()
    {
        var ruleSet = LinkageDynamicRuleSet.Create(
            "rs-legacy",
            "alg-1",
            new[] { BlockingFeatureNames.FirstName, BlockingFeatureNames.BirthYear },
            Array.Empty<KeyValuePair<string, decimal>>());

        Assert.Multiple(() =>
        {
            Assert.That(ruleSet.BlockingPasses, Is.Empty);
            Assert.That(ruleSet.EffectiveBlockingPasses, Has.Count.EqualTo(1));
            Assert.That(ruleSet.EffectiveBlockingPasses[0].PassId, Is.EqualTo("legacy"));
        });
    }

    [Test]
    public void MultiPassRuleSet_PreservesGroupingAndChangesFingerprint()
    {
        var first = LinkageDynamicRuleSet.CreateWithPasses(
            "rs-2",
            "alg-2",
            new[]
            {
                LinkageBlockingPass.Create("exact-name-year", new[]
                {
                    BlockingFeatureNames.FullName,
                    BlockingFeatureNames.BirthYear
                }),
                LinkageBlockingPass.Create("mother-year", new[]
                {
                    BlockingFeatureNames.MotherFirstName,
                    BlockingFeatureNames.BirthYear
                })
            },
            Array.Empty<KeyValuePair<string, decimal>>());

        var second = LinkageDynamicRuleSet.CreateWithPasses(
            "rs-2",
            "alg-2",
            new[]
            {
                LinkageBlockingPass.Create("mixed-a", new[]
                {
                    BlockingFeatureNames.FullName,
                    BlockingFeatureNames.MotherFirstName
                }),
                LinkageBlockingPass.Create("mixed-b", new[]
                {
                    BlockingFeatureNames.BirthYear
                })
            },
            Array.Empty<KeyValuePair<string, decimal>>());

        Assert.Multiple(() =>
        {
            Assert.That(first.BlockingPasses, Has.Count.EqualTo(2));
            Assert.That(first.BlockingFields, Is.EquivalentTo(second.BlockingFields));
            Assert.That(first.FingerprintSha256, Is.Not.EqualTo(second.FingerprintSha256));
        });
    }

    [Test]
    public void Planner_UsesAndAcrossFeaturesAndKeepsSurnameValuesTogether()
    {
        var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
            "rs-3",
            "alg-3",
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
            Cpf: null,
            NomeCompleto: "Maria da Silva Souza",
            DataNascimento: new DateOnly(1980, 5, 12),
            NomeMae: "Ana Lima");

        var planned = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);

        Assert.That(planned, Has.Count.EqualTo(1));
        var clauses = planned[0].Clauses.ToDictionary(static x => x.Feature, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(clauses[BlockingFeatureNames.BirthYear].Values, Is.EqualTo(new[] { "1980" }));
            Assert.That(clauses[BlockingFeatureNames.Surnames].Values, Is.EqualTo(new[] { "DA", "SILVA", "SOUZA" }));
        });
    }

    [Test]
    public void Planner_RejectsCpfBecauseCpfUsesDeterministicRoute()
    {
        var ruleSet = LinkageDynamicRuleSet.Create(
            "rs-4",
            "alg-4",
            new[] { BlockingFeatureNames.BirthYear },
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(
            Cpf: "12345678909",
            NomeCompleto: "Maria Silva",
            DataNascimento: new DateOnly(1980, 5, 12),
            NomeMae: "Ana Lima");

        Assert.That(
            () => BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Planner_RejectsUnknownFeatureInsteadOfReinterpretingRuleSet()
    {
        var ruleSet = LinkageDynamicRuleSet.Create(
            "rs-5",
            "alg-5",
            new[] { "unknown_feature" },
            Array.Empty<KeyValuePair<string, decimal>>());
        var observation = new IdentityObservation(
            Cpf: null,
            NomeCompleto: "Maria Silva",
            DataNascimento: new DateOnly(1980, 5, 12),
            NomeMae: "Ana Lima");

        Assert.That(
            () => BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalFrequencyV9Tests
{
    private static readonly DateOnly Birth = new(1990, 6, 20);

    private static IdentityTrainingPair Pair(
        string left, string right, string? leftMother = null, string? rightMother = null) =>
        new(left, Birth, leftMother, right, Birth, rightMother);

    private static IReadOnlyList<IdentityTrainingPair> CandidatePairs() =>
    [
        Pair("Maria Aparecida da Silva", "Maria Cecilia da Silva",
            "Ana Souza", "ANA SOUZA"),
        Pair("Maria Silva", "MARIA SILVA",
            null, "Ana Costa"),
        Pair("Ana", "Ana Costa")
    ];

    private static NominalFrequencyReferenceContractV9 Reference(
        NominalFrequencyUnitV9 unit,
        NominalFrequencySourceV9 source = NominalFrequencySourceV9.IndependentlyObservedPopulation,
        bool approved = true,
        long support = 1000) =>
        new(unit, source, "REFERENCE_POPULATION_FRAME", "BLOCKING_RULESET_FRAME",
            support, approved);

    [Test]
    public void MapsOnlyExactNominalLevelsToTheirOwnFrequencyUnits()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(CompositeNominalTermFrequencyContractV9.RequiredUnit(
                CompositeNominalLevelV9.ExactFull), Is.EqualTo(NominalFrequencyUnitV9.FullName));
            Assert.That(CompositeNominalTermFrequencyContractV9.RequiredUnit(
                CompositeNominalLevelV9.ExactFirstLast), Is.EqualTo(NominalFrequencyUnitV9.FirstLastJoint));
            Assert.That(CompositeNominalTermFrequencyContractV9.RequiredUnit(
                CompositeNominalLevelV9.ExactFirst), Is.EqualTo(NominalFrequencyUnitV9.FirstName));
            Assert.That(CompositeNominalTermFrequencyContractV9.RequiredUnit(
                CompositeNominalLevelV9.ExactLast), Is.EqualTo(NominalFrequencyUnitV9.LastContentSurname));
        }));
        foreach (var level in new[]
        {
            CompositeNominalLevelV9.Missing,
            CompositeNominalLevelV9.Other,
            CompositeNominalLevelV9.HighFull,
            CompositeNominalLevelV9.HighFirstLast,
            CompositeNominalLevelV9.ReversedFirstLast
        })
            Assert.That(CompositeNominalTermFrequencyContractV9.RequiredUnit(level),
                Is.Null, "Level: " + level);
    }

    [Test]
    public void RejectsV8FirstNameMarginalAppliedToFullNameOrJointExact()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    Reference(NominalFrequencyUnitV9.FirstName), 100)),
                Throws.InvalidOperationException.With.Message.Contains("exige unidade"));
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    Reference(NominalFrequencyUnitV9.FullName,
                        NominalFrequencySourceV9.IbgePublishedMarginal), 100)),
                Throws.InvalidOperationException.With.Message.Contains("Marginais"));
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFirstLast,
                    Reference(NominalFrequencyUnitV9.FirstLastJoint,
                        NominalFrequencySourceV9.IbgePublishedMarginal), 100)),
                Throws.InvalidOperationException.With.Message.Contains("Marginais"));
        }));
    }

    [Test]
    public void FailClosedWhenEvidenceIsNotIndependentlyApprovedOrSupported()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    Reference(NominalFrequencyUnitV9.FullName, approved: false), 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    Reference(NominalFrequencyUnitV9.FullName, support: 10), 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.HighFull,
                    Reference(NominalFrequencyUnitV9.FullName), 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    Reference(NominalFrequencyUnitV9.FullName) with
                    {
                        BlockingFrameFingerprint = ""
                    }, 100)),
                Throws.InvalidOperationException);
        }));
        Assert.DoesNotThrow(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactFull,
                Reference(NominalFrequencyUnitV9.FullName), 100));
    }

    [Test]
    public void EmpiricalEndpointUnitsNeverConfuseFullNameWithFirstLastJoint()
    {
        var audit = CompositeNominalEndpointFrequencyAuditBuilderV9.Analyze(CandidatePairs());
        var full = audit.Units.Single(x =>
            x.Attribute == "NOME_V9" && x.Unit == NominalFrequencyUnitV9.FullName);
        var joint = audit.Units.Single(x =>
            x.Attribute == "NOME_V9" && x.Unit == NominalFrequencyUnitV9.FirstLastJoint);
        var first = audit.Units.Single(x =>
            x.Attribute == "NOME_V9" && x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Status, Is.EqualTo(
                CompositeNominalEndpointFrequencyAuditBuilderV9.Status));
            Assert.That(audit.Units, Has.Count.EqualTo(8));
            Assert.That(full.CandidatePairs, Is.EqualTo(3));
            Assert.That(full.ObservedEndpoints, Is.EqualTo(6));
            Assert.That(full.DistinctObservedValues, Is.EqualTo(5));
            Assert.That(full.MaximumEndpointAppearances, Is.EqualTo(2));
            Assert.That(full.PairUnitAgreements, Is.EqualTo(1));
            Assert.That(joint.ObservedEndpoints, Is.EqualTo(5));
            Assert.That(joint.MissingEndpoints, Is.EqualTo(1));
            Assert.That(joint.DistinctObservedValues, Is.EqualTo(2));
            Assert.That(joint.MaximumEndpointAppearances, Is.EqualTo(4));
            Assert.That(joint.PairUnitAgreements, Is.EqualTo(2));
            Assert.That(first.PairUnitAgreements, Is.EqualTo(3));
        }));
    }

    [Test]
    public void MotherMissingIsExplicitAndNoNameOrRawFrequencyKeyLeavesDTO()
    {
        var audit = CompositeNominalEndpointFrequencyAuditBuilderV9.Analyze(CandidatePairs());
        var mother = audit.Units.Single(x =>
            x.Attribute == "NOME_MAE_V9" && x.Unit == NominalFrequencyUnitV9.FullName);
        var json = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(mother.ObservedEndpoints, Is.EqualTo(3));
            Assert.That(mother.MissingEndpoints, Is.EqualTo(3));
            Assert.That(mother.PairUnitAgreements, Is.EqualTo(1));
            Assert.That(json, Does.Not.Contain("APARECIDA"));
            Assert.That(json, Does.Not.Contain("CECILIA"));
            Assert.That(json, Does.Not.Contain("SOUZA"));
            Assert.That(json, Does.Not.Contain("MARIA SILVA"));
            Assert.That(json, Does.Not.Contain("Histograms"));
        }));
        Assert.That((Action)(() =>
            CompositeNominalEndpointFrequencyAuditBuilderV9.Analyze(
                Array.Empty<IdentityTrainingPair>())),
            Throws.InvalidOperationException);
    }

    [Test]
    public void AgnomeAndIntermediateNameArePreservedInFullNameButNotJointUnit()
    {
        var pairs = new[]
        {
            Pair("Joao Antonio Silva Filho", "Joao Pedro Silva Neto")
        };
        var audit = CompositeNominalEndpointFrequencyAuditBuilderV9.Analyze(pairs);
        var full = audit.Units.Single(x =>
            x.Attribute == "NOME_V9" && x.Unit == NominalFrequencyUnitV9.FullName);
        var joint = audit.Units.Single(x =>
            x.Attribute == "NOME_V9" && x.Unit == NominalFrequencyUnitV9.FirstLastJoint);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(full.PairUnitAgreements, Is.Zero);
            Assert.That(full.DistinctObservedValues, Is.EqualTo(2));
            Assert.That(joint.PairUnitAgreements, Is.EqualTo(1));
            Assert.That(joint.DistinctObservedValues, Is.EqualTo(1));
        }));
    }
}

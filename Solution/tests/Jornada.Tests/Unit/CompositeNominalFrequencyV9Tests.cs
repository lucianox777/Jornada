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

    private static NominalSyntheticIbgeBootstrapV9 Bootstrap() =>
        new(
            CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
            CompositeNominalTermFrequencyContractV9.CanonicalBootstrapPeople,
            "SYNTHETIC_CORPUS_30000_SHA256",
            "CORPUS_GENERATOR_VERSION_20261012",
            42UL,
            "SYNTHETIC_HYPOTHESES_FINGERPRINT");

    private static NominalFrequencyReferenceContractV9 IbgeBootstrap(
        NominalFrequencyUnitV9 unit,
        NominalFrequencySourceV9 source) =>
        Reference(unit, source, approved: false, support: 0)
            with { SyntheticBootstrap = Bootstrap() };

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
    public void AllowsCanonicalIbgeSurnameMarginalOnlyAsExplicitLastSurnameProxy()
    {
        // DC-LK-TF line 95 and Calibrador FS section 10 authorize an
        // INITIAL proxy, not a measured last-position frequency.
        var proxy = IbgeBootstrap(
            NominalFrequencyUnitV9.LastContentSurname,
            NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy);

        Assert.That(CompositeNominalTermFrequencyContractV9.Version,
            Is.EqualTo("V9_NOMINAL_TF_UNIT_GUARD_V3"));
        Assert.DoesNotThrow((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactLast, proxy, 100)));
        Assert.Multiple((Action)(() =>
        {
            // An unqualified legacy marginal must NOT silently masquerade as
            // measured LAST surname. The proxy provenance is mandatory.
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast,
                    proxy with { Source = NominalFrequencySourceV9.IbgePublishedMarginal }, 100)),
                Throws.InvalidOperationException.With.Message.Contains("proxy explícita"));
            // A proxy is not a full-name or observed first+last joint rate.
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    proxy with { Unit = NominalFrequencyUnitV9.FullName }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFirstLast,
                    proxy with { Unit = NominalFrequencyUnitV9.FirstLastJoint }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFirst,
                    proxy with { Unit = NominalFrequencyUnitV9.FirstName }, 100)),
                Throws.InvalidOperationException);
            // Synthetic bootstrap is explicitly NOT a real-observed sample:
            // an artificial population approval or observed support is invalid.
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast,
                    proxy with { IndependentPopulationValidationApproved = true }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast,
                    proxy with { IndependentObservedSupport = 99 }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast,
                    proxy with { PopulationFrameFingerprint = "" }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast,
                    proxy with { BlockingFrameFingerprint = "" }, 100)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void SyntheticIbgeFirstNameBootstrapNeedsNoObservedSupportOrPopulationCertificate()
    {
        var ibge = IbgeBootstrap(
            NominalFrequencyUnitV9.FirstName,
            NominalFrequencySourceV9.IbgePublishedMarginal);
        Assert.DoesNotThrow((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactFirst, ibge,
                minimumIndependentSupport: 100_000)));

        // A generic IBGE prenome cannot be used as last surname, even in bootstrap.
        Assert.That((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactLast,
                ibge with { Unit = NominalFrequencyUnitV9.LastContentSurname }, 1)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void SyntheticBootstrapFailsClosedWithoutCanonicalCorpusProvenance()
    {
        var ibge = IbgeBootstrap(
            NominalFrequencyUnitV9.LastContentSurname,
            NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy);
        var bad = new[]
        {
            ibge with { SyntheticBootstrap = null },
            ibge with { SyntheticBootstrap = Bootstrap() with { People = 29_999 } },
            ibge with { SyntheticBootstrap = Bootstrap() with { People = 30_001 } },
            ibge with { SyntheticBootstrap = Bootstrap() with { Provenance = "REAL_OBSERVADO" } },
            ibge with { SyntheticBootstrap = Bootstrap() with { CorpusFingerprint = "" } },
            ibge with { SyntheticBootstrap = Bootstrap() with { GeneratorVersion = "" } },
            ibge with { SyntheticBootstrap = Bootstrap() with { GenerationHypothesesFingerprint = "" } },
            ibge with { BlockingFrameFingerprint = "" },
            ibge with { PopulationFrameFingerprint = "" },
            ibge with { IndependentObservedSupport = 2 },
            ibge with { IndependentPopulationValidationApproved = true }
        };
        foreach (var input in bad)
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactLast, input, 100)),
                Throws.InvalidOperationException, "Bootstrap inválido deve ser rejeitado.");

        Assert.That((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactFull,
                ibge with { Unit = NominalFrequencyUnitV9.FullName }, 100)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void RealObservedPopulationStillRequiresIndependentValidationAndObservedSupport()
    {
        var observed = Reference(NominalFrequencyUnitV9.FullName);
        Assert.DoesNotThrow((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactFull, observed, 100)));
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    observed with { SyntheticBootstrap = Bootstrap() }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    observed with { IndependentObservedSupport = 99 }, 100)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                    CompositeNominalLevelV9.ExactFull,
                    observed with { IndependentPopulationValidationApproved = false }, 100)),
                Throws.InvalidOperationException);
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
        Assert.DoesNotThrow((Action)(() =>
            CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
                CompositeNominalLevelV9.ExactFull,
                Reference(NominalFrequencyUnitV9.FullName), 100)));
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

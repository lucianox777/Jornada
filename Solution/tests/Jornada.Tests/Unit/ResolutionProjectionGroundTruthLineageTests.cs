using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ResolutionProjectionGroundTruthLineageTests
{
    [Test]
    public void BlockingCandidatesUseDeclaredSourceAttributeAsLineage()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField(
                    "nome_cidadao",
                    ResolutionAttributeSemantic.PersonName,
                    EligibleForResolution: true)
            },
            "TEST_V1");

        var lineage = ResolutionProjectionGroundTruthLineage.BlockingCandidates(plan);
        var normalized = lineage.Single(item => item.FeatureName == "nome_cidadao__normalized");

        Assert.That(normalized.Sources.Select(static source => source.CanonicalAttribute),
            Is.EquivalentTo(new[] { "nome_cidadao" }));
    }

    [Test]
    public void OpaqueFeatureDerivedFromCnsIsRejectedWithoutNameInference()
    {
        var plan = new ResolutionProjectionPlan(
            "TEST_V1",
            "TEST_CATALOG",
            new[]
            {
                new ResolutionSourceField("cns", ResolutionAttributeSemantic.Text, EligibleForResolution: true)
            },
            new[]
            {
                new ResolutionProjectedFeature(
                    "identificador_hash",
                    "cns",
                    ResolutionAttributeSemantic.Text,
                    ResolutionFeatureOrigin.Calculated,
                    "TEST_MODEL@V1",
                    "HASH@V1",
                    "hash",
                    ResolutionMaterializationKind.ProcessorMaterialized,
                    false,
                    true)
            },
            "test-fingerprint");

        Assert.Throws<InvalidOperationException>(() =>
            ResolutionProjectionGroundTruthLineage.EnsureBlockingCandidatesDoNotLeak(
                plan,
                GroundTruthSource.Cns));
    }

    [Test]
    public void CurrentPersonProjectionDoesNotLeakCpfOrCnsIntoBlocking()
    {
        var plan = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() =>
                ResolutionProjectionGroundTruthLineage.EnsureBlockingCandidatesDoNotLeak(
                    plan,
                    GroundTruthSource.Cpf));
            Assert.DoesNotThrow(() =>
                ResolutionProjectionGroundTruthLineage.EnsureBlockingCandidatesDoNotLeak(
                    plan,
                    GroundTruthSource.Cns));
        });
    }
}

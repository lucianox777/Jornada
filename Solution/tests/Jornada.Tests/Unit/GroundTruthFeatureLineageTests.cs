using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthFeatureLineageTests
{
    [Test]
    public void CnsDerivedFeatureIsRejectedEvenWhenFeatureNameDoesNotContainCns()
    {
        var features = new[]
        {
            GroundTruthFeatureLineage.Direct("IDENTIFICADOR_HASH", "CNS")
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(GroundTruthSource.Cns, features));

        Assert.That(ex!.Message, Does.Contain("IDENTIFICADOR_HASH"));
    }

    [Test]
    public void CpfDerivedFeatureIsRejectedEvenWhenFeatureNameDoesNotContainCpf()
    {
        var features = new[]
        {
            GroundTruthFeatureLineage.Direct("IDENTIDADE_BUCKET", "CPF")
        };

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(GroundTruthSource.Cpf, features));
    }

    [Test]
    public void CanonicalAliasesAreTypedAsGroundTruthSources()
    {
        var cpf = GroundTruthFeatureLineage.Direct("CPF_BUCKET", "cpf-declarado");
        var cns = GroundTruthFeatureLineage.Direct("CNS_BUCKET", "identificador:cns");

        Assert.Multiple(() =>
        {
            Assert.That(cpf.Sources.Single().CanonicalAttribute, Is.EqualTo("cpf_declarado"));
            Assert.That(cpf.Sources.Single().LabelSource, Is.EqualTo(GroundTruthSource.Cpf));
            Assert.That(cns.Sources.Single().CanonicalAttribute, Is.EqualTo("identificador_cns"));
            Assert.That(cns.Sources.Single().LabelSource, Is.EqualTo(GroundTruthSource.Cns));
        });
    }

    [Test]
    public void IndependentFeaturesAreAllowed()
    {
        var features = new[]
        {
            GroundTruthFeatureLineage.Direct("NOME_FONETICO", "NOME_COMPLETO"),
            GroundTruthFeatureLineage.Direct("ANO_NASCIMENTO", "DATA_NASCIMENTO")
        };

        Assert.DoesNotThrow(() =>
            GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(GroundTruthSource.Cns, features));
    }

    [Test]
    public void MultipleSourcesAreCheckedTransitively()
    {
        var features = new[]
        {
            GroundTruthFeatureLineage.Direct("FEATURE_COMPOSTA", "NOME_COMPLETO", "CNS", "DATA_NASCIMENTO")
        };

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(GroundTruthSource.Cns, features));
    }

    [Test]
    public void DirectRejectsMissingProvenance()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => GroundTruthFeatureLineage.Direct("FEATURE_SEM_ORIGEM"));
            Assert.Throws<ArgumentException>(() => GroundTruthFeatureLineage.Direct("FEATURE_SEM_ORIGEM", " "));
            Assert.Throws<ArgumentException>(() => GroundTruthFeatureLineage.Direct(" ", "NOME_COMPLETO"));
        });
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class ResolutionComparatorCatalogTests
{
    [Test]
    public void Catalog_ContainsUniversalExactAndJaroWinklerComparators()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HomologatedResolutionComparatorCatalog.All.Select(static x => x.QualifiedComparator),
                Does.Contain("EXACT_ORDINAL@V1"));
            Assert.That(
                HomologatedResolutionComparatorCatalog.All.Select(static x => x.QualifiedComparator),
                Does.Contain("JARO_WINKLER@V1"));
            Assert.That(HomologatedResolutionComparatorCatalog.All.All(static x => x.SystemDefault), Is.True);
        });
    }

    [Test]
    public void ExactOrdinal_ReturnsBinaryAgreement()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HomologatedResolutionComparatorCatalog.Evaluate("EXACT_ORDINAL", "V1", "JOAO", "JOAO"),
                Is.EqualTo(1d));
            Assert.That(
                HomologatedResolutionComparatorCatalog.Evaluate("EXACT_ORDINAL", "V1", "JOAO", "Joao"),
                Is.EqualTo(0d));
        });
    }

    [Test]
    public void JaroWinkler_IsSimilarityScoreAndAllowsCalibratedThreshold()
    {
        Assert.That(
            HomologatedResolutionComparatorCatalog.TryGet("JARO_WINKLER", "V1", out var definition),
            Is.True);

        var equal = HomologatedResolutionComparatorCatalog.Evaluate("JARO_WINKLER", "V1", "MARIA", "MARIA");
        var close = HomologatedResolutionComparatorCatalog.Evaluate("JARO_WINKLER", "V1", "MARIA", "MARTA");
        var far = HomologatedResolutionComparatorCatalog.Evaluate("JARO_WINKLER", "V1", "MARIA", "JOSE");

        Assert.Multiple(() =>
        {
            Assert.That(definition.OutputKind, Is.EqualTo(ResolutionComparatorOutputKind.SimilarityScore));
            Assert.That(definition.CalibratedThresholdAllowed, Is.True);
            Assert.That(equal, Is.EqualTo(1d));
            Assert.That(close, Is.GreaterThan(far));
        });
    }
}

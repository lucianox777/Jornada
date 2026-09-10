using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IbgeCalibrationAttributeCatalogTests
{
    [TestCase(BlockingCandidateFeatureCatalog.NameFull)]
    [TestCase(BlockingCandidateFeatureCatalog.NameFirst)]
    [TestCase(BlockingCandidateFeatureCatalog.NameSurnames)]
    [TestCase(BlockingCandidateFeatureCatalog.NameLast)]
    public void Name_features_are_enriched_by_known_ibge_name_source(string feature)
    {
        Assert.That(IbgeCalibrationAttributeCatalog.TryGetSource(feature, out var source), Is.True);
        Assert.That(source, Is.EqualTo(ExternalNameFrequencyCatalog.IbgeSource));
    }

    [TestCase(BlockingCandidateFeatureCatalog.BirthDay)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthMonth)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthYear)]
    [TestCase("district")]
    public void Features_without_explicit_semantic_mapping_remain_without_ibge_enrichment(string feature)
    {
        Assert.That(IbgeCalibrationAttributeCatalog.Supports(feature), Is.False);
    }

    [Test]
    public void Unsupported_feature_does_not_disappear_from_optimizer_candidate_catalog()
    {
        Assert.That(BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates,
            Does.Contain(BlockingCandidateFeatureCatalog.BirthYear));
        Assert.That(IbgeCalibrationAttributeCatalog.Supports(BlockingCandidateFeatureCatalog.BirthYear), Is.False);
    }
}

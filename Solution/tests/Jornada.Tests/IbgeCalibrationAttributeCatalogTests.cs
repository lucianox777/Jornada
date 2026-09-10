using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IbgeCalibrationAttributeCatalogTests
{
    [Test]
    public void First_name_uses_ibge_first_name_statistics()
    {
        Assert.That(IbgeCalibrationAttributeCatalog.TryGetMapping(
            BlockingCandidateFeatureCatalog.FirstName, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Source, Is.EqualTo(ExternalNameFrequencyCatalog.IbgeSource));
            Assert.That(mapping.StatisticKind, Is.EqualTo(IbgeNameStatisticKind.FirstName));
        });
    }

    [TestCase(BlockingCandidateFeatureCatalog.Surnames)]
    [TestCase(BlockingCandidateFeatureCatalog.LastName)]
    public void Surname_features_use_ibge_surname_statistics(string feature)
    {
        Assert.That(IbgeCalibrationAttributeCatalog.TryGetMapping(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Source, Is.EqualTo(ExternalNameFrequencyCatalog.IbgeSource));
            Assert.That(mapping.StatisticKind, Is.EqualTo(IbgeNameStatisticKind.Surname));
        });
    }

    [TestCase(BlockingCandidateFeatureCatalog.FullName)]
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
            Does.Contain(BlockingCandidateFeatureCatalog.FullName));
        Assert.That(BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates,
            Does.Contain(BlockingCandidateFeatureCatalog.BirthYear));
        Assert.That(IbgeCalibrationAttributeCatalog.Supports(BlockingCandidateFeatureCatalog.FullName), Is.False);
        Assert.That(IbgeCalibrationAttributeCatalog.Supports(BlockingCandidateFeatureCatalog.BirthYear), Is.False);
    }
}

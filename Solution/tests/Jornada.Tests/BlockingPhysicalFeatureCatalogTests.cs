using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingPhysicalFeatureCatalogTests
{
    [Test]
    public void RequiredOptimizerFeatures_CoversEveryRequiredLogicalCandidate()
    {
        var physical = BlockingPhysicalFeatureCatalog.RequiredOptimizerFeatures
            .Select(static x => x.Feature)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var logical = BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.That(physical, Is.EqualTo(logical));
    }

    [TestCase(BlockingCandidateFeatureCatalog.FullName)]
    [TestCase(BlockingCandidateFeatureCatalog.FirstName)]
    [TestCase(BlockingCandidateFeatureCatalog.Surnames)]
    [TestCase(BlockingCandidateFeatureCatalog.LastName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFirstName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherSurnames)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherLastName)]
    public void NameFeatures_UseMaterializedSilverHistory(string feature)
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection));
            Assert.That(mapping.SourceScope, Is.EqualTo(BlockingPhysicalSourceScope.SilverObservationHistory));
        });
    }

    [TestCase(BlockingCandidateFeatureCatalog.BirthDay)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthMonth)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthYear)]
    public void BirthComponents_UseMaterializedCurrentGoldValue(string feature)
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection));
            Assert.That(mapping.SourceScope, Is.EqualTo(BlockingPhysicalSourceScope.GoldCurrent));
        });
    }

    [Test]
    public void Surnames_IsExplicitlyMultiValued()
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(
            BlockingCandidateFeatureCatalog.Surnames, out var mapping), Is.True);
        Assert.That(mapping.MultiValued, Is.True);
    }
}

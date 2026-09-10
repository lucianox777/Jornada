using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class BlockingFeatureTemporalCatalogTests
{
    [TestCase(BlockingFeatureNames.BirthDay)]
    [TestCase(BlockingFeatureNames.BirthMonth)]
    [TestCase(BlockingFeatureNames.BirthYear)]
    public void Birth_components_are_stable_identity_data(string feature)
    {
        Assert.That(
            BlockingFeatureTemporalCatalog.Get(feature),
            Is.EqualTo(BlockingFeatureTemporalSemantics.StableIdentityDatum));
    }

    [TestCase(BlockingFeatureNames.FullName)]
    [TestCase(BlockingFeatureNames.FirstName)]
    [TestCase(BlockingFeatureNames.Surnames)]
    [TestCase(BlockingFeatureNames.LastName)]
    [TestCase(BlockingFeatureNames.MotherFullName)]
    [TestCase(BlockingFeatureNames.MotherFirstName)]
    [TestCase(BlockingFeatureNames.MotherSurnames)]
    [TestCase(BlockingFeatureNames.MotherLastName)]
    public void Name_features_are_versioned_aliases(string feature)
    {
        Assert.That(
            BlockingFeatureTemporalCatalog.Get(feature),
            Is.EqualTo(BlockingFeatureTemporalSemantics.VersionedAlias));
    }

    [Test]
    public void Unknown_feature_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockingFeatureTemporalCatalog.Get("unknown"));
    }
}

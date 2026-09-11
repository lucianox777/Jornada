using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class PersonResolutionBlockingProjectorTests
{
    [Test]
    public void Project_PreservesMultiValuedPhoneAndEmail()
    {
        var projected = PersonResolutionBlockingProjector.Project(new IdentityResolutionAttributeValue[]
        {
            new(PersonResolutionContractCatalog.ContactPhone, "(11) 99999-0001"),
            new(PersonResolutionContractCatalog.ContactPhone, "+55 11 98888-0002"),
            new(PersonResolutionContractCatalog.ContactEmail, "A@Example.Test"),
            new(PersonResolutionContractCatalog.ContactEmail, "b@example.test")
        });

        Assert.Multiple(() =>
        {
            Assert.That(projected
                .Where(static key => key.Feature == PersonResolutionContractCatalog.ContactPhoneCanonicalFeature)
                .Select(static key => key.Value),
                Is.EquivalentTo(new[] { "5511999990001", "5511988880002" }));
            Assert.That(projected
                .Where(static key => key.Feature == PersonResolutionContractCatalog.ContactEmailCanonicalFeature)
                .Select(static key => key.Value),
                Is.EquivalentTo(new[] { "a@example.test", "b@example.test" }));
        });
    }

    [Test]
    public void Project_SocialNameProducesOnlyDeclaredNameFeatures()
    {
        var projected = PersonResolutionBlockingProjector.Project(new[]
        {
            new IdentityResolutionAttributeValue(PersonResolutionContractCatalog.SocialName, "Maria das Flores")
        });

        Assert.Multiple(() =>
        {
            Assert.That(projected, Does.Contain(new BlockingProjectionKey(
                PersonResolutionContractCatalog.SocialNameNormalizedFeature, "MARIA DAS FLORES")));
            Assert.That(projected, Does.Contain(new BlockingProjectionKey(
                PersonResolutionContractCatalog.SocialNameFirstFeature, "MARIA")));
            Assert.That(projected, Does.Contain(new BlockingProjectionKey(
                PersonResolutionContractCatalog.SocialNameSurnamesFeature, "FLORES")));
            Assert.That(projected.All(static key =>
                PersonResolutionContractCatalog.TryGetByBlockingFeature(key.Feature, out var field) &&
                field.Code == PersonResolutionContractCatalog.SocialName), Is.True);
        });
    }

    [Test]
    public void Project_ConfidentialShelterAndUnknownAttributesFailClosed()
    {
        var projected = PersonResolutionBlockingProjector.Project(new IdentityResolutionAttributeValue[]
        {
            new(PersonResolutionContractCatalog.ConfidentialShelterAddress, "Rua Sigilosa, 10"),
            new("NOME_PARECIDO_COM_NOME", "Pessoa Qualquer")
        });

        Assert.That(projected, Is.Empty);
    }

    [Test]
    public void Project_InvalidContactDoesNotProduceApproximateKey()
    {
        var projected = PersonResolutionBlockingProjector.Project(new[]
        {
            new IdentityResolutionAttributeValue(PersonResolutionContractCatalog.ContactEmail, "sem-arroba")
        });

        Assert.That(projected, Is.Empty);
    }

    [TestCase(PersonResolutionContractCatalog.ContactPhoneCanonicalFeature)]
    [TestCase(PersonResolutionContractCatalog.ContactEmailCanonicalFeature)]
    [TestCase(PersonResolutionContractCatalog.SocialNameNormalizedFeature)]
    public void DynamicFeatures_AreExplicitVersionedAliases(string feature)
    {
        Assert.That(
            BlockingFeatureTemporalCatalog.Get(feature),
            Is.EqualTo(BlockingFeatureTemporalSemantics.VersionedAlias));
    }

    [Test]
    public void ConfidentialShelter_IsKnownButNeverEligibleForBlocking()
    {
        Assert.That(PersonResolutionContractCatalog.TryGet(
            PersonResolutionContractCatalog.ConfidentialShelterAddress, out var field), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(field.EligibleForResolution, Is.False);
            Assert.That(field.BlockingFeatures, Is.Empty);
            Assert.That(field.BlockingTemporalSemantics, Is.Null);
        });
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class BlockingFeatureObservationFactoryTests
{
    [Test]
    public void Create_UsesSameDerivedComponentsAsOperationalProjection()
    {
        var pair = new IdentityTrainingPair(
            "João da Silva", new DateOnly(1980, 3, 7), "Maria de Souza",
            "JOAO SILVA", new DateOnly(1980, 3, 7), "Maria Souza");

        var observation = BlockingFeatureObservationFactory.Create(pair, true);

        Assert.Multiple(() =>
        {
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FirstName], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.Surnames], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.LastName], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FullName], Is.False);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.MotherFirstName], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.MotherSurnames], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.MotherLastName], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.BirthDay], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.BirthMonth], Is.True);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.BirthYear], Is.True);
        });
    }

    [Test]
    public void Create_PhoneticProjectionCanAgreeWhenNormalizedNameDoesNot()
    {
        var pair = new IdentityTrainingPair(
            "Philippe Silva", new DateOnly(1980, 3, 7), "Ana Souza",
            "Filipe Silva", new DateOnly(1980, 3, 7), "Ana Souza");

        var observation = BlockingFeatureObservationFactory.Create(pair, true);

        Assert.Multiple(() =>
        {
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FullName], Is.False);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr], Is.True);
        });
    }

    [Test]
    public void Create_DoesNotImputeMissingNameComponents()
    {
        var pair = new IdentityTrainingPair(
            "   ", new DateOnly(1991, 12, 4), "Ana Lima",
            "Carlos Lima", new DateOnly(1991, 12, 4), "Ana Lima");

        var observation = BlockingFeatureObservationFactory.Create(pair, false, 2m);

        Assert.Multiple(() =>
        {
            Assert.That(observation.Weight, Is.EqualTo(2m));
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FullName], Is.Null);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr], Is.Null);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.FirstName], Is.Null);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.Surnames], Is.Null);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.LastName], Is.Null);
            Assert.That(observation.Agreements[BlockingCandidateFeatureCatalog.BirthYear], Is.True);
        });
    }

    [Test]
    public void Create_UsesSetOverlapForMultiValuedContactAttributes()
    {
        var pair = new IdentityTrainingPair(
            "Pessoa A", new DateOnly(1990, 1, 2), "Mãe A",
            "Pessoa A", new DateOnly(1990, 1, 2), "Mãe A",
            LeftResolutionValues: new ResolutionSourceValue[]
            {
                new(PersonResolutionAttributeCatalog.ContactEmail, "primeiro@example.test"),
                new(PersonResolutionAttributeCatalog.ContactEmail, "comum@example.test"),
                new(PersonResolutionAttributeCatalog.ContactPhone, "(11) 99999-0001")
            },
            RightResolutionValues: new ResolutionSourceValue[]
            {
                new(PersonResolutionAttributeCatalog.ContactEmail, "COMUM@example.test"),
                new(PersonResolutionAttributeCatalog.ContactEmail, "outro@example.test"),
                new(PersonResolutionAttributeCatalog.ContactPhone, "(11) 99999-0002")
            });

        var observation = BlockingFeatureObservationFactory.Create(pair, true);

        Assert.Multiple(() =>
        {
            Assert.That(observation.Agreements["email_contato__canonical"], Is.True);
            Assert.That(observation.Agreements["telefone_contato__canonical"], Is.False);
        });
    }

    [Test]
    public void Create_ReportsNullWhenDynamicAttributeIsMissingOnOneSide()
    {
        var pair = new IdentityTrainingPair(
            "Pessoa A", new DateOnly(1990, 1, 2), "Mãe A",
            "Pessoa A", new DateOnly(1990, 1, 2), "Mãe A",
            LeftResolutionValues: new[]
            {
                new ResolutionSourceValue(PersonResolutionAttributeCatalog.ContactEmail, "a@example.test")
            });

        var observation = BlockingFeatureObservationFactory.Create(pair, false);

        Assert.That(observation.Agreements["email_contato__canonical"], Is.Null);
    }

    [Test]
    public void Create_RequiresBothMatchAndNonMatchCorpora()
    {
        var pair = new IdentityTrainingPair(
            "A B", new DateOnly(2000, 1, 1), "C D",
            "A B", new DateOnly(2000, 1, 1), "C D");

        Assert.Throws<ArgumentException>(() =>
            BlockingFeatureObservationFactory.Create(new[] { pair }, Array.Empty<IdentityTrainingPair>()));
    }
}

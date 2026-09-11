using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ResolutionProjectionModelCatalogTests
{
    [Test]
    public void CurrentPlan_PreservesLegacyRepresentationsAndAddsExplicitDynamicAttributes()
    {
        var candidates = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan.BlockingCandidateFeatures;

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Does.Contain(BlockingCandidateFeatureCatalog.FullName));
            Assert.That(candidates, Does.Contain(BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr));
            Assert.That(candidates, Does.Contain(BlockingCandidateFeatureCatalog.MotherFullName));
            Assert.That(candidates, Does.Contain(BlockingCandidateFeatureCatalog.BirthYear));
            Assert.That(candidates, Does.Contain("telefone_contato__canonical"));
            Assert.That(candidates, Does.Contain("email_contato__canonical"));
            Assert.That(candidates, Does.Contain("nome_social__normalized"));
            Assert.That(candidates, Does.Not.Contain("endereco_casa_abrigo_sigilosa__canonical"));
        });
    }

    [Test]
    public void CurrentPlan_MatchesFrozenPhysicalProjectionContract()
    {
        var plan = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;

        Assert.Multiple(() =>
        {
            Assert.That(plan.ProjectionSchemaVersion, Is.EqualTo(PersonResolutionProjectionContract.SchemaVersion));
            Assert.That(plan.Fingerprint, Is.EqualTo(PersonResolutionProjectionContract.FingerprintSha256));
            Assert.That(PersonResolutionAttributeCatalog.ProjectionSchemaVersion,
                Is.EqualTo(PersonResolutionProjectionContract.SchemaVersion));
        });
    }

    [Test]
    public void Build_PersonNameRequiresExplicitEligibility()
    {
        var denied = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("apelido_social", ResolutionAttributeSemantic.PersonName) },
            "TEST_V1");
        var allowed = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("apelido_social", ResolutionAttributeSemantic.PersonName, EligibleForResolution: true) },
            "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(denied.BlockingCandidateFeatures, Is.Empty);
            Assert.That(denied.Features, Has.Count.EqualTo(1));
            Assert.That(allowed.BlockingCandidateFeatures, Does.Contain("apelido_social__normalized"));
            Assert.That(allowed.BlockingCandidateFeatures, Does.Contain("apelido_social__phonetic"));
        });
    }

    [Test]
    public void Build_PersonNameAutomaticallyProducesCalculatedDerivationsFromSeveralAlgorithms()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("apelido_social", ResolutionAttributeSemantic.PersonName, EligibleForResolution: true) },
            "TEST_V1");

        var original = plan.Features.Single(static feature => feature.Feature == "source__apelido_social");
        var calculated = plan.Features.Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(original.CandidateForBlocking, Is.False);
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__upper"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__normalized"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__phonetic"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__surnames"));
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_BASIC_PTBR@V1"));
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_COMPONENTS@V2"));
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_METAPHONE_BR@V1"));
            Assert.That(calculated.All(static x => x.ProjectionOutput is not null), Is.True);
        });
    }

    [Test]
    public void Build_DateComponentsAreGeneratedColumnCandidates()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("data_evento", ResolutionAttributeSemantic.Date, EligibleForResolution: true) },
            "TEST_V1");
        var calculated = plan.Features.Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calculated.Select(static x => x.Feature), Is.EquivalentTo(new[]
            {
                "data_evento__day", "data_evento__month", "data_evento__year"
            }));
            Assert.That(calculated.All(static x => x.Materialization == ResolutionMaterializationKind.GeneratedColumn), Is.True);
            Assert.That(calculated.All(static x => x.Algorithm == "DATE_COMPONENTS@V2"), Is.True);
        });
    }

    [Test]
    public void Build_ImplementedPhoneAndEmailAlgorithmsPreserveSourceMultiplicity()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField("telefone_contato", ResolutionAttributeSemantic.Phone, EligibleForResolution: true, MultiValued: true),
                new ResolutionSourceField("email_contato", ResolutionAttributeSemantic.Email, EligibleForResolution: true, MultiValued: true)
            },
            "TEST_V1");
        var calculated = plan.Features.Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calculated.Select(static x => x.Feature), Is.EquivalentTo(new[]
            {
                "telefone_contato__canonical", "email_contato__canonical"
            }));
            Assert.That(calculated.Single(static x => x.Feature == "telefone_contato__canonical").Algorithm,
                Is.EqualTo("TELEFONE_BR_CANONICO@V2"));
            Assert.That(calculated.Single(static x => x.Feature == "email_contato__canonical").Algorithm,
                Is.EqualTo("EMAIL_CANONICO@V2"));
            Assert.That(calculated.All(static x => x.MultiValued), Is.True);
        });
    }

    [Test]
    public void ConfidentialShelterAddress_IsExplicitlyIneligibleAndFailClosed()
    {
        Assert.That(PersonResolutionAttributeCatalog.TryGet(PersonResolutionAttributeCatalog.ConfidentialShelterAddress, out var field), Is.True);
        var plan = ResolutionProjectionPlanner.Build(new[] { field }, "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(field.Semantic, Is.EqualTo(ResolutionAttributeSemantic.Address));
            Assert.That(field.EligibleForResolution, Is.False);
            Assert.That(plan.BlockingCandidateFeatures, Is.Empty);
            Assert.That(plan.Features.Single().Feature, Is.EqualTo("source__endereco_casa_abrigo_sigilosa"));
        });
    }

    [Test]
    public void Build_SemanticWithoutHomologatedAlgorithmKeepsOnlyOriginalAttribute()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("logradouro", ResolutionAttributeSemantic.Address, EligibleForResolution: true) },
            "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Features, Has.Count.EqualTo(1));
            Assert.That(plan.Features[0].Origin, Is.EqualTo(ResolutionFeatureOrigin.Original));
            Assert.That(plan.BlockingCandidateFeatures, Is.Empty);
        });
    }

    [Test]
    public void Catalog_AllowsSeveralAlgorithmsPerSemanticWithoutOneToOneDictionaryConstraint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.PersonName)
                .Select(static x => x.QualifiedAlgorithm), Does.Contain("PERSON_NAME_METAPHONE_BR@V1"));
            Assert.That(HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.Phone)
                .Select(static x => x.QualifiedAlgorithm), Does.Contain("TELEFONE_BR_CANONICO@V2"));
            Assert.That(HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.Email)
                .Select(static x => x.QualifiedAlgorithm), Does.Contain("EMAIL_CANONICO@V2"));
        });
    }

    [Test]
    public void Build_IsDeterministicAndFingerprintIncludesEligibility()
    {
        var fields = new[]
        {
            new ResolutionSourceField("nome", ResolutionAttributeSemantic.PersonName, EligibleForResolution: true),
            new ResolutionSourceField("data", ResolutionAttributeSemantic.Date, EligibleForResolution: true)
        };
        var first = ResolutionProjectionPlanner.Build(fields, "TEST_V1");
        var reordered = ResolutionProjectionPlanner.Build(fields.Reverse(), "TEST_V1");
        var next = ResolutionProjectionPlanner.Build(fields, "TEST_V2");
        var denied = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField("nome", ResolutionAttributeSemantic.PersonName),
                new ResolutionSourceField("data", ResolutionAttributeSemantic.Date, EligibleForResolution: true)
            }, "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(first.Fingerprint, Is.EqualTo(reordered.Fingerprint));
            Assert.That(first.Fingerprint, Is.Not.EqualTo(next.Fingerprint));
            Assert.That(first.Fingerprint, Is.Not.EqualTo(denied.Fingerprint));
            Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
        });
    }
}

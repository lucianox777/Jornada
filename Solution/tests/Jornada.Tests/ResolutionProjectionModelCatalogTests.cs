using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ResolutionProjectionModelCatalogTests
{
    [Test]
    public void CurrentPlan_IncludesLegacyBasicAndPhoneticPtBrRepresentations()
    {
        var expected = new[]
        {
            BlockingCandidateFeatureCatalog.FullName,
            BlockingCandidateFeatureCatalog.FullNameUpper,
            BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics,
            BlockingCandidateFeatureCatalog.FullNameWithoutParticles,
            BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr,
            BlockingCandidateFeatureCatalog.FirstName,
            BlockingCandidateFeatureCatalog.Surnames,
            BlockingCandidateFeatureCatalog.LastName,
            BlockingCandidateFeatureCatalog.MotherFullName,
            BlockingCandidateFeatureCatalog.MotherFullNameUpper,
            BlockingCandidateFeatureCatalog.MotherFullNameUpperNoDiacritics,
            BlockingCandidateFeatureCatalog.MotherFullNameWithoutParticles,
            BlockingCandidateFeatureCatalog.MotherFullNamePhoneticPtBr,
            BlockingCandidateFeatureCatalog.MotherFirstName,
            BlockingCandidateFeatureCatalog.MotherSurnames,
            BlockingCandidateFeatureCatalog.MotherLastName,
            BlockingCandidateFeatureCatalog.BirthDay,
            BlockingCandidateFeatureCatalog.BirthMonth,
            BlockingCandidateFeatureCatalog.BirthYear
        }.OrderBy(static x => x, StringComparer.Ordinal).ToArray();

        Assert.That(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan.BlockingCandidateFeatures,
            Is.EqualTo(expected));
    }

    [Test]
    public void Build_PersonNameAutomaticallyProducesCalculatedDerivationsFromSeveralAlgorithms()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("apelido_social", ResolutionAttributeSemantic.PersonName) },
            "TEST_V1");

        var original = plan.Features.Single(static feature => feature.Feature == "source__apelido_social");
        var calculated = plan.Features
            .Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(original.Origin, Is.EqualTo(ResolutionFeatureOrigin.Original));
            Assert.That(original.CandidateForBlocking, Is.False);
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__upper"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__upper_no_diacritics"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__without_particles"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__normalized"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__phonetic"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__first"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__surnames"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__last"));
            Assert.That(calculated.All(static x => x.Origin == ResolutionFeatureOrigin.Calculated), Is.True);
            Assert.That(calculated.All(static x => x.ResolutionModel is not null), Is.True);
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_BASIC_PTBR@V1"));
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_COMPONENTS@V2"));
            Assert.That(calculated.Select(static x => x.Algorithm), Does.Contain("PERSON_NAME_METAPHONE_BR@V1"));
        });
    }

    [Test]
    public void Build_DateComponentsAreGeneratedColumnCandidates()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("data_evento", ResolutionAttributeSemantic.Date) },
            "TEST_V1");

        var calculated = plan.Features
            .Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calculated.Select(static x => x.Feature), Is.EquivalentTo(new[]
            {
                "data_evento__day",
                "data_evento__month",
                "data_evento__year"
            }));
            Assert.That(calculated.All(static x => x.Materialization == ResolutionMaterializationKind.GeneratedColumn), Is.True);
            Assert.That(calculated.All(static x => x.Algorithm == "DATE_COMPONENTS@V2"), Is.True);
        });
    }

    [Test]
    public void Build_ImplementedPhoneAndEmailAlgorithmsProduceCanonicalColumns()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField("telefone_contato", ResolutionAttributeSemantic.Phone),
                new ResolutionSourceField("email_contato", ResolutionAttributeSemantic.Email)
            },
            "TEST_V1");

        var calculated = plan.Features
            .Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calculated.Select(static x => x.Feature), Is.EquivalentTo(new[]
            {
                "telefone_contato__canonical",
                "email_contato__canonical"
            }));
            Assert.That(calculated.Single(static x => x.Feature == "telefone_contato__canonical").Algorithm,
                Is.EqualTo("TELEFONE_BR_CANONICO@V2"));
            Assert.That(calculated.Single(static x => x.Feature == "email_contato__canonical").Algorithm,
                Is.EqualTo("EMAIL_CANONICO@V2"));
        });
    }

    [Test]
    public void Build_SemanticWithoutHomologatedAlgorithmKeepsOnlyOriginalAttribute()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceField("logradouro", ResolutionAttributeSemantic.Address) },
            "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Features, Has.Count.EqualTo(1));
            Assert.That(plan.Features[0].Feature, Is.EqualTo("source__logradouro"));
            Assert.That(plan.Features[0].Origin, Is.EqualTo(ResolutionFeatureOrigin.Original));
            Assert.That(plan.BlockingCandidateFeatures, Is.Empty);
        });
    }

    [Test]
    public void AlgorithmVersion_FixesExactOutputColumnsAndRequiresImplementationAndTests()
    {
        Assert.That(
            HomologatedResolutionAlgorithmCatalog.TryGet(
                HomologatedResolutionAlgorithmCatalog.PersonNameBasicPtBrAlgorithm,
                HomologatedResolutionAlgorithmCatalog.PersonNameBasicPtBrVersion,
                out var basicNameAlgorithm), Is.True);
        Assert.That(
            HomologatedResolutionAlgorithmCatalog.TryGet(
                HomologatedResolutionAlgorithmCatalog.PersonNameComponentsAlgorithm,
                HomologatedResolutionAlgorithmCatalog.PersonNameComponentsVersion,
                out var nameAlgorithm), Is.True);
        Assert.That(
            HomologatedResolutionAlgorithmCatalog.TryGet(
                HomologatedResolutionAlgorithmCatalog.PersonNameMetaphoneBrAlgorithm,
                HomologatedResolutionAlgorithmCatalog.PersonNameMetaphoneBrVersion,
                out var phoneticAlgorithm), Is.True);
        Assert.That(
            HomologatedResolutionAlgorithmCatalog.TryGet(
                HomologatedResolutionAlgorithmCatalog.BrazilianPhoneCanonicalAlgorithm,
                HomologatedResolutionAlgorithmCatalog.BrazilianPhoneCanonicalVersion,
                out var phoneAlgorithm), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(basicNameAlgorithm.OutputColumns.Select(static x => x.CanonicalCode), Is.EqualTo(new[]
            {
                "upper", "upper_no_diacritics", "without_particles"
            }));
            Assert.That(nameAlgorithm.OutputColumns.Select(static x => x.CanonicalCode), Is.EqualTo(new[]
            {
                "normalized", "first", "surnames", "last"
            }));
            Assert.That(phoneticAlgorithm.OutputColumns.Select(static x => x.CanonicalCode), Is.EqualTo(new[] { "phonetic" }));
            Assert.That(phoneAlgorithm.OutputColumns.Select(static x => x.CanonicalCode), Is.EqualTo(new[] { "canonical" }));
            Assert.That(phoneticAlgorithm.ImplementationReferences, Has.Some.Contains("17fdee95581442cdcc98fddc30aea3079caf27ae"));
            Assert.That(HomologatedResolutionAlgorithmCatalog.All.All(static x => x.ImplementationReferences.Count > 0), Is.True);
            Assert.That(HomologatedResolutionAlgorithmCatalog.All.All(static x => x.TestReferences.Count > 0), Is.True);
        });
    }

    [Test]
    public void Catalog_AllowsSeveralAlgorithmsPerSemanticWithoutOneToOneDictionaryConstraint()
    {
        var personName = HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.PersonName);
        var phone = HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.Phone);
        var email = HomologatedResolutionAlgorithmCatalog.ForSemantic(ResolutionAttributeSemantic.Email);

        Assert.Multiple(() =>
        {
            Assert.That(personName.Select(static x => x.QualifiedAlgorithm), Does.Contain("PERSON_NAME_BASIC_PTBR@V1"));
            Assert.That(personName.Select(static x => x.QualifiedAlgorithm), Does.Contain("PERSON_NAME_COMPONENTS@V2"));
            Assert.That(personName.Select(static x => x.QualifiedAlgorithm), Does.Contain("PERSON_NAME_METAPHONE_BR@V1"));
            Assert.That(phone.Select(static x => x.QualifiedAlgorithm), Does.Contain("TELEFONE_BR_CANONICO@V2"));
            Assert.That(email.Select(static x => x.QualifiedAlgorithm), Does.Contain("EMAIL_CANONICO@V2"));
        });
    }

    [Test]
    public void Build_IsDeterministicAndFingerprintChangesWithSchemaVersion()
    {
        var fields = new[]
        {
            new ResolutionSourceField("nome", ResolutionAttributeSemantic.PersonName),
            new ResolutionSourceField("data", ResolutionAttributeSemantic.Date)
        };

        var first = ResolutionProjectionPlanner.Build(fields, "TEST_V1");
        var reordered = ResolutionProjectionPlanner.Build(fields.Reverse(), "TEST_V1");
        var next = ResolutionProjectionPlanner.Build(fields, "TEST_V2");

        Assert.Multiple(() =>
        {
            Assert.That(first.Fingerprint, Is.EqualTo(reordered.Fingerprint));
            Assert.That(first.Fingerprint, Is.Not.EqualTo(next.Fingerprint));
            Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
        });
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ResolutionProjectionModelCatalogTests
{
    [Test]
    public void CurrentPlan_PreservesLegacyBlockingCandidateVocabulary()
    {
        var expected = new[]
        {
            BlockingCandidateFeatureCatalog.FullName,
            BlockingCandidateFeatureCatalog.FirstName,
            BlockingCandidateFeatureCatalog.Surnames,
            BlockingCandidateFeatureCatalog.LastName,
            BlockingCandidateFeatureCatalog.MotherFullName,
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
    public void Build_PersonNameAutomaticallyProducesCalculatedDerivations()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceAttribute("apelido_social", ResolutionAttributeSemantic.PersonName) },
            "TEST_V1");

        var original = plan.Features.Single(static feature => feature.Feature == "source__apelido_social");
        var calculated = plan.Features
            .Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(original.Origin, Is.EqualTo(ResolutionFeatureOrigin.Original));
            Assert.That(original.CandidateForBlocking, Is.False);
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__normalized"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__first"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__surnames"));
            Assert.That(calculated.Select(static x => x.Feature), Does.Contain("apelido_social__last"));
            Assert.That(calculated.All(static x => x.Origin == ResolutionFeatureOrigin.Calculated), Is.True);
            Assert.That(calculated.All(static x => x.ResolutionModel is not null), Is.True);
            Assert.That(calculated.All(static x => x.Algorithm is not null), Is.True);
        });
    }

    [Test]
    public void Build_DateComponentsAreGeneratedColumnCandidates()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceAttribute("data_evento", ResolutionAttributeSemantic.Date) },
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
        });
    }

    [Test]
    public void Build_SemanticWithoutHomologatedModelKeepsOnlyOriginalAttribute()
    {
        var plan = ResolutionProjectionPlanner.Build(
            new[] { new ResolutionSourceAttribute("email", ResolutionAttributeSemantic.Email) },
            "TEST_V1");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Features, Has.Count.EqualTo(1));
            Assert.That(plan.Features[0].Feature, Is.EqualTo("source__email"));
            Assert.That(plan.Features[0].Origin, Is.EqualTo(ResolutionFeatureOrigin.Original));
            Assert.That(plan.BlockingCandidateFeatures, Is.Empty);
        });
    }

    [Test]
    public void Build_IsDeterministicAndFingerprintChangesWithSchemaVersion()
    {
        var attributes = new[]
        {
            new ResolutionSourceAttribute("nome", ResolutionAttributeSemantic.PersonName),
            new ResolutionSourceAttribute("data", ResolutionAttributeSemantic.Date)
        };

        var first = ResolutionProjectionPlanner.Build(attributes, "TEST_V1");
        var reordered = ResolutionProjectionPlanner.Build(attributes.Reverse(), "TEST_V1");
        var next = ResolutionProjectionPlanner.Build(attributes, "TEST_V2");

        Assert.Multiple(() =>
        {
            Assert.That(first.Fingerprint, Is.EqualTo(reordered.Fingerprint));
            Assert.That(first.Fingerprint, Is.Not.EqualTo(next.Fingerprint));
            Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
        });
    }

    [Test]
    public void CatalogContainsOnlyExplicitlyHomologatedModels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HomologatedResolutionModelCatalog.All, Is.Not.Empty);
            Assert.That(
                HomologatedResolutionModelCatalog.All.Select(static model => model.Semantic),
                Is.EquivalentTo(new[]
                {
                    ResolutionAttributeSemantic.PersonName,
                    ResolutionAttributeSemantic.Date
                }));
        });
    }
}

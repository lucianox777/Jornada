using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ResolutionProjectionPromotionPlannerTests
{
    [Test]
    public void Build_PromotesWinningFeaturesWithoutInventingIndexEvidence()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var passes = new[]
        {
            LinkageBlockingPass.Create("P1", new[]
            {
                BlockingCandidateFeatureCatalog.LastName,
                BlockingCandidateFeatureCatalog.BirthYear
            })
        };

        var physical = ResolutionProjectionPromotionPlanner.Build(projection, passes);

        Assert.Multiple(() =>
        {
            Assert.That(physical.SimpleIndexes, Is.Empty);
            Assert.That(
                physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.FirstName).Lifecycle,
                Is.EqualTo(ResolutionFeatureLifecycle.Candidate));
            Assert.That(
                physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.LastName).Lifecycle,
                Is.EqualTo(ResolutionFeatureLifecycle.Promoted));
            Assert.That(
                physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.BirthYear).Lifecycle,
                Is.EqualTo(ResolutionFeatureLifecycle.Promoted));
        });
    }

    [Test]
    public void Build_IndexesOnlyWinningFeaturesWithMeasuredPhysicalEvidence()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var passes = new[]
        {
            LinkageBlockingPass.Create("P1", new[]
            {
                BlockingCandidateFeatureCatalog.LastName,
                BlockingCandidateFeatureCatalog.BirthYear
            })
        };

        var physical = ResolutionProjectionPromotionPlanner.Build(
            projection,
            passes,
            physicallyValidatedIndexes: new[] { BlockingCandidateFeatureCatalog.LastName });

        Assert.Multiple(() =>
        {
            Assert.That(
                physical.SimpleIndexes.Select(static x => x.Feature),
                Is.EqualTo(new[] { BlockingCandidateFeatureCatalog.LastName }));
            Assert.That(physical.SimpleIndexes[0].Reason, Is.EqualTo("MEASURED_PHYSICAL_BENEFIT"));
            Assert.That(
                physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.LastName).Lifecycle,
                Is.EqualTo(ResolutionFeatureLifecycle.Indexed));
            Assert.That(
                physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.BirthYear).Lifecycle,
                Is.EqualTo(ResolutionFeatureLifecycle.Promoted));
        });
    }

    [Test]
    public void Build_KeepsPreviouslyPromotedCalculatedFeatureWhenItLeavesCurrentPlan()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var physical = ResolutionProjectionPromotionPlanner.Build(
            projection,
            new[]
            {
                LinkageBlockingPass.Create("P1", new[] { BlockingCandidateFeatureCatalog.BirthYear })
            },
            new[] { BlockingCandidateFeatureCatalog.LastName });

        Assert.That(
            physical.Features.Single(x => x.Feature == BlockingCandidateFeatureCatalog.LastName).Lifecycle,
            Is.EqualTo(ResolutionFeatureLifecycle.Promoted));
    }

    [Test]
    public void Build_SourceAttributesNeverBecomeCalculatedOrIndexed()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var physical = ResolutionProjectionPromotionPlanner.Build(
            projection,
            new[]
            {
                LinkageBlockingPass.Create("P1", new[] { BlockingCandidateFeatureCatalog.FullName })
            });

        Assert.That(
            physical.Features.Where(static x => x.Origin == ResolutionFeatureOrigin.Original)
                .All(static x => x.Lifecycle == ResolutionFeatureLifecycle.Source),
            Is.True);
    }

    [Test]
    public void Build_FailsClosedWhenBlockingPlanReferencesUnknownFeature()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;

        Assert.Throws<ArgumentException>(() => ResolutionProjectionPromotionPlanner.Build(
            projection,
            new[] { LinkageBlockingPass.Create("P1", new[] { "feature_inexistente" }) }));
    }

    [Test]
    public void Build_FailsClosedWhenIndexEvidenceDoesNotBelongToWinningPlan()
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;

        Assert.Throws<ArgumentException>(() => ResolutionProjectionPromotionPlanner.Build(
            projection,
            new[] { LinkageBlockingPass.Create("P1", new[] { BlockingCandidateFeatureCatalog.BirthYear }) },
            physicallyValidatedIndexes: new[] { BlockingCandidateFeatureCatalog.LastName }));
    }
}

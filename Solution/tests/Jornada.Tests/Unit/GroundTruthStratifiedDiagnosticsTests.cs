using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthStratifiedDiagnosticsTests
{
    [Test]
    public void SampleBudgetIsAllocatedByLabelablePopulationShare()
    {
        var plan = GroundTruthStratifiedSamplePlanner.Plan(
            new GroundTruthPopulationSnapshot(
                WithCpf: 3000,
                WithoutCpfWithCns: 1000,
                WithoutCpfWithoutCns: 500),
            requestedBudget: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(plan.AllocatedBudget, Is.EqualTo(1000));
            Assert.That(plan.For(GroundTruthPopulationStratum.WithCpf).RequestedSample, Is.EqualTo(750));
            Assert.That(plan.For(GroundTruthPopulationStratum.WithoutCpfWithCns).RequestedSample, Is.EqualTo(250));
            Assert.That(plan.For(GroundTruthPopulationStratum.WithoutCpfWithoutCns).RequestedSample, Is.Zero);
        });
    }

    [Test]
    public void UnlabelledPopulationRemainsExplicitInPlan()
    {
        var plan = GroundTruthStratifiedSamplePlanner.Plan(
            new GroundTruthPopulationSnapshot(900, 100, 500),
            requestedBudget: 100);

        var uncovered = plan.For(GroundTruthPopulationStratum.WithoutCpfWithoutCns);

        Assert.Multiple(() =>
        {
            Assert.That(uncovered.HasIndependentLabelSource, Is.False);
            Assert.That(uncovered.LabelSource, Is.Null);
            Assert.That(uncovered.TargetPopulation, Is.EqualTo(500));
            Assert.That(uncovered.PopulationShare, Is.EqualTo(500m / 1500m));
        });
    }

    [Test]
    public void SamplingDoesNotRequireOperationalFiveThousandPairThreshold()
    {
        var plan = GroundTruthStratifiedSamplePlanner.Plan(
            new GroundTruthPopulationSnapshot(80, 20, 0),
            requestedBudget: 10);

        Assert.Multiple(() =>
        {
            Assert.That(plan.AllocatedBudget, Is.EqualTo(10));
            Assert.That(plan.For(GroundTruthPopulationStratum.WithCpf).RequestedSample, Is.EqualTo(8));
            Assert.That(plan.For(GroundTruthPopulationStratum.WithoutCpfWithCns).RequestedSample, Is.EqualTo(2));
        });
    }

    [Test]
    public void SamplingCapsBudgetAtAvailableLabelablePopulation()
    {
        var plan = GroundTruthStratifiedSamplePlanner.Plan(
            new GroundTruthPopulationSnapshot(3, 2, 100),
            requestedBudget: 50);

        Assert.That(plan.AllocatedBudget, Is.EqualTo(5));
    }

    [Test]
    public void DiagnosticsUseExplicitVersionedStatisticalAssessment()
    {
        var assessment = new GroundTruthStatisticalAssessment(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithoutCpfWithCns,
            Method: "STRATIFIED_PRECISION_CHECK",
            MethodVersion: "V1",
            AssessedPositivePairCount: 240,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);

        var diagnostics = GroundTruthCoverageDiagnosticsBuilder.Build(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithoutCpfWithCns,
            eligiblePopulation: 400,
            targetPopulation: 800,
            positivePairCount: 240,
            assessment);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.StatisticallySufficient, Is.True);
            Assert.That(diagnostics.RepresentativeForTargetStratum, Is.True);
            Assert.That(diagnostics.CanBePreferredForCalibration, Is.True);
            Assert.That(diagnostics.Coverage, Is.EqualTo(0.5m));
        });
    }

    [Test]
    public void DiagnosticsRejectAssessmentProducedForDifferentPairCount()
    {
        var assessment = new GroundTruthStatisticalAssessment(
            GroundTruthSource.Cpf,
            GroundTruthPopulationStratum.WithCpf,
            "METHOD",
            "V1",
            AssessedPositivePairCount: 4999,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthCoverageDiagnosticsBuilder.Build(
                GroundTruthSource.Cpf,
                GroundTruthPopulationStratum.WithCpf,
                eligiblePopulation: 10000,
                targetPopulation: 10000,
                positivePairCount: 5000,
                assessment));
    }

    [Test]
    public void AssessmentRejectsSourceAssignedToWrongPopulationStratum()
    {
        var assessment = new GroundTruthStatisticalAssessment(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithCpf,
            "METHOD",
            "V1",
            AssessedPositivePairCount: 10,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);

        Assert.Throws<InvalidOperationException>(assessment.Validate);
    }

    [Test]
    public void LargePopulationDoesNotOverflowIntSampleAllocation()
    {
        var plan = GroundTruthStratifiedSamplePlanner.Plan(
            new GroundTruthPopulationSnapshot(
                WithCpf: 10_000_000_000,
                WithoutCpfWithCns: 5_000_000_000,
                WithoutCpfWithoutCns: 1_000_000_000),
            requestedBudget: 1000);

        Assert.That(plan.AllocatedBudget, Is.EqualTo(1000));
    }
}

using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthDiagnosticRunTests
{
    [Test]
    public void BuildPreservesUnrepresentedPopulationAndLabelableDiagnostics()
    {
        var population = new GroundTruthPopulationSnapshot(3000, 1000, 1000);
        var plan = GroundTruthStratifiedSamplePlanner.Plan(population, 1000);

        var observations = new[]
        {
            new GroundTruthStratumObservation(
                GroundTruthPopulationStratum.WithCpf,
                GroundTruthSource.Cpf,
                EligiblePopulation: 2900,
                SampledRecords: 750,
                PositivePairCount: 500),
            new GroundTruthStratumObservation(
                GroundTruthPopulationStratum.WithoutCpfWithCns,
                GroundTruthSource.Cns,
                EligiblePopulation: 700,
                SampledRecords: 250,
                PositivePairCount: 120),
            new GroundTruthStratumObservation(
                GroundTruthPopulationStratum.WithoutCpfWithoutCns,
                null,
                EligiblePopulation: 0,
                SampledRecords: 0,
                PositivePairCount: 0)
        };

        var assessments = new[]
        {
            Assessment(GroundTruthSource.Cpf, GroundTruthPopulationStratum.WithCpf, 500, true, true),
            Assessment(GroundTruthSource.Cns, GroundTruthPopulationStratum.WithoutCpfWithCns, 120, false, false)
        };

        var run = GroundTruthDiagnosticRunBuilder.Build(population, plan, observations, assessments);

        Assert.Multiple(() =>
        {
            Assert.That(run.LabelableDiagnostics, Has.Count.EqualTo(2));
            Assert.That(run.UnrepresentedPopulationShare, Is.EqualTo(0.2m));
            Assert.That(run.For(GroundTruthPopulationStratum.WithCpf).Coverage!.Coverage, Is.EqualTo(2900m / 3000m));
            Assert.That(run.For(GroundTruthPopulationStratum.WithoutCpfWithCns).Coverage!.CanBePreferredForCalibration, Is.False);
            Assert.That(run.For(GroundTruthPopulationStratum.WithoutCpfWithoutCns).Coverage, Is.Null);
        });
    }

    [Test]
    public void BuildRejectsMissingAssessmentForLabelableStratum()
    {
        var population = new GroundTruthPopulationSnapshot(100, 50, 25);
        var plan = GroundTruthStratifiedSamplePlanner.Plan(population, 75);
        var observations = ObservationsFor(plan, cpfPairs: 20, cnsPairs: 10);

        var assessments = new[]
        {
            Assessment(GroundTruthSource.Cpf, GroundTruthPopulationStratum.WithCpf, 20, true, true)
        };

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthDiagnosticRunBuilder.Build(population, plan, observations, assessments));
    }

    [Test]
    public void UnlabelableStratumCannotReceiveStatisticalAssessment()
    {
        var population = new GroundTruthPopulationSnapshot(100, 50, 25);
        var plan = GroundTruthStratifiedSamplePlanner.Plan(population, 75);
        var observations = ObservationsFor(plan, cpfPairs: 20, cnsPairs: 10);

        var assessments = new[]
        {
            Assessment(GroundTruthSource.Cpf, GroundTruthPopulationStratum.WithCpf, 20, true, true),
            Assessment(GroundTruthSource.Cns, GroundTruthPopulationStratum.WithoutCpfWithCns, 10, true, true),
            new GroundTruthStatisticalAssessment(
                GroundTruthSource.Cns,
                GroundTruthPopulationStratum.WithoutCpfWithoutCns,
                "INVALID",
                "V1",
                0,
                false,
                false)
        };

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthDiagnosticRunBuilder.Build(population, plan, observations, assessments));
    }

    [Test]
    public void ObservationCannotExceedPlannedSample()
    {
        var population = new GroundTruthPopulationSnapshot(100, 0, 0);
        var plan = GroundTruthStratifiedSamplePlanner.Plan(population, 10);
        var allocation = plan.For(GroundTruthPopulationStratum.WithCpf);
        var observation = new GroundTruthStratumObservation(
            GroundTruthPopulationStratum.WithCpf,
            GroundTruthSource.Cpf,
            EligiblePopulation: 100,
            SampledRecords: allocation.RequestedSample + 1,
            PositivePairCount: 1);

        Assert.Throws<InvalidOperationException>(() => observation.Validate(allocation));
    }

    private static GroundTruthStratumObservation[] ObservationsFor(
        GroundTruthStratifiedSamplePlan plan,
        long cpfPairs,
        long cnsPairs) =>
        new[]
        {
            ObservationFor(plan.For(GroundTruthPopulationStratum.WithCpf), cpfPairs),
            ObservationFor(plan.For(GroundTruthPopulationStratum.WithoutCpfWithCns), cnsPairs),
            ObservationFor(plan.For(GroundTruthPopulationStratum.WithoutCpfWithoutCns), 0)
        };

    private static GroundTruthStratumObservation ObservationFor(
        GroundTruthSampleAllocation allocation,
        long positivePairs) =>
        new(
            allocation.Stratum,
            allocation.LabelSource,
            allocation.HasIndependentLabelSource ? allocation.TargetPopulation : 0,
            allocation.RequestedSample,
            positivePairs);

    private static GroundTruthStatisticalAssessment Assessment(
        GroundTruthSource source,
        GroundTruthPopulationStratum stratum,
        long positivePairs,
        bool sufficient,
        bool representative) =>
        new(
            source,
            stratum,
            "STRATIFIED_GROUND_TRUTH_DIAGNOSTIC",
            "V1",
            positivePairs,
            sufficient,
            representative);
}

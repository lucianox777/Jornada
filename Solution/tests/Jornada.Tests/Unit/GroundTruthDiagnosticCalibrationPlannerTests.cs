using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthDiagnosticCalibrationPlannerTests
{
    [Test]
    public void DiagnosticRunFeedsCalibrationWithoutRecomputingStatisticalSufficiency()
    {
        var run = BuildRun(cpfSufficient: false, cpfRepresentative: false, cnsSufficient: true, cnsRepresentative: true);

        var plan = GroundTruthCalibrationPlanner.CreateFromDiagnosticRun(
            run,
            new[] { "NOME_COMPLETO", "DATA_NASCIMENTO" },
            new[] { "NOME_JARO_WINKLER" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.LabelSource, Is.EqualTo(GroundTruthSource.Cns));
            Assert.That(plan.PopulationStratum, Is.EqualTo(GroundTruthPopulationStratum.WithoutCpfWithCns));
            Assert.That(plan.Diagnostics.PositivePairCount, Is.EqualTo(80));
        });
    }

    [Test]
    public void DiagnosticRunFailsClosedWhenAssessmentsDoNotAuthorizeAnyLabelSource()
    {
        var run = BuildRun(cpfSufficient: false, cpfRepresentative: false, cnsSufficient: true, cnsRepresentative: false);

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthCalibrationPlanner.CreateFromDiagnosticRun(
                run,
                new[] { "NOME_COMPLETO" },
                new[] { "NOME_JARO_WINKLER" }));
    }

    [Test]
    public void DiagnosticRunAndProjectionPlanPreserveAutomaticAntiLeakageLineage()
    {
        var run = BuildRun(cpfSufficient: true, cpfRepresentative: true, cnsSufficient: true, cnsRepresentative: true);
        var projection = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField(
                    "nome_completo",
                    ResolutionAttributeSemantic.PersonName,
                    CompatibilityProfile: "PERSON_NAME",
                    EligibleForResolution: true),
                new ResolutionSourceField(
                    "data_nascimento",
                    ResolutionAttributeSemantic.Date,
                    CompatibilityProfile: "BIRTH_DATE",
                    EligibleForResolution: true)
            },
            "TEST_DIAGNOSTIC_PROJECTION_V1");

        var plan = GroundTruthCalibrationPlanner.CreateFromDiagnosticRunAndProjectionPlan(
            run,
            projection,
            new[] { "name_full", "birth_year" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.LabelSource, Is.EqualTo(GroundTruthSource.Cpf));
            Assert.That(plan.CandidateGenerationInputs, Is.EquivalentTo(projection.BlockingCandidateFeatures));
            Assert.That(plan.FeatureLineages.Select(static item => item.FeatureName), Does.Contain("name_full"));
            Assert.That(plan.FeatureLineages.Select(static item => item.FeatureName), Does.Contain("birth_year"));
        });
    }

    private static GroundTruthDiagnosticRun BuildRun(
        bool cpfSufficient,
        bool cpfRepresentative,
        bool cnsSufficient,
        bool cnsRepresentative)
    {
        var population = new GroundTruthPopulationSnapshot(1000, 400, 200);
        var samplePlan = GroundTruthStratifiedSamplePlanner.Plan(population, 400);
        var observations = samplePlan.Allocations.Select(allocation =>
            new GroundTruthStratumObservation(
                allocation.Stratum,
                allocation.LabelSource,
                allocation.HasIndependentLabelSource ? allocation.TargetPopulation : 0,
                allocation.RequestedSample,
                allocation.Stratum switch
                {
                    GroundTruthPopulationStratum.WithCpf => 200,
                    GroundTruthPopulationStratum.WithoutCpfWithCns => 80,
                    _ => 0
                })).ToArray();

        var assessments = new[]
        {
            new GroundTruthStatisticalAssessment(
                GroundTruthSource.Cpf,
                GroundTruthPopulationStratum.WithCpf,
                "STRATIFIED_GROUND_TRUTH_DIAGNOSTIC",
                "V1",
                200,
                cpfSufficient,
                cpfRepresentative),
            new GroundTruthStatisticalAssessment(
                GroundTruthSource.Cns,
                GroundTruthPopulationStratum.WithoutCpfWithCns,
                "STRATIFIED_GROUND_TRUTH_DIAGNOSTIC",
                "V1",
                80,
                cnsSufficient,
                cnsRepresentative)
        };

        return GroundTruthDiagnosticRunBuilder.Build(population, samplePlan, observations, assessments);
    }
}

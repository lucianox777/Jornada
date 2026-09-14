using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthCalibrationPlanTests
{
    private static GroundTruthCoverageDiagnostics Cpf(bool sufficient, bool representative) =>
        new(GroundTruthSource.Cpf, GroundTruthPopulationStratum.WithCpf, 1000, 1000, 500, sufficient, representative);

    private static GroundTruthCoverageDiagnostics Cns(bool sufficient, bool representative) =>
        new(GroundTruthSource.Cns, GroundTruthPopulationStratum.WithoutCpfWithCns, 400, 800, 200, sufficient, representative);

    [Test]
    public void PlannerPrefersCpfWhenEvidenceIsSufficientAndRepresentative()
    {
        var plan = GroundTruthCalibrationPlanner.Create(
            Cpf(true, true),
            Cns(true, true),
            new[] { "NOME_COMPLETO", "DATA_NASCIMENTO" },
            new[] { "NOME_JARO_WINKLER", "NASC_ANO_EXACT" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.LabelSource, Is.EqualTo(GroundTruthSource.Cpf));
            Assert.That(plan.PopulationStratum, Is.EqualTo(GroundTruthPopulationStratum.WithCpf));
        });
    }

    [Test]
    public void PlannerUsesCnsOnlyAsAuxiliarySourceForWithoutCpfWithCns()
    {
        var plan = GroundTruthCalibrationPlanner.Create(
            Cpf(false, false),
            Cns(true, true),
            new[] { "NOME_COMPLETO", "DATA_NASCIMENTO" },
            new[] { "NOME_JARO_WINKLER", "NASC_ANO_EXACT" },
            new[] { "IBGE_TERM_FREQUENCY" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.LabelSource, Is.EqualTo(GroundTruthSource.Cns));
            Assert.That(plan.PopulationStratum, Is.EqualTo(GroundTruthPopulationStratum.WithoutCpfWithCns));
            Assert.That(GroundTruthIsolationPolicy.CanActAsIdentityAnchor(plan.LabelSource), Is.False);
        });
    }

    [Test]
    public void PlannerFailsClosedWhenNoSourceRepresentsTarget()
    {
        Assert.Throws<InvalidOperationException>(() => GroundTruthCalibrationPlanner.Create(
            Cpf(false, false),
            Cns(true, false),
            new[] { "NOME_COMPLETO" },
            new[] { "NOME_JARO_WINKLER" }));
    }

    [Test]
    public void PlannerRejectsCnsLeakageBeforeCalibrationRuns()
    {
        Assert.Throws<InvalidOperationException>(() => GroundTruthCalibrationPlanner.Create(
            Cpf(false, false),
            Cns(true, true),
            new[] { "CNS_HASH_BLOCK" },
            new[] { "NOME_JARO_WINKLER" }));
    }

    [Test]
    public void ProjectionPlannerUsesAutomaticLineageForBlockingAndScoring()
    {
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
            "TEST_GROUND_TRUTH_V1");

        var plan = GroundTruthCalibrationPlanner.CreateFromProjectionPlan(
            Cpf(false, false),
            Cns(true, true),
            projection,
            new[] { "name_full", "birth_year" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.LabelSource, Is.EqualTo(GroundTruthSource.Cns));
            Assert.That(plan.CandidateGenerationInputs, Is.EquivalentTo(projection.BlockingCandidateFeatures));
            Assert.That(plan.FeatureLineages.Select(static x => x.FeatureName), Does.Contain("name_full"));
            Assert.That(plan.FeatureLineages.Select(static x => x.FeatureName), Does.Contain("birth_year"));
            Assert.That(
                plan.FeatureLineages.Single(static x => x.FeatureName == "name_full")
                    .Sources.Select(static source => source.CanonicalAttribute),
                Is.EquivalentTo(new[] { "nome_completo" }));
        });
    }

    [Test]
    public void ProjectionPlannerRejectsOpaqueCnsDerivedFeature()
    {
        var projection = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField(
                    "cns",
                    ResolutionAttributeSemantic.Text,
                    EligibleForResolution: false)
            },
            "TEST_CNS_V1");

        var feature = new ResolutionProjectedFeature(
            "identificador_hash",
            "cns",
            ResolutionAttributeSemantic.Text,
            ResolutionFeatureOrigin.Calculated,
            null,
            "HASH@V1",
            "hash",
            ResolutionMaterializationKind.ProcessorMaterialized,
            false,
            true);
        projection = projection with
        {
            Features = projection.Features.Concat(new[] { feature }).ToArray()
        };

        Assert.Throws<InvalidOperationException>(() =>
            GroundTruthCalibrationPlanner.CreateFromProjectionPlan(
                Cpf(false, false),
                Cns(true, true),
                projection,
                new[] { "identificador_hash" }));
    }

    [Test]
    public void ProjectionPlannerRejectsScoringFeatureWithoutProvenance()
    {
        var projection = ResolutionProjectionPlanner.Build(
            new[]
            {
                new ResolutionSourceField(
                    "nome_completo",
                    ResolutionAttributeSemantic.PersonName,
                    EligibleForResolution: true)
            },
            "TEST_UNKNOWN_V1");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GroundTruthCalibrationPlanner.CreateFromProjectionPlan(
                Cpf(true, true),
                Cns(true, true),
                projection,
                new[] { "FEATURE_FORA_DO_PLANO" }));

        Assert.That(ex!.Message, Does.Contain("sem linhagem"));
    }
}

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
}

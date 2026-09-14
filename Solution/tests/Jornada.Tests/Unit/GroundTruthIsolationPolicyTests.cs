using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthIsolationPolicyTests
{
    [Test]
    public void Cpf_IsTheOnlyIdentityAnchor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GroundTruthIsolationPolicy.CanActAsIdentityAnchor(GroundTruthSource.Cpf), Is.True);
            Assert.That(GroundTruthIsolationPolicy.CanActAsIdentityAnchor(GroundTruthSource.Cns), Is.False);
        });
    }

    [Test]
    public void CnsEligibility_IsFailClosedAndParameterized()
    {
        var eligible = new GroundTruthEligibility(
            StructurallyValid: true,
            DistinctPersonsObserved: 2,
            MaximumDistinctPersonsAllowed: 2,
            BirthDateDistanceDays: 3,
            MaximumBirthDateDistanceDays: 30);

        var reused = eligible with { DistinctPersonsObserved = 3 };
        var distantBirthDates = eligible with { BirthDateDistanceDays = 31 };
        var invalid = eligible with { StructurallyValid = false };

        Assert.Multiple(() =>
        {
            Assert.That(eligible.IsEligible, Is.True);
            Assert.That(reused.IsEligible, Is.False);
            Assert.That(distantBirthDates.IsEligible, Is.False);
            Assert.That(invalid.IsEligible, Is.False);
        });
    }

    [Test]
    public void CpfRemainsPreferredWhenSufficientAndRepresentative()
    {
        var cpf = new GroundTruthCoverageDiagnostics(
            GroundTruthSource.Cpf,
            GroundTruthPopulationStratum.WithCpf,
            1000,
            1000,
            500,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);
        var cns = new GroundTruthCoverageDiagnostics(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithoutCpfWithCns,
            100,
            500,
            50,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);

        Assert.That(GroundTruthIsolationPolicy.SelectPreferredLabelSource(cpf, cns), Is.EqualTo(GroundTruthSource.Cpf));
    }

    [Test]
    public void CnsCanBeAuxiliaryWhenCpfIsNotSufficientForTargetAndCnsDiagnosticsPass()
    {
        var cpf = new GroundTruthCoverageDiagnostics(
            GroundTruthSource.Cpf,
            GroundTruthPopulationStratum.WithCpf,
            100,
            1000,
            20,
            StatisticallySufficient: false,
            RepresentativeForTargetStratum: false);
        var cns = new GroundTruthCoverageDiagnostics(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithoutCpfWithCns,
            500,
            800,
            250,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: true);

        Assert.That(GroundTruthIsolationPolicy.SelectPreferredLabelSource(cpf, cns), Is.EqualTo(GroundTruthSource.Cns));
    }

    [Test]
    public void CnsCoverageDoesNotClaimRepresentationOfWholeWithoutCpfStratum()
    {
        var cns = new GroundTruthCoverageDiagnostics(
            GroundTruthSource.Cns,
            GroundTruthPopulationStratum.WithoutCpfWithCns,
            300,
            1000,
            100,
            StatisticallySufficient: true,
            RepresentativeForTargetStratum: false);

        Assert.Multiple(() =>
        {
            Assert.That(cns.Coverage, Is.EqualTo(0.3m));
            Assert.That(cns.CanBePreferredForCalibration, Is.False);
        });
    }

    [Test]
    public void LabelSourceCannotLeakIntoBlockingScoringOrDerivedInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
                GroundTruthSource.Cns,
                new[] { "NOME_NORMALIZADO", "DATA_NASCIMENTO" },
                new[] { "NOME_JARO_WINKLER", "NASC_ANO_EXACT" },
                new[] { "IBGE_TERM_FREQUENCY" }));

            Assert.Throws<InvalidOperationException>(() => GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
                GroundTruthSource.Cns,
                new[] { "CNS_EXACT" },
                new[] { "NOME_JARO_WINKLER" }));

            Assert.Throws<InvalidOperationException>(() => GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
                GroundTruthSource.Cns,
                new[] { "NOME_NORMALIZADO" },
                new[] { "NOME_JARO_WINKLER" },
                new[] { "HASH_CNS" }));
        });
    }
}

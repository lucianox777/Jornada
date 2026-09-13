using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BootstrapLinkageParameterEstimatorTests
{
    [Test]
    public void Exact_ibge_collision_is_sum_of_squared_published_probabilities()
    {
        var rows = new[]
        {
            new NameFrequencyCount("MARIA", 60),
            new NameFrequencyCount("ANA", 30),
            new NameFrequencyCount("RITA", 10)
        };

        var u = BootstrapLinkageParameterEstimator.ExactCollisionProbability(rows);

        Assert.That(u, Is.EqualTo(0.46m).Within(0.00000001m));
    }

    [Test]
    public void Bootstrap_uses_prior_for_m_and_ibge_only_for_exact_name_u()
    {
        var rows = new[]
        {
            new NameFrequencyCount("MARIA", 50),
            new NameFrequencyCount("ANA", 30),
            new NameFrequencyCount("JOSE", 20)
        };

        var result = BootstrapLinkageParameterEstimator.Estimate(
            rows, populationSize: 10_000, distinctBirthDates: 1_000,
            threshold: 0.95m, conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(result.Stage, Is.EqualTo(LinkageParameterStage.PRIOR_BOOTSTRAP));
            Assert.That(result.MOrigin, Is.EqualTo("PRIOR_INSTITUCIONAL"));
            Assert.That(result.UNameOrigin, Does.Contain("IBGE"));
            Assert.That(result.EligibleForAutomaticActivation, Is.False);
            Assert.That(result.Parameters["M_SAMPLE_SIZE"], Is.Zero);
            Assert.That(result.Parameters["M_ORIGIN_PRIOR_INSTITUCIONAL"], Is.EqualTo(1m));
            Assert.That(result.Parameters["U_NOME_ORIGIN_IBGE_COLLISION"], Is.EqualTo(1m));
            Assert.That(result.Parameters["M_NOME_EXACT"], Is.EqualTo(0.90m));
            Assert.That(result.Parameters["AUTO_PROMOTION_ALLOWED"], Is.Zero);
        });
    }

    [Test]
    public void Suppressed_or_absent_ibge_names_are_not_materialized_as_zero_probability()
    {
        var rows = new[]
        {
            new NameFrequencyCount("MARIA", 90),
            new NameFrequencyCount("ANA", 10)
        };

        var result = BootstrapLinkageParameterEstimator.Estimate(
            rows, populationSize: 1_000, distinctBirthDates: 100,
            threshold: 0.95m, conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishedNameFrequencyMass, Is.EqualTo(100));
            Assert.That(result.Parameters["U_NOME_EXACT"], Is.GreaterThan(0m));
            Assert.That(result.Parameters.ContainsKey("U_IBGE_SUPPRESSED_ZERO"), Is.False);
        });
    }

    [Test]
    public void Birth_is_single_probabilistic_evidence_in_bootstrap()
    {
        var result = BootstrapLinkageParameterEstimator.Estimate(
            new[] { new NameFrequencyCount("MARIA", 1) },
            populationSize: 1_000, distinctBirthDates: 100,
            threshold: 0.95m, conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(result.Parameters["SCORING_BIRTH_SINGLE_EVIDENCE_V3"], Is.EqualTo(1m));
            Assert.That(result.Parameters.ContainsKey("M_DATA_NASCIMENTO_EXACT"), Is.True);
            Assert.That(result.Parameters.Keys.Any(x => x.StartsWith("M_NASC_DIA_", StringComparison.Ordinal)), Is.False);
            Assert.That(result.Parameters.Keys.Any(x => x.StartsWith("M_NASC_MES_", StringComparison.Ordinal)), Is.False);
            Assert.That(result.Parameters.Keys.Any(x => x.StartsWith("M_NASC_ANO_", StringComparison.Ordinal)), Is.False);
        });
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageParameterEstimatorTests
{
    [Test]
    public void Generates_m_u_threshold_and_prior_parameters()
    {
        var matched = new[]
        {
            new IdentityTrainingPair("Maria da Silva", new DateOnly(1980,1,1), "Ana Silva", "Maria da Silva", new DateOnly(1980,1,1), "Ana Silva"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), "Rita Souza", "João de Souza", new DateOnly(1970,2,2), "Rita Souza")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair("Maria da Silva", new DateOnly(1980,1,1), "Ana Silva", "Carlos Pereira", new DateOnly(1980,1,1), "Lucia Pereira"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), "Rita Souza", "Mariana Lima", new DateOnly(1970,2,2), "Teresa Lima")
        };

        var p = LinkageParameterEstimator.Estimate(matched, unmatched, 1000, 100, 0.5m, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(p.ContainsKey("M_NOME_EXACT"), Is.True);
            Assert.That(p.ContainsKey("U_NOME_LOW"), Is.True);
            Assert.That(p["T_LINKAGE"], Is.EqualTo(0.95m));
            Assert.That(p["CONFLICT_MARGIN"], Is.EqualTo(0.03m));
            Assert.That(p["PRIOR_MATCH_PROBABILITY"], Is.EqualTo(0.1m));
            Assert.That(p["PRIOR_BLOCK_MAX"], Is.EqualTo(0.25m));
        });
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ConflictMarginLogOddsTests
{
    private static readonly Guid ModelId = Guid.Parse("85000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateExact = Guid.Parse("86000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateHigh = Guid.Parse("86000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1985, 5, 12);

    [Test]
    public void Calculate_preserves_log_odds_when_rounded_posterior_saturates()
    {
        var parameters = Parameters();

        var exact = FellegiSunterScoring.Calculate(
            parameters, NameComparisonState.EXACT, NameComparisonState.EXACT, 2, Birth, Birth);
        var high = FellegiSunterScoring.Calculate(
            parameters, NameComparisonState.HIGH, NameComparisonState.EXACT, 2, Birth, Birth);

        Assert.Multiple(() =>
        {
            Assert.That(exact.Posterior, Is.EqualTo(1.00000000m));
            Assert.That(high.Posterior, Is.EqualTo(1.00000000m));
            Assert.That(exact.LogOdds, Is.GreaterThan(high.LogOdds));
            Assert.That(exact.LogOdds - high.LogOdds, Is.GreaterThan(0.5m));
        });
    }

    [Test]
    public void V5_uses_log_odds_margin_while_legacy_replay_keeps_posterior_margin()
    {
        Assert.That(IdentityComparison.CompareName("Maria da Silva", "Maria da Silv"), Is.EqualTo(NameComparisonState.HIGH));
        var parameters = Parameters();
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria da Silva", Birth, "Ana de Souza");
        var candidates = new[]
        {
            new LinkageCandidate(CandidateHigh, "Maria da Silv", Birth, "Ana de Souza"),
            new LinkageCandidate(CandidateExact, observation.NomeCompleto, Birth, observation.NomeMae)
        };

        var v5 = LinkageModelPolicy.Create(
            ModelId, 5, LinkageParameterCatalog.SemanticBirthAlgorithmVersion, parameters);
        var legacy = LinkageModelPolicy.Create(
            ModelId, 4, "FELLEGI_SUNTER_JOINT_BIRTH_V4", parameters);

        var v5Decision = ProbabilisticLinkageDecisions.Resolve(v5, observation, candidates);
        var legacyDecision = ProbabilisticLinkageDecisions.Resolve(legacy, observation, candidates);

        Assert.Multiple(() =>
        {
            Assert.That(v5Decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(v5Decision.PessoaUuidResolvido, Is.EqualTo(CandidateExact));
            Assert.That(v5Decision.MelhorScore, Is.EqualTo(1.00000000m));
            Assert.That(v5Decision.SegundoScore, Is.EqualTo(1.00000000m));
            Assert.That(v5Decision.Margem, Is.GreaterThan(0.5m));

            Assert.That(legacyDecision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(legacyDecision.Margem, Is.EqualTo(0m));
        });
    }

    private static Dictionary<string, decimal> Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .50m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,

            ["M_NOME_EXACT"] = .999999m,
            ["U_NOME_EXACT"] = .000001m,
            ["M_NOME_HIGH"] = .9999m,
            ["U_NOME_HIGH"] = .0001m,
            ["M_NOME_MEDIUM"] = .50m,
            ["U_NOME_MEDIUM"] = .50m,
            ["M_NOME_LOW"] = .0001m,
            ["U_NOME_LOW"] = .9999m,

            ["M_NOME_MAE_EXACT"] = .999999m,
            ["U_NOME_MAE_EXACT"] = .000001m,
            ["M_NOME_MAE_HIGH"] = .70m,
            ["U_NOME_MAE_HIGH"] = .20m,
            ["M_NOME_MAE_MEDIUM"] = .20m,
            ["U_NOME_MAE_MEDIUM"] = .30m,
            ["M_NOME_MAE_LOW"] = .10m,
            ["U_NOME_MAE_LOW"] = .50m
        };

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }

        return parameters;
    }
}

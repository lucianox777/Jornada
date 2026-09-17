using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkageDecisionBoundaryTests
{
    private static readonly DateOnly Birth = new(1982, 4, 10);
    private static readonly IdentityObservation Observation =
        new(null, "NAO_INFORMADO", "Maria da Silva", Birth, "Ana de Souza");

    private static readonly LinkageCandidate Strong =
        new(Guid.Parse("9b000000-0000-4000-8000-000000000001"), "Maria da Silva", Birth, "Ana de Souza");

    private static readonly LinkageCandidate Weak =
        new(Guid.Parse("9b000000-0000-4000-8000-000000000002"), "Nome sem relação", Birth, "Ana de Souza");

    [Test]
    public void V6_threshold_is_inclusive_at_exact_best_score_and_rejects_immediately_above()
    {
        var baseline = CreateModel(.40m, .05m);
        var best = ProbabilisticLinkageDecisions.Rank(baseline, Observation, [Strong, Weak])[0];
        Assert.That(best.Score, Is.LessThan(1m), "O fixture precisa deixar espaço para testar threshold imediatamente acima.");

        var exact = CreateModel(best.Score, .05m);
        var above = CreateModel(best.Score + .00000001m, .05m);

        var exactDecision = ProbabilisticLinkageDecisions.Resolve(exact, Observation, [Strong]);
        var aboveDecision = ProbabilisticLinkageDecisions.Resolve(above, Observation, [Strong]);

        Assert.Multiple(() =>
        {
            Assert.That(exactDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO),
                "T_LINKAGE é inclusivo: score == threshold deve resolver quando não há conflito.");
            Assert.That(exactDecision.MelhorScore, Is.EqualTo(best.Score));
            Assert.That(aboveDecision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(aboveDecision.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
            Assert.That(aboveDecision.MelhorScore, Is.EqualTo(best.Score));
        });
    }

    [Test]
    public void V6_log_odds_conflict_margin_is_inclusive_at_exact_margin_and_conflicts_immediately_above()
    {
        var baseline = CreateModel(.40m, .05m);
        var ranked = ProbabilisticLinkageDecisions.Rank(baseline, Observation, [Strong, Weak]);
        Assert.That(ranked, Has.Count.EqualTo(2));
        var observedMargin = ranked[0].LogOdds - ranked[1].LogOdds;
        Assert.That(observedMargin, Is.GreaterThan(0m));

        var exact = CreateModel(.40m, observedMargin);
        var above = CreateModel(.40m, observedMargin + .00000001m);

        var exactDecision = ProbabilisticLinkageDecisions.Resolve(exact, Observation, [Strong, Weak]);
        var aboveDecision = ProbabilisticLinkageDecisions.Resolve(above, Observation, [Strong, Weak]);

        Assert.Multiple(() =>
        {
            Assert.That(exactDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO),
                "CONFLICT_MARGIN_LOG_ODDS é inclusivo: margem == limite não deve conflitar.");
            Assert.That(exactDecision.Margem, Is.EqualTo(observedMargin));
            Assert.That(aboveDecision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(aboveDecision.Motivo, Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            Assert.That(aboveDecision.Margem, Is.EqualTo(observedMargin));
        });
    }

    private static LinkageModel CreateModel(decimal threshold, decimal logOddsConflictMargin)
    {
        var parameters = V6Parameters();
        parameters[LinkageParameterCatalog.Threshold] = threshold;
        parameters[LinkageParameterCatalog.LogOddsConflictMargin] = logOddsConflictMargin;
        return LinkageModelPolicy.Create(
            Guid.Parse("9a000000-0000-4000-8000-000000000001"),
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            parameters);
    }

    private static Dictionary<string, decimal> V6Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .40m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
        };

        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            parameters[$"M_NOME_{state}"] = state == "EXACT" ? .99m : state == "LOW" ? .001m : .0045m;
            parameters[$"U_NOME_{state}"] = state == "EXACT" ? .001m : state == "LOW" ? .99m : .0045m;
            parameters[$"M_NOME_MAE_{state}"] = .25m;
            parameters[$"U_NOME_MAE_{state}"] = .25m;
        }

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }

        return parameters;
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

/// <summary>
/// Ensaio do scorer/decisor FS REAL sobre universo sintético já recuperado.
/// Não substitui blocking SQL, HTTP, autorização por pessoa ou validação representativa.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class FellegiSunterTopFiveSyntheticTests
{
    private static readonly DateOnly Birth = new(1982, 4, 10);

    private static LinkageModel Model()
    {
        // Parâmetros de fixture declarados; NÃO representam modelo calibrado ATIVO.
        var p = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
        };
        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            p[$"M_NOME_{state}"] = state == "EXACT" ? .90m : state == "LOW" ? .01m : .045m;
            p[$"U_NOME_{state}"] = state == "EXACT" ? .001m : state == "LOW" ? .90m : .0495m;
            p[$"M_NOME_MAE_{state}"] = state == "EXACT" ? .90m : state == "LOW" ? .01m : .045m;
            p[$"U_NOME_MAE_{state}"] = state == "EXACT" ? .001m : state == "LOW" ? .90m : .0495m;
        }
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            p[$"M_NASCIMENTO_SEMANTICO_{state}"] = state == "EXACT" ? .90m : .10m / (BirthDateSemanticEvidence.States.Count - 1);
            p[$"U_NASCIMENTO_SEMANTICO_{state}"] = state == "EXACT" ? .001m : .999m / (BirthDateSemanticEvidence.States.Count - 1);
        }
        return LinkageModelPolicy.Create(Guid.Parse("c0000000-0000-4000-8000-000000000001"),
            6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, p);
    }

    [Test]
    public void SharedFsRanksTopFiveAndMeasuresTruthPresentAndAbsentSeparately()
    {
        var model = Model();
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria da Silva", Birth, "Ana de Souza");
        var truth = Guid.Parse("c0000000-0000-4000-8000-000000000002");
        var candidates = new[]
        {
            new LinkageCandidate(truth, "Maria da Silva", Birth, "Ana de Souza"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000003"), "Maria da Silva", new DateOnly(1995, 4, 10), "Outra Pessoa"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000004"), "Joana Pereira", Birth, "Outra Pessoa"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000005"), "Carla Santos", new DateOnly(1975, 1, 1), "Maria Pereira"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000006"), "Pedro Costa", new DateOnly(1990, 2, 2), "Luiza Martins"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000007"), "Mariana Oliveira", new DateOnly(1981, 3, 3), "Teresa Silva"),
            new LinkageCandidate(Guid.Parse("c0000000-0000-4000-8000-000000000008"), "Joao Ribeiro", new DateOnly(1970, 7, 7), "Carla Lima")
        };
        var ranked = ProbabilisticLinkageDecisions.Rank(model, observation, candidates);
        var topFive = ranked.Take(5).Select(x => x.PessoaUuid).ToArray();
        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
        // Leave-truth-out é um cenário distinto, não uma falsa negativa de blocking.
        var withoutTruth = candidates.Where(x => x.PessoaUuid != truth).ToArray();
        var absent = ProbabilisticLinkageDecisions.Resolve(model, observation, withoutTruth);
        var absentTopFive = ProbabilisticLinkageDecisions.Rank(model, observation, withoutTruth)
            .Take(5).Select(x => x.PessoaUuid).ToArray();

        TestContext.Progress.WriteLine(
            $"SYNTHETIC FS FIXTURE ONLY; positives=1; leave_truth_out=1; " +
            $"fs_tp={(decision.Status == ResolutionStatus.RESOLVIDO && decision.PessoaUuidResolvido == truth ? 1 : 0)}; " +
            $"fs_fp={(decision.Status == ResolutionStatus.RESOLVIDO && decision.PessoaUuidResolvido != truth ? 1 : 0) + (absent.Status == ResolutionStatus.RESOLVIDO ? 1 : 0)}; " +
            $"top5_truth_present={(topFive.Contains(truth) ? 1 : 0)}; " +
            $"top5_truth_absent={(absentTopFive.Contains(truth) ? 1 : 0)}");
        Assert.Multiple(() =>
        {
            Assert.That(ranked, Has.Count.EqualTo(7));
            Assert.That(topFive, Has.Length.EqualTo(5));
            Assert.That(topFive.Distinct(), Has.Count.EqualTo(5));
            Assert.That(topFive, Does.Contain(truth), "A verdade presente deve ser recuperada entre os cinco nesta fixture.");
            Assert.That(absentTopFive, Does.Not.Contain(truth));
            Assert.That(decision.PessoaUuidResolvido is null || candidates.Any(x => x.PessoaUuid == decision.PessoaUuidResolvido));
        });
    }
}

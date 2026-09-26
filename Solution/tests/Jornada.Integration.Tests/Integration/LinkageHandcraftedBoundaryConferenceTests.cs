using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

/// <summary>
/// DT-01: replay deterministico de pares ARTESANAIS, indexados como no replay #507,
/// mas NAO sorteados por ReplayPairs/Monte Carlo. A conferencia V1 compara estados
/// precomputados; nao confere comparadores, blocking ou validade estatistica (#31).
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class LinkageHandcraftedBoundaryConferenceTests
{
    private static readonly Guid ModelId = Guid.Parse("b0100000-0000-4000-8000-000000000001");
    private static readonly DateOnly Birth = new(1990, 1, 2);

    [Test]
    public void Frozen_v1_replays_handpicked_threshold_and_extreme_pairs_without_changing_final_decision()
    {
        // Carrega o contrato GOVERNADO, sem tolerancia fabricada pelo teste.
        var tolerance = ImplementationConferenceToleranceConfiguration.Load(
            Path.Combine(FindRepositoryRoot(), "Solution", "config", "linkage",
                "implementation-conference-tolerance.json")).ToContract();
        Assert.Multiple(() =>
        {
            Assert.That(tolerance.Status, Is.EqualTo("FROZEN"));
            Assert.That(tolerance.Version, Is.EqualTo("V1_2026-09-26"));
            Assert.That(tolerance.MaxAbsolutePairLlrDifference, Is.EqualTo(0.01m));
        });

        // Cada indice representa um par deliberadamente escolhido, nao uma amostra.
        foreach (var scenario in BoundaryCases())
        {
            var parameters = Parameters(scenario);
            var model = LinkageModelPolicy.Create(
                ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
            var mother = scenario.Mode == "TINY_U_CANCELLATION"
                ? NameComparisonState.LOW
                : scenario.Mode == "SATURATED_THREE_LLR"
                    ? NameComparisonState.EXACT
                    : NameComparisonState.HIGH;
            var first = Canonical(
                CandidateId(scenario.Index, false), NameComparisonState.EXACT, mother, parameters,
                scenario.Mode == "DEMOGRAPHIC_GUARD");
            var fixtures = new List<BoundaryCandidate> { first };
            if (scenario.Mode is "DUAL_THRESHOLD" or "MARGIN")
            {
                fixtures.Add(Canonical(
                    CandidateId(scenario.Index, true),
                    scenario.Mode == "MARGIN" ? NameComparisonState.HIGH : NameComparisonState.EXACT,
                    NameComparisonState.HIGH, parameters, false));
            }

            var request = BuildRequest(model, parameters, fixtures, tolerance);
            var report = IndependentImplementationConference.Evaluate(request);
            TestContext.Progress.WriteLine(
                $"DT-01 handcrafted pair {scenario.Index} ({scenario.Mode}): " +
                $"canonical={request.CanonicalDecision.Status}, independent={report.IndependentDecision?.Status}, " +
                $"maxPairLlrDiff={report.MaxObservedPairLlrDifference}, reason={report.Reason ?? "NONE"}");

            Assert.Multiple(() =>
            {
                Assert.That(request.CanonicalDecision.Status, Is.EqualTo(scenario.Expected),
                    $"{scenario.Mode}: fixture deixou de exercitar a decisao planejada.");
                Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.CONFORME),
                    $"{scenario.Mode}: {report.Reason}");
                Assert.That(report.SameFinalDecision, Is.True, scenario.Mode);
                Assert.That(report.IndependentDecision, Is.EqualTo(request.CanonicalDecision),
                    $"{scenario.Mode}: status, UUIDs e motivo precisam ser identicos.");
                Assert.That(report.Candidates.Count, Is.EqualTo(fixtures.Count), scenario.Mode);
                Assert.That(report.Candidates.All(row =>
                    row.AbsoluteLlrDifference <= tolerance.MaxAbsolutePairLlrDifference!.Value),
                    Is.True, scenario.Mode);
                Assert.That(report.StatisticalValidation, Is.EqualTo("NOT_ASSESSED_ISSUE_31"));
            });
            if (scenario.Mode != "SATURATED_THREE_LLR")
                Assert.That(Math.Abs(first.Breakdown.Score.Posterior - parameters[LinkageParameterCatalog.Threshold]),
                    Is.LessThan(0.000001m), $"{scenario.Mode} precisa permanecer na fronteira de T_LINKAGE.");
            if (scenario.Mode == "ROUND_TO_THRESHOLD")
                Assert.That(first.Breakdown.Score.Posterior, Is.EqualTo(0.90000000m),
                    "Exercita explicitamente a quantizacao operacional em oito casas.");
            if (scenario.Mode == "SATURATED_THREE_LLR")
                Assert.That(first.Breakdown.Contributions.Count(c => c.LogLikelihoodRatio > 20m),
                    Is.EqualTo(3), "Exercita a soma dos tres termos maximos do gate V1.");
        }
    }

    [Test]
    public void Legacy_five_term_path_replays_the_same_handcrafted_pair_in_pure_double()
    {
        // Cobertura numerica SUPLEMENTAR: a conferencia governada V1 aceita apenas
        // decision-evidence, que tem tres termos. O scorer legado pode acumular cinco.
        var parameters = Parameters(new BoundaryCase(99, "FIVE_LLR", 0.0000001m,
            ResolutionStatus.RESOLVIDO));
        parameters.Remove(LinkageParameterCatalog.DecisionEvidenceScoring);
        parameters.Remove(LinkageParameterCatalog.BirthSemanticEvidenceScoring);
        parameters[LinkageParameterCatalog.BirthComponentScoring] = 1m;
        parameters["M_NOME_EXACT"] = .9m;
        parameters["U_NOME_EXACT"] = .009m; // 100
        parameters["M_NOME_MAE_HIGH"] = .1m;
        parameters["U_NOME_MAE_HIGH"] = .5m; // 0.2
        foreach (var (term, u) in new[] { ("NASC_DIA", .009m), ("NASC_MES", .009m), ("NASC_ANO", .002m) })
        {
            parameters[$"M_{term}_EXACT"] = .9m;
            parameters[$"U_{term}_EXACT"] = u; // 100, 100, 450
            parameters[$"M_{term}_DIFF"] = .5m;
            parameters[$"U_{term}_DIFF"] = .5m;
        }

        var breakdown = FellegiSunterScoring.CalculateWithBreakdown(
            parameters, NameComparisonState.EXACT, NameComparisonState.HIGH,
            null, Birth, Birth);
        var prior = (double)parameters[LinkageParameterCatalog.PriorMatchProbability];
        // Implementacao paralela local, exclusivamente para diagnostico do legado:
        // acumula primeiro os cinco LLRs em double e so depois soma o logit.
        var independentLlr = new[] { "NOME_EXACT", "NOME_MAE_HIGH",
                "NASC_DIA_EXACT", "NASC_MES_EXACT", "NASC_ANO_EXACT" }
            .Sum(key => Math.Log((double)parameters[$"M_{key}"] /
                                  (double)parameters[$"U_{key}"]));
        var independentLogOdds = Math.Log(prior / (1d - prior)) + independentLlr;
        var independentPosterior = Math.Round(
            (decimal)(1d / (1d + Math.Exp(-Math.Clamp(independentLogOdds, -40d, 40d)))),
            8, MidpointRounding.AwayFromZero);
        var model = LinkageModelPolicy.Create(ModelId, 1,
            "FELLEGI_SUNTER_LEGACY_FIVE_TERM_HANDCRAFT", parameters);
        var runtimeDecision = ProbabilisticLinkageDecisions.ResolveRanked(
            model, [new CandidateScore(CandidateId(99, false),
                breakdown.Score.Posterior, breakdown.Score.LogOdds)],
            "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO");
        var independentStatus = independentPosterior >= parameters[LinkageParameterCatalog.Threshold]
            ? ResolutionStatus.RESOLVIDO
            : ResolutionStatus.NAO_RESOLVIDO;
        TestContext.Progress.WriteLine(
            $"DT-01 handcrafted legacy five terms: posterior decimal={breakdown.Score.Posterior}, " +
            $"double={independentPosterior}, llr diff=" +
            $"{Math.Abs(breakdown.Contributions.Sum(c => c.LogLikelihoodRatio) - (decimal)independentLlr)}");
        Assert.Multiple(() =>
        {
            Assert.That(breakdown.Contributions.Count, Is.EqualTo(5));
            Assert.That(Math.Abs(breakdown.Contributions.Sum(c => c.LogLikelihoodRatio) -
                                 (decimal)independentLlr), Is.LessThanOrEqualTo(.01m));
            Assert.That(breakdown.Score.Posterior, Is.EqualTo(independentPosterior));
            Assert.That(runtimeDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(independentStatus, Is.EqualTo(runtimeDecision.Status));
            Assert.That(Math.Abs(breakdown.Score.Posterior - .9m), Is.LessThan(.000001m));
        });
    }

    private static readonly BoundaryCase[] Cases =
    [
        new(0, "JUST_BELOW_T", .81818170m, ResolutionStatus.NAO_RESOLVIDO),
        new(1, "ROUND_TO_THRESHOLD", .8181818181818182m, ResolutionStatus.RESOLVIDO),
        new(2, "JUST_ABOVE_T", .81818195m, ResolutionStatus.RESOLVIDO),
        new(3, "DUAL_THRESHOLD", .81818195m, ResolutionStatus.CONFLITO),
        new(4, "MARGIN", .81818195m, ResolutionStatus.CONFLITO),
        new(5, "TINY_U_CANCELLATION", .8888888888888889m, ResolutionStatus.RESOLVIDO),
        new(6, "EXTREME_PRIOR", .0000001m, ResolutionStatus.RESOLVIDO),
        new(7, "DEMOGRAPHIC_GUARD", .81818195m, ResolutionStatus.CONFLITO),
        new(8, "SATURATED_THREE_LLR", .9999999m, ResolutionStatus.RESOLVIDO)
    ];

    private static IReadOnlyList<BoundaryCase> BoundaryCases() => Cases;

    private static Dictionary<string, decimal> Parameters(BoundaryCase scenario)
    {
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = scenario.Prior,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .05m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
        };
        foreach (var feature in new[] { "NOME", "NOME_MAE" })
        {
            foreach (var state in LinkageParameterCatalog.NameStates)
            {
                p[$"M_{feature}_{state}"] = .5m;
                p[$"U_{feature}_{state}"] = .5m;
            }
        }
        p["M_NOME_EXACT"] = .9m;
        p["U_NOME_EXACT"] = .09m; // LLR ln(10)
        p["M_NOME_HIGH"] = .891m;
        p["U_NOME_HIGH"] = .09m; // LLR ln(9.9), proximidade de margem
        p["M_NOME_MAE_HIGH"] = .1m;
        p["U_NOME_MAE_HIGH"] = .5m; // LLR ln(0.2), cancela parte do nome
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            p[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            p[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }
        switch (scenario.Mode)
        {
            case "DUAL_THRESHOLD":
                p[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
                break;
            case "DEMOGRAPHIC_GUARD":
                p[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
                break;
            case "TINY_U_CANCELLATION":
                p["U_NOME_EXACT"] = .000000001m;
                p["M_NOME_MAE_LOW"] = .000000001m;
                p["U_NOME_MAE_LOW"] = .8m;
                break;
            case "EXTREME_PRIOR":
                p["U_NOME_EXACT"] = .00000001m; // razao 9e7 contra prior 1e-7
                p["M_NOME_MAE_HIGH"] = .5m;
                break;
            case "SATURATED_THREE_LLR":
                p["M_NOME_EXACT"] = .999999999m;
                p["U_NOME_EXACT"] = .000000001m;
                p["M_NOME_MAE_EXACT"] = .999999999m;
                p["U_NOME_MAE_EXACT"] = .000000001m;
                p["M_NASCIMENTO_SEMANTICO_EXACT"] = .999999999m;
                p["U_NASCIMENTO_SEMANTICO_EXACT"] = .000000001m;
                break;
        }
        return p;
    }

    private static BoundaryCandidate Canonical(Guid id, NameComparisonState name,
        NameComparisonState mother, IReadOnlyDictionary<string, decimal> p, bool collision)
    {
        var breakdown = FellegiSunterScoring.CalculateWithBreakdown(p, name, mother, null, Birth, Birth);
        return new(id, breakdown, collision);
    }

    private static ImplementationConferenceRequest BuildRequest(LinkageModel model,
        IReadOnlyDictionary<string, decimal> p, IReadOnlyList<BoundaryCandidate> fixtures,
        ImplementationConferenceToleranceContract tolerance)
    {
        var ranked = fixtures.Select(f => new CandidateScore(f.Id, f.Breakdown.Score.Posterior,
                f.Breakdown.Score.LogOdds, f.Collision))
            .OrderByDescending(f => f.LogOdds).ThenBy(f => f.PessoaUuid).ToArray();
        var canonical = ProbabilisticLinkageDecisions.ResolveRanked(
            model, ranked, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        var ranks = ranked.Select((c, i) => (c.PessoaUuid, Rank: i + 1))
            .ToDictionary(x => x.PessoaUuid, x => x.Rank);
        var candidates = fixtures.Select(f => new ImplementationConferenceCandidate(
            f.Id, ranks[f.Id],
            f.Breakdown.Contributions.Select(c =>
                new ImplementationConferenceEvidence(c.Evidence, c.State)).ToArray(),
            f.Collision,
            f.Breakdown.Contributions.Sum(c => c.LogLikelihoodRatio),
            f.Breakdown.Score.LogOdds,
            f.Breakdown.Score.Posterior)).ToArray();
        return new ImplementationConferenceRequest(model.ModelId, model.Version,
            model.AlgorithmVersion, p, candidates,
            new ImplementationConferenceDecision(canonical.Status, canonical.PessoaUuidResolvido,
                canonical.MelhorCandidatoUuid, canonical.SegundoCandidatoUuid, canonical.Motivo),
            tolerance);
    }

    private static Guid CandidateId(int index, bool second) =>
        Guid.Parse($"b0100000-0000-4000-8000-{(index * 2 + (second ? 2 : 1)):D12}");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }

    private sealed record BoundaryCase(int Index, string Mode, decimal Prior, ResolutionStatus Expected);
    private sealed record BoundaryCandidate(Guid Id,
        FellegiSunterScoreBreakdown Breakdown, bool Collision);
}

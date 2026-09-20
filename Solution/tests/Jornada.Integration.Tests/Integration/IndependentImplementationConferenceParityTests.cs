using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class IndependentImplementationConferenceParityTests
{
    private static readonly Guid ModelId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly DateOnly Birth = new(1990, 1, 2);

    // Somente fixture de teste. Não é tolerância de governança e nunca é persistida.
    private static readonly ImplementationConferenceToleranceContract TestTolerance =
        new("TEST_ONLY_NOT_GOVERNANCE", "FROZEN", 0.000001m);

    [Test]
    public void Unfrozen_tolerance_is_not_executed_and_never_invents_a_default()
    {
        var request = new ImplementationConferenceRequest(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(),
            [],
            new ImplementationConferenceDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, null,
                "SEM_CANDIDATO_NO_RULESET_BLOCKING"),
            new ImplementationConferenceToleranceContract(
                "PENDING_EXTERNAL_FREEZE", "UNFROZEN", null));

        var report = IndependentImplementationConference.Evaluate(request);

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.NAO_EXECUTADA));
            Assert.That(report.Reason, Is.EqualTo("TOLERANCE_NOT_FROZEN"));
            Assert.That(report.MaxAllowedPairLlrDifference, Is.Null);
            Assert.That(report.StatisticalValidation, Is.EqualTo("NOT_ASSESSED_ISSUE_31"));
        });
    }

    [Test]
    public void Independent_engine_matches_runtime_score_ranking_and_final_policy()
    {
        var parameters = Parameters();
        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);

        var candidates = new[]
        {
            CanonicalCandidate(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                NameComparisonState.EXACT,
                NameComparisonState.EXACT,
                parameters),
            CanonicalCandidate(
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                NameComparisonState.LOW,
                NameComparisonState.LOW,
                parameters)
        };

        var request = RequestFromRuntime(model, candidates, parameters, TestTolerance);
        var report = IndependentImplementationConference.Evaluate(request);

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.CONFORME));
            Assert.That(report.SameFinalDecision, Is.True);
            Assert.That(report.SameTop1, Is.True);
            Assert.That(report.SpearmanRankCorrelation, Is.EqualTo(1m));
            Assert.That(report.MaxObservedPairLlrDifference, Is.LessThanOrEqualTo(TestTolerance.MaxAbsolutePairLlrDifference));
            Assert.That(report.Scope, Is.EqualTo(
                "SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE"));
        });
    }

    [Test]
    public void Independent_engine_matches_runtime_dual_threshold_conflict()
    {
        var parameters = Parameters();
        parameters[LinkageParameterCatalog.Threshold] = .50m;
        parameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;

        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);

        var candidates = new[]
        {
            CanonicalCandidate(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                NameComparisonState.EXACT,
                NameComparisonState.EXACT,
                parameters),
            CanonicalCandidate(
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                NameComparisonState.EXACT,
                NameComparisonState.EXACT,
                parameters)
        };

        var request = RequestFromRuntime(model, candidates, parameters, TestTolerance);
        var report = IndependentImplementationConference.Evaluate(request);

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.CONFORME));
            Assert.That(report.IndependentDecision!.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(report.IndependentDecision.Reason, Is.EqualTo("DOIS_CANDIDATOS_ACIMA_T_LINKAGE"));
        });
    }

    [Test]
    public void Frozen_execution_with_incomplete_model_is_not_executed_instead_of_throwing()
    {
        var parameters = Parameters();
        parameters.Remove("U_NOME_EXACT");
        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters());

        var candidate = CanonicalCandidate(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            NameComparisonState.EXACT,
            NameComparisonState.EXACT,
            Parameters());

        var valid = RequestFromRuntime(model, [candidate], Parameters(), TestTolerance);
        var report = IndependentImplementationConference.Evaluate(
            valid with { Parameters = parameters });

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.NAO_EXECUTADA));
            Assert.That(report.Reason, Is.EqualTo("MODEL_OR_VECTOR_CONTRACT_INVALID"));
        });
    }

    [Test]
    public void Final_decision_divergence_fails_even_when_all_pair_llrs_match()
    {
        var parameters = Parameters();
        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);

        var candidates = new[]
        {
            CanonicalCandidate(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                NameComparisonState.EXACT,
                NameComparisonState.EXACT,
                parameters),
            CanonicalCandidate(
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                NameComparisonState.LOW,
                NameComparisonState.LOW,
                parameters)
        };

        var request = RequestFromRuntime(model, candidates, parameters, TestTolerance);
        var alteredDecision = request.CanonicalDecision with
        {
            Status = ResolutionStatus.CONFLITO,
            ResolvedCandidateId = null,
            Reason = "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"
        };

        var report = IndependentImplementationConference.Evaluate(
            request with { CanonicalDecision = alteredDecision });

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.DIVERGENTE));
            Assert.That(report.Reason, Is.EqualTo("FINAL_DECISION_DIVERGENCE"));
            Assert.That(report.MaxObservedPairLlrDifference, Is.LessThanOrEqualTo(
                TestTolerance.MaxAbsolutePairLlrDifference));
            Assert.That(report.SameTop1, Is.True);
            Assert.That(report.SameFinalDecision, Is.False);
        });
    }

    [Test]
    public void Pair_llr_divergence_fails_even_when_top1_and_final_decision_still_match()
    {
        var parameters = Parameters();
        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);

        var candidates = new[]
        {
            CanonicalCandidate(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                NameComparisonState.EXACT,
                NameComparisonState.EXACT,
                parameters),
            CanonicalCandidate(
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                NameComparisonState.LOW,
                NameComparisonState.LOW,
                parameters)
        };

        var request = RequestFromRuntime(model, candidates, parameters, TestTolerance);
        var changed = request.Candidates.ToArray();
        changed[1] = changed[1] with
        {
            CanonicalLogLikelihoodRatio =
                changed[1].CanonicalLogLikelihoodRatio + .01m
        };

        var report = IndependentImplementationConference.Evaluate(
            request with { Candidates = changed });

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ImplementationConferenceStatus.DIVERGENTE));
            Assert.That(report.Reason, Is.EqualTo("PAIR_LLR_DIVERGENCE"));
            Assert.That(report.SameTop1, Is.True);
            Assert.That(report.SameFinalDecision, Is.True);
        });
    }

    [Test]
    public void Governed_tolerance_configuration_remains_unfrozen_without_numeric_default()
    {
        var root = FindRepositoryRoot();
        var json = File.ReadAllText(Path.Combine(
            root, "Solution", "config", "linkage",
            "implementation-conference-tolerance.json"));

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var config = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(
                config.GetProperty("status").GetString(),
                Is.EqualTo("UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION"));
            Assert.That(
                config.GetProperty("maxAbsolutePairLlrDifference").ValueKind,
                Is.EqualTo(System.Text.Json.JsonValueKind.Null));
            Assert.That(
                config.GetProperty("methodVersion").GetString(),
                Is.EqualTo(IndependentImplementationConference.MethodVersion));
        });
    }

    [Test]
    public void Conference_source_does_not_reuse_runtime_scorer_policy_or_comparators()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Evaluation",
            "IndependentImplementationConference.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("FellegiSunterScoring"));
            Assert.That(source, Does.Not.Contain("ProbabilisticLinkageDecisions"));
            Assert.That(source, Does.Not.Contain("IdentityComparison"));
            Assert.That(source, Does.Not.Contain("BirthDateSemanticEvidence.Classify"));
            Assert.That(source, Does.Contain(
                "SCORER_POLICY_ONLY_STATES_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE"));
        });
    }

    private static ImplementationConferenceRequest RequestFromRuntime(
        LinkageModel model,
        IReadOnlyList<CanonicalCandidateFixture> fixtures,
        IReadOnlyDictionary<string, decimal> parameters,
        ImplementationConferenceToleranceContract tolerance)
    {
        var ranked = fixtures
            .Select(x => new CandidateScore(
                x.CandidateId,
                x.Breakdown.Score.Posterior,
                x.Breakdown.Score.LogOdds,
                false))
            .OrderByDescending(x => x.LogOdds)
            .ThenBy(x => x.PessoaUuid)
            .ToArray();

        var canonical = ProbabilisticLinkageDecisions.ResolveRanked(
            model, ranked, "SEM_CANDIDATO_NO_RULESET_BLOCKING");

        var canonicalRank = ranked
            .Select((x, i) => new { x.PessoaUuid, Rank = i + 1 })
            .ToDictionary(x => x.PessoaUuid, x => x.Rank);

        var inputs = fixtures.Select(x =>
        {
            var evidence = x.Breakdown.Contributions
                .Select(c => new ImplementationConferenceEvidence(c.Evidence, c.State))
                .ToArray();
            var llr = x.Breakdown.Contributions.Sum(c => c.LogLikelihoodRatio);
            return new ImplementationConferenceCandidate(
                x.CandidateId,
                canonicalRank[x.CandidateId],
                evidence,
                false,
                llr,
                x.Breakdown.Score.LogOdds,
                x.Breakdown.Score.Posterior);
        }).ToArray();

        return new ImplementationConferenceRequest(
            model.ModelId,
            model.Version,
            model.AlgorithmVersion,
            parameters,
            inputs,
            new ImplementationConferenceDecision(
                canonical.Status,
                canonical.PessoaUuidResolvido,
                canonical.MelhorCandidatoUuid,
                canonical.SegundoCandidatoUuid,
                canonical.Motivo),
            tolerance);
    }

    private static CanonicalCandidateFixture CanonicalCandidate(
        Guid candidateId,
        NameComparisonState nameState,
        NameComparisonState motherState,
        IReadOnlyDictionary<string, decimal> parameters)
    {
        var breakdown = FellegiSunterScoring.CalculateWithBreakdown(
            parameters,
            nameState,
            motherState,
            null,
            Birth,
            Birth);
        return new(candidateId, breakdown);
    }

    private static Dictionary<string, decimal> Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
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
            foreach (var (state, m, u) in new[]
            {
                ("EXACT", .90m, .01m),
                ("HIGH", .05m, .04m),
                ("MEDIUM", .03m, .10m),
                ("LOW", .02m, .85m)
            })
            {
                parameters[$"M_{feature}_{state}"] = m;
                parameters[$"U_{feature}_{state}"] = u;
            }
        }

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .50m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .50m;
        }

        return parameters;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }

    private sealed record CanonicalCandidateFixture(
        Guid CandidateId,
        FellegiSunterScoreBreakdown Breakdown);
}

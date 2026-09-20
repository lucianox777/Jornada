using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticLinkagePolicyTests
{
    private static readonly Guid ModelId = Guid.Parse("83000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateA = Guid.Parse("84000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateB = Guid.Parse("84000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1982, 4, 10);

    [Test]
    public void V1_RemainsCompatibleWithoutBirthComponentParameters()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(model), Is.False);
        var observation = Observation();
        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
            [new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae)]);
        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(CandidateA));
            Assert.That(decision.ModeloId, Is.EqualTo(ModelId));
        });
    }

    [Test]
    public void EnabledV2_MustNotSilentlyFallBackToV1()
    {
        var parameters = BirthComponentParameters(LinkageParameterCatalog.BirthComponentScoring);
        var complete = LinkageModelPolicy.Create(ModelId, 2, "FELLEGI_SUNTER_V1", parameters);
        Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(complete), Is.True);
        parameters.Remove("U_NASC_DIA_DIFF");
        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(ModelId, 2, "FELLEGI_SUNTER_V1", parameters));
        Assert.That(error!.Message, Does.Contain("U_NASC_DIA_DIFF"));
    }

    [Test]
    public void EnabledV3_MustHaveCompleteSingleBirthDistribution()
    {
        var parameters = SingleBirthParameters();
        var complete = LinkageModelPolicy.Create(ModelId, 3, "FELLEGI_SUNTER_V1", parameters);
        Assert.That(LinkageModelPolicy.SupportsSingleBirthScoring(complete), Is.True);
        parameters.Remove("U_DATA_NASCIMENTO_DIFF");
        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(ModelId, 3, "FELLEGI_SUNTER_V1", parameters));
        Assert.That(error!.Message, Does.Contain("U_DATA_NASCIMENTO_DIFF"));
    }

    [Test]
    public void EnabledV4_MustHaveCompleteJointBirthDistribution_without_legacy_flags()
    {
        var parameters = JointBirthParameters(includeLegacyFlags: false);
        var complete = LinkageModelPolicy.Create(ModelId, 4, "FELLEGI_SUNTER_JOINT_BIRTH_V4", parameters);
        Assert.Multiple(() =>
        {
            Assert.That(LinkageModelPolicy.SupportsJointBirthScoring(complete), Is.True);
            Assert.That(LinkageModelPolicy.SupportsSingleBirthScoring(complete), Is.False);
        });
        parameters.Remove("U_NASCIMENTO_CONJUNTO_101");
        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(ModelId, 4, "FELLEGI_SUNTER_JOINT_BIRTH_V4", parameters));
        Assert.That(error!.Message, Does.Contain("U_NASCIMENTO_CONJUNTO_101"));
    }

    [Test]
    public void EnabledV6_MustHaveCompleteSemanticBirthAndDecisionEvidence()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        var complete = LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
        Assert.Multiple(() =>
        {
            Assert.That(LinkageModelPolicy.SupportsSemanticBirthScoring(complete), Is.True);
            Assert.That(LinkageModelPolicy.SupportsJointBirthScoring(complete), Is.False);
            Assert.That(LinkageModelPolicy.SupportsSingleBirthScoring(complete), Is.False);
        });
        parameters.Remove($"U_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.OneDigitError}");
        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters));
        Assert.That(error!.Message, Does.Contain($"U_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.OneDigitError}"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void V6_provenance_requires_enabled_semantic_scoring_flag(bool disabledInsteadOfMissing)
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        if (disabledInsteadOfMissing) parameters[LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 0m;
        else parameters.Remove(LinkageParameterCatalog.BirthSemanticEvidenceScoring);
        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters));
        Assert.That(error!.Message, Does.Contain(LinkageParameterCatalog.BirthSemanticEvidenceScoring));
    }

    [Test]
    public void V4_precedes_complete_V3_and_V2_when_replaying_mixed_model()
    {
        var parameters = JointBirthParameters(includeLegacyFlags: true);
        parameters[LinkageParameterCatalog.BirthComponentScoring] = 1m;
        foreach (var feature in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
        {
            parameters[$"M_{feature}_EXACT"] = .9m; parameters[$"M_{feature}_DIFF"] = .1m;
            parameters[$"U_{feature}_EXACT"] = .1m; parameters[$"U_{feature}_DIFF"] = .9m;
        }
        var model = LinkageModelPolicy.Create(ModelId, 4, "FELLEGI_SUNTER_JOINT_BIRTH_V4", parameters);
        Assert.Multiple(() =>
        {
            Assert.That(LinkageModelPolicy.SupportsJointBirthScoring(model), Is.True);
            Assert.That(LinkageModelPolicy.SupportsSingleBirthScoring(model), Is.True);
            Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(model), Is.True);
        });
    }

    [Test]
    public void V6_precedes_complete_V4_V3_and_V2_when_replaying_mixed_model()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: true);
        parameters[LinkageParameterCatalog.BirthComponentScoring] = 1m;
        foreach (var feature in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
        {
            parameters[$"M_{feature}_EXACT"] = .9m; parameters[$"M_{feature}_DIFF"] = .1m;
            parameters[$"U_{feature}_EXACT"] = .1m; parameters[$"U_{feature}_DIFF"] = .9m;
        }
        var model = LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
        Assert.Multiple(() =>
        {
            Assert.That(LinkageModelPolicy.SupportsSemanticBirthScoring(model), Is.True);
            Assert.That(LinkageModelPolicy.SupportsJointBirthScoring(model), Is.True);
            Assert.That(LinkageModelPolicy.SupportsSingleBirthScoring(model), Is.True);
            Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(model), Is.True);
        });
    }

    [Test]
    public void LegacyBlockingNamedV2Flag_RemainsReadableOnlyForCompatibility()
    {
        var parameters = BirthComponentParameters(LinkageParameterCatalog.LegacyBirthComponentScoring);
        var legacy = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", parameters);
        Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(legacy), Is.True);
    }

    [Test]
    public void V6_dual_threshold_guard_marks_conflict_when_both_candidates_clear_threshold()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        parameters[LinkageParameterCatalog.PriorMatchProbability] = .25m;
        parameters[LinkageParameterCatalog.Threshold] = .30m;
        parameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        var model = LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
        var observation = Observation();

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [
            new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae),
            new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, "Pessoa sem relação")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.MelhorScore, Is.GreaterThanOrEqualTo(model.Threshold));
            Assert.That(decision.SegundoScore, Is.GreaterThanOrEqualTo(model.Threshold));
            Assert.That(decision.Margem, Is.GreaterThan(model.ConflictMargin),
                "O caso deve provar que a nova trava atua mesmo quando a margem V6 isoladamente permitiria resolver.");
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(decision.PessoaUuidResolvido, Is.Null);
            Assert.That(decision.Motivo, Is.EqualTo("DOIS_CANDIDATOS_ACIMA_T_LINKAGE"));
        });
    }

    [Test]
    public void V6_calibrated_conflict_floor_remains_active_when_link_threshold_rises()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        parameters[LinkageParameterCatalog.Threshold] = .99m;
        parameters[LinkageParameterCatalog.LogOddsConflictMargin] = .10m;
        parameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        parameters[LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m;
        parameters[LinkageParameterCatalog.DualThresholdConflictFloor] = .95m;

        var model = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
        var ranking = new[]
        {
            new CandidateScore(CandidateA, .999m, 7m),
            new CandidateScore(CandidateB, .96m, 3m)
        };

        var decision = ProbabilisticLinkageDecisions.ResolveRanked(
            model, ranking, "SEM_CANDIDATO_TESTE");

        var legacyParameters = new Dictionary<string, decimal>(parameters, StringComparer.Ordinal);
        legacyParameters.Remove(LinkageParameterCatalog.DualThresholdConflictFloorV2);
        legacyParameters.Remove(LinkageParameterCatalog.DualThresholdConflictFloor);
        var legacy = LinkageModelPolicy.Create(
            ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, legacyParameters);
        var legacyDecision = ProbabilisticLinkageDecisions.ResolveRanked(
            legacy, ranking, "SEM_CANDIDATO_TESTE");

        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(decision.Motivo, Is.EqualTo("SEGUNDO_CANDIDATO_ACIMA_PISO_CONFLITO"));
            Assert.That(legacyDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO),
                "Sem o piso independente, elevar T enfraquece a guarda antiga quando o segundo candidato cai abaixo de T.");
        });
    }

    [Test]
    public void Frozen_ranking_replay_uses_the_same_runtime_decision_policy()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        parameters[LinkageParameterCatalog.PriorMatchProbability] = .25m;
        parameters[LinkageParameterCatalog.Threshold] = .30m;
        parameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        var model = LinkageModelPolicy.Create(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            parameters);
        var observation = Observation();
        var candidates = new[]
        {
            new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae),
            new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, "Pessoa sem relação")
        };

        var direct = ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
        var frozenRanking = ProbabilisticLinkageDecisions.Rank(model, observation, candidates);
        var replay = ProbabilisticLinkageDecisions.ResolveRanked(
            model,
            frozenRanking,
            "SEM_CANDIDATO_NO_RULESET_BLOCKING");

        Assert.That(replay, Is.EqualTo(direct));
    }

    [Test]
    public void Changing_only_global_prior_preserves_ranking_and_log_odds_margin()
    {
        var activeParameters = SemanticBirthParameters(includeLegacyFlags: false);
        activeParameters[LinkageParameterCatalog.PriorMatchProbability] = .25m;
        activeParameters[LinkageParameterCatalog.Threshold] = .95m;
        activeParameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        var active = LinkageModelPolicy.Create(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            activeParameters);

        var counterfactualParameters = new Dictionary<string, decimal>(
            activeParameters,
            StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .218397833493m
        };
        var counterfactual = LinkageModelPolicy.Create(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            counterfactualParameters);

        var observation = Observation();
        var candidates = new[]
        {
            new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae),
            new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, "Pessoa sem relação")
        };

        var activeRanking = ProbabilisticLinkageDecisions.Rank(active, observation, candidates);
        var counterfactualRanking = ProbabilisticLinkageDecisions.Rank(counterfactual, observation, candidates);

        Assert.Multiple(() =>
        {
            Assert.That(
                counterfactualRanking.Select(static row => row.PessoaUuid),
                Is.EqualTo(activeRanking.Select(static row => row.PessoaUuid)));
            Assert.That(
                counterfactualRanking[0].LogOdds - counterfactualRanking[1].LogOdds,
                Is.EqualTo(activeRanking[0].LogOdds - activeRanking[1].LogOdds));
            Assert.That(
                counterfactualRanking[0].LogOdds - activeRanking[0].LogOdds,
                Is.EqualTo(counterfactualRanking[1].LogOdds - activeRanking[1].LogOdds));
            Assert.That(
                counterfactualRanking[0].Score,
                Is.LessThan(activeRanking[0].Score));
        });
    }

    [Test]
    public void V6_without_dual_threshold_guard_preserves_replay_of_previous_models()
    {
        var parameters = SemanticBirthParameters(includeLegacyFlags: false);
        parameters[LinkageParameterCatalog.PriorMatchProbability] = .25m;
        parameters[LinkageParameterCatalog.Threshold] = .30m;
        parameters.Remove(LinkageParameterCatalog.DualThresholdConflictGuard);
        var model = LinkageModelPolicy.Create(ModelId, 6, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, parameters);
        var observation = Observation();

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [
            new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae),
            new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, "Pessoa sem relação")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.SegundoScore, Is.GreaterThanOrEqualTo(model.Threshold));
            Assert.That(decision.Margem, Is.GreaterThan(model.ConflictMargin));
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(CandidateA));
        });
    }

    [Test]
    public void Decisions_KeepThresholdMarginAndDeterministicOrdering()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        var observation = Observation();
        var low = ProbabilisticLinkageDecisions.Resolve(model, observation, [new LinkageCandidate(CandidateA, "Nome sem relação", Birth, "Mãe diferente")]);
        Assert.Multiple(() =>
        {
            Assert.That(low.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(low.PessoaUuidResolvido, Is.Null);
            Assert.That(low.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
        });
        var tied = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, observation.NomeMae), new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae)]);
        Assert.Multiple(() =>
        {
            Assert.That(tied.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(tied.PessoaUuidResolvido, Is.Null);
            Assert.That(tied.MelhorCandidatoUuid, Is.EqualTo(CandidateA));
            Assert.That(tied.SegundoCandidatoUuid, Is.EqualTo(CandidateB));
            Assert.That(tied.Margem, Is.EqualTo(0m));
        });
    }

    private static Dictionary<string, decimal> SemanticBirthParameters(bool includeLegacyFlags)
    {
        var parameters = includeLegacyFlags ? JointBirthParameters(includeLegacyFlags: true) : Parameters();
        parameters[LinkageParameterCatalog.DecisionEvidenceScoring] = 1m;
        parameters[LinkageParameterCatalog.LogOddsConflictMargin] = .05m;
        parameters[LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m;
        parameters["M_NOME_MAE_MISSING"] = .10m;
        parameters["U_NOME_MAE_MISSING"] = .10m;
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }
        return parameters;
    }

    private static Dictionary<string, decimal> JointBirthParameters(bool includeLegacyFlags)
    {
        var parameters = includeLegacyFlags ? SingleBirthParameters() : Parameters();
        parameters[LinkageParameterCatalog.BirthJointEvidenceScoring] = 1m;
        foreach (var state in LinkageParameterCatalog.BirthJointStates)
        {
            parameters[$"M_NASCIMENTO_CONJUNTO_{state}"] = .125m;
            parameters[$"U_NASCIMENTO_CONJUNTO_{state}"] = .125m;
        }
        return parameters;
    }

    private static Dictionary<string, decimal> SingleBirthParameters()
    {
        var parameters = Parameters();
        parameters[LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m;
        parameters["M_DATA_NASCIMENTO_EXACT"] = .96m; parameters["M_DATA_NASCIMENTO_DIFF"] = .04m;
        parameters["U_DATA_NASCIMENTO_EXACT"] = .002m; parameters["U_DATA_NASCIMENTO_DIFF"] = .998m;
        return parameters;
    }

    private static Dictionary<string, decimal> BirthComponentParameters(string enableParameter)
    {
        var parameters = Parameters();
        parameters[enableParameter] = 1m;
        foreach (var feature in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
        {
            parameters[$"M_{feature}_EXACT"] = .9m; parameters[$"M_{feature}_DIFF"] = .1m;
            parameters[$"U_{feature}_EXACT"] = .1m; parameters[$"U_{feature}_DIFF"] = .9m;
        }
        return parameters;
    }

    private static IdentityObservation Observation(string? cpf = null) => new(cpf, cpf is null ? "NAO_INFORMADO" : null, "Maria da Silva", Birth, "Ana de Souza");

    private static Dictionary<string, decimal> Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .05m
        };
        foreach (var feature in new[] { "NOME", "NOME_MAE" })
            foreach (var (state, m, u) in new[] { ("EXACT", .90m, .01m), ("HIGH", .05m, .04m), ("MEDIUM", .03m, .10m), ("LOW", .02m, .85m) })
            {
                parameters[$"M_{feature}_{state}"] = m;
                parameters[$"U_{feature}_{state}"] = u;
            }
        return parameters;
    }
}

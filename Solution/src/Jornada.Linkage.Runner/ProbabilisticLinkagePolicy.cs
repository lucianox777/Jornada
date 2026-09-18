using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

internal sealed record LinkageModel(Guid ModelId, int Version, string AlgorithmVersion,
    IReadOnlyDictionary<string, decimal> Parameters, decimal Threshold, decimal ConflictMargin)
{
    internal ProbabilisticLinkageModelRef Reference => new(ModelId, Version, AlgorithmVersion, Threshold, ConflictMargin);
}

internal sealed record LinkageRuntimeSnapshot(LinkageModel Model, LinkageDynamicRuleSet? RuleSet)
{
    internal ProbabilisticLinkageModelRef Reference => Model.Reference with
    {
        BlockingContract = RuleSet is null ? null : new ProbabilisticLinkageBlockingContractRef(
            RuleSet.RuleSetVersion, RuleSet.FingerprintSha256,
            RuleSet.ProjectionSchemaVersion, RuleSet.ProjectionFingerprintSha256)
    };
}

internal sealed record LinkageCandidate(Guid PessoaUuid, string NomeCompleto, DateOnly DataNascimento, string? NomeMae);
internal sealed record CandidateScore(Guid PessoaUuid, decimal Score, decimal LogOdds);

internal static class LinkageModelPolicy
{
    internal static LinkageModel Create(Guid modelId, int version, string algorithm, IReadOnlyDictionary<string, decimal> parameters)
    {
        var missing = LinkageParameterCatalog.CoreScoringRequired.Where(x => !parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo incompleto. Parâmetros ausentes: {string.Join(", ", missing)}");
        var decisionV6 = string.Equals(algorithm, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);
        if (decisionV6)
        {
            var missingV6 = LinkageParameterCatalog.DecisionEvidenceRequired.Where(x => !parameters.ContainsKey(x)).ToArray();
            if (missingV6.Length > 0) throw new InvalidOperationException($"Modelo V6 incompleto. Parâmetros de decisão/evidência ausentes: {string.Join(", ", missingV6)}");
            if (parameters[LinkageParameterCatalog.DecisionEvidenceScoring] < 1m)
                throw new InvalidOperationException($"Modelo V6 incompleto. {LinkageParameterCatalog.DecisionEvidenceScoring} deve estar habilitado.");
        }
        var margin = decisionV6 ? parameters[LinkageParameterCatalog.LogOddsConflictMargin] : parameters[LinkageParameterCatalog.ConflictMargin];
        var model = new LinkageModel(modelId, version, algorithm, parameters, parameters[LinkageParameterCatalog.Threshold], margin);
        _ = SupportsSemanticBirthScoring(model); _ = SupportsJointBirthScoring(model); _ = SupportsSingleBirthScoring(model); _ = SupportsBirthComponentScoring(model);
        return model;
    }

    internal static bool SupportsSemanticBirthScoring(LinkageModel model)
    {
        var requiresSemantic = string.Equals(model.AlgorithmVersion, LinkageParameterCatalog.LegacySemanticBirthAlgorithmVersion, StringComparison.Ordinal) ||
            string.Equals(model.AlgorithmVersion, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);
        var enabled = model.Parameters.TryGetValue(LinkageParameterCatalog.BirthSemanticEvidenceScoring, out var current) && current >= 1m;
        if (requiresSemantic && !enabled) throw new InvalidOperationException($"Modelo semântico incompleto. Proveniência {model.AlgorithmVersion} exige {LinkageParameterCatalog.BirthSemanticEvidenceScoring} habilitado.");
        if (!enabled) return false;
        var missing = LinkageParameterCatalog.BirthSemanticEvidenceRequired.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo V5/V6 incompleto. Parâmetros semânticos de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }

    internal static bool SupportsJointBirthScoring(LinkageModel model)
    {
        if (!model.Parameters.TryGetValue(LinkageParameterCatalog.BirthJointEvidenceScoring, out var enabled) || enabled < 1m) return false;
        var missing = LinkageParameterCatalog.BirthJointEvidenceRequired.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo V4 incompleto. Parâmetros de nascimento conjunto ausentes: {string.Join(", ", missing)}");
        return true;
    }

    internal static bool SupportsSingleBirthScoring(LinkageModel model)
    {
        if (!model.Parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var enabled) || enabled < 1m) return false;
        var missing = LinkageParameterCatalog.BirthSingleEvidenceRequired.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo V3 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }

    internal static bool SupportsBirthComponentScoring(LinkageModel model)
    {
        if (SupportsSemanticBirthScoring(model) || SupportsJointBirthScoring(model) || SupportsSingleBirthScoring(model)) return true;
        var enabled = model.Parameters.TryGetValue(LinkageParameterCatalog.BirthComponentScoring, out var current) ? current : model.Parameters.TryGetValue(LinkageParameterCatalog.LegacyBirthComponentScoring, out var legacy) ? legacy : 0m;
        if (enabled < 1m) return false;
        var missing = LinkageParameterCatalog.BirthComponentRequired.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo V2 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }
}

internal static class ProbabilisticLinkageDecisions
{
    internal static IReadOnlyList<CandidateScore> Rank(
        LinkageModel model,
        IdentityObservation observation,
        IReadOnlyList<LinkageCandidate> candidates)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");

        var decisionV6 = string.Equals(model.AlgorithmVersion, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);
        var uniqueCandidates = DeduplicateCandidates(candidates);
        if (uniqueCandidates.Count == 0)
            return Array.Empty<CandidateScore>();

        static NameComparisonState? CompareOptionalName(string? left, string? right) =>
            IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null ? null : IdentityComparison.CompareName(left, right);

        return uniqueCandidates.Select(candidate =>
            {
                var score = FellegiSunterScoring.Calculate(model.Parameters,
                    IdentityComparison.CompareName(observation.NomeCompleto, candidate.NomeCompleto),
                    CompareOptionalName(observation.NomeMae, candidate.NomeMae), uniqueCandidates.Count,
                    observation.DataNascimento, candidate.DataNascimento);
                return new CandidateScore(candidate.PessoaUuid, score.Posterior, score.LogOdds);
            })
            .OrderByDescending(x => decisionV6 ? x.LogOdds : x.Score)
            .ThenBy(x => x.PessoaUuid)
            .ToArray();
    }

    internal static ProbabilisticLinkageDecision Resolve(LinkageModel model, IdentityObservation observation, IReadOnlyList<LinkageCandidate> candidates)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf)) throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");
        var decisionV6 = string.Equals(model.AlgorithmVersion, LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);
        var scored = Rank(model, observation, candidates);
        if (scored.Count == 0)
            return new ProbabilisticLinkageDecision(ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null, model.ModelId,
                decisionV6 ? "SEM_CANDIDATO_NO_RULESET_BLOCKING" : LinkageModelPolicy.SupportsBirthComponentScoring(model) ? "SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE" : "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO");

        var best = scored[0];
        var second = scored.Count > 1 ? scored[1] : null;
        if (second is not null && second.PessoaUuid == best.PessoaUuid)
            throw new InvalidOperationException("Ranking probabilístico inválido: melhor e segundo candidato possuem o mesmo UUID.");
        var secondScore = second?.Score;
        decimal? margin = second is null ? null : decisionV6 ? best.LogOdds - second.LogOdds : best.Score - second.Score;

        if (best.Score < model.Threshold)
            return new ProbabilisticLinkageDecision(ResolutionStatus.NAO_RESOLVIDO, null, best.PessoaUuid, best.Score, second?.PessoaUuid, secondScore, margin, model.ModelId, "ABAIXO_T_LINKAGE");

        var dualThresholdGuard = model.Parameters.TryGetValue(
            LinkageParameterCatalog.DualThresholdConflictGuard,
            out var dualThresholdFlag) && dualThresholdFlag >= 1m;
        if (dualThresholdGuard && second is not null && second.Score >= model.Threshold)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.CONFLITO, null,
                best.PessoaUuid, best.Score,
                second.PessoaUuid, second.Score,
                margin, model.ModelId,
                "DOIS_CANDIDATOS_ACIMA_T_LINKAGE");

        if (second is not null && margin!.Value < model.ConflictMargin)
            return new ProbabilisticLinkageDecision(ResolutionStatus.CONFLITO, null, best.PessoaUuid, best.Score, second.PessoaUuid, second.Score, margin, model.ModelId, "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE");
        return new ProbabilisticLinkageDecision(ResolutionStatus.RESOLVIDO, best.PessoaUuid, best.PessoaUuid, best.Score, second?.PessoaUuid, secondScore, margin, model.ModelId);
    }

    private static IReadOnlyList<LinkageCandidate> DeduplicateCandidates(IReadOnlyList<LinkageCandidate> candidates)
    {
        if (candidates.Count < 2)
            return candidates;

        var result = new List<LinkageCandidate>(candidates.Count);
        foreach (var group in candidates.GroupBy(static candidate => candidate.PessoaUuid))
        {
            var first = group.First();
            if (group.Skip(1).Any(candidate => candidate != first))
                throw new InvalidOperationException(
                    $"Candidato {first.PessoaUuid} apareceu mais de uma vez com atributos divergentes; ranking recusado fail-closed.");
            result.Add(first);
        }
        return result;
    }
}
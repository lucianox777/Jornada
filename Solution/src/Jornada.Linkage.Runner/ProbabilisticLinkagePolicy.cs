using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

// Provider-independent policy. The existing FellegiSunterScoring and name comparators
// remain the only implementation of the mathematical score.
internal sealed record LinkageModel(
    Guid ModelId, int Version, string AlgorithmVersion,
    IReadOnlyDictionary<string, decimal> Parameters, decimal Threshold, decimal ConflictMargin)
{
    internal ProbabilisticLinkageModelRef Reference =>
        new(ModelId, Version, AlgorithmVersion, Threshold, ConflictMargin);
}

/// <summary>
/// Snapshot operacional imutável de um modelo consumível, de seu ruleset e da referência
/// estatística congelada por modelo. Nenhuma alteração da referência ATIVA durante o processo
/// pode reendereçar o score de um modelo já carregado.
/// </summary>
internal sealed record LinkageRuntimeSnapshot(
    LinkageModel Model,
    LinkageDynamicRuleSet? RuleSet,
    IReadOnlyDictionary<string, decimal>? PublicationNameFrequency = null)
{
    internal ProbabilisticLinkageModelRef Reference => Model.Reference with
    {
        BlockingContract = RuleSet is null
            ? null
            : new ProbabilisticLinkageBlockingContractRef(
                RuleSet.RuleSetVersion,
                RuleSet.FingerprintSha256,
                RuleSet.ProjectionSchemaVersion,
                RuleSet.ProjectionFingerprintSha256)
    };
}

internal sealed record LinkageCandidate(Guid PessoaUuid, string NomeCompleto, DateOnly DataNascimento, string? NomeMae);
internal sealed record CandidateScore(Guid PessoaUuid, decimal Score);

internal static class LinkageModelPolicy
{
    private const string SingleBirthScoringParameter = "SCORING_BIRTH_SINGLE_EVIDENCE_V3";
    private const string BirthComponentScoringParameter = "SCORING_BIRTH_COMPONENTS_V2";
    private const string LegacyBirthComponentScoringParameter = "BLOCKING_BIRTH_COMPONENTS_V2";

    private static readonly string[] SingleBirthParameters =
    [
        "M_DATA_NASCIMENTO_EXACT", "M_DATA_NASCIMENTO_DIFF",
        "U_DATA_NASCIMENTO_EXACT", "U_DATA_NASCIMENTO_DIFF"
    ];

    private static readonly string[] BirthComponentParameters =
    [
        "M_NASC_DIA_EXACT", "M_NASC_DIA_DIFF", "U_NASC_DIA_EXACT", "U_NASC_DIA_DIFF",
        "M_NASC_MES_EXACT", "M_NASC_MES_DIFF", "U_NASC_MES_EXACT", "U_NASC_MES_DIFF",
        "M_NASC_ANO_EXACT", "M_NASC_ANO_DIFF", "U_NASC_ANO_EXACT", "U_NASC_ANO_DIFF"
    ];

    internal static LinkageModel Create(Guid modelId, int version, string algorithm,
        IReadOnlyDictionary<string, decimal> parameters)
    {
        var required = new[]
        {
            "PRIOR_MATCH_PROBABILITY", "PRIOR_BLOCK_MIN", "PRIOR_BLOCK_MAX", "T_LINKAGE", "CONFLICT_MARGIN",
            "M_NOME_EXACT", "M_NOME_HIGH", "M_NOME_MEDIUM", "M_NOME_LOW",
            "U_NOME_EXACT", "U_NOME_HIGH", "U_NOME_MEDIUM", "U_NOME_LOW",
            "M_NOME_MAE_EXACT", "M_NOME_MAE_HIGH", "M_NOME_MAE_MEDIUM", "M_NOME_MAE_LOW",
            "U_NOME_MAE_EXACT", "U_NOME_MAE_HIGH", "U_NOME_MAE_MEDIUM", "U_NOME_MAE_LOW"
        };
        var missing = required.Where(x => !parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo incompleto. Parâmetros ausentes: {string.Join(", ", missing)}");
        var model = new LinkageModel(modelId, version, algorithm, parameters,
            parameters["T_LINKAGE"], parameters["CONFLICT_MARGIN"]);

        _ = SupportsSingleBirthScoring(model);
        _ = SupportsBirthComponentScoring(model);
        return model;
    }

    internal static bool SupportsSingleBirthScoring(LinkageModel model)
    {
        if (!model.Parameters.TryGetValue(SingleBirthScoringParameter, out var enabled) || enabled < 1m)
            return false;

        var missing = SingleBirthParameters.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo V3 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }

    internal static bool SupportsBirthComponentScoring(LinkageModel model)
    {
        // V3 ainda pode usar os passes ampliados de blocking de nascimento, embora o
        // score trate a data como uma única evidência. V2 permanece somente para replay.
        if (SupportsSingleBirthScoring(model))
            return true;

        var enabled = model.Parameters.TryGetValue(BirthComponentScoringParameter, out var current)
            ? current
            : model.Parameters.TryGetValue(LegacyBirthComponentScoringParameter, out var legacy)
                ? legacy
                : 0m;
        if (enabled < 1m)
            return false;
        var missing = BirthComponentParameters.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo V2 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }
}

internal static class ProbabilisticLinkageDecisions
{
    internal static ProbabilisticLinkageDecision Resolve(
        LinkageModel model,
        IdentityObservation observation,
        IReadOnlyList<LinkageCandidate> candidates,
        IReadOnlyDictionary<string, decimal>? publicationNameFrequency = null)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");
        if (candidates.Count == 0)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null, model.ModelId,
                LinkageModelPolicy.SupportsBirthComponentScoring(model)
                    ? "SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_AMPLIADOS"
                    : "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO");

        var observationNameProbability = NameFrequencyStratification.ResolvePublicationKeyProbability(
            publicationNameFrequency, observation.NomeCompleto);
        var observationMotherProbability = NameFrequencyStratification.ResolvePublicationKeyProbability(
            publicationNameFrequency, observation.NomeMae);

        var scored = candidates
            .Select(candidate =>
            {
                var candidateNameProbability = NameFrequencyStratification.ResolvePublicationKeyProbability(
                    publicationNameFrequency, candidate.NomeCompleto);
                var candidateMotherProbability = NameFrequencyStratification.ResolvePublicationKeyProbability(
                    publicationNameFrequency, candidate.NomeMae);

                // A maior frequência marginal é a estatística pareada conservadora da V1:
                // basta um dos lados ser comum para a evidência não ser tratada como rara.
                // A regra é parte da versão do algoritmo e pode ser substituída por nova
                // metodologia calibrada sem alterar modelos históricos.
                var nameProbability = ConservativeProbability(observationNameProbability, candidateNameProbability);
                var motherProbability = ConservativeProbability(observationMotherProbability, candidateMotherProbability);
                var nameStratum = NameFrequencyStratification.Classify(model.Parameters, "NOME", nameProbability);
                var motherStratum = NameFrequencyStratification.Classify(model.Parameters, "NOME_MAE", motherProbability);

                return new CandidateScore(candidate.PessoaUuid,
                    FellegiSunterScoring.CalculatePosterior(model.Parameters,
                        IdentityComparison.CompareName(observation.NomeCompleto, candidate.NomeCompleto),
                        IdentityComparison.CompareName(observation.NomeMae, candidate.NomeMae),
                        candidates.Count, observation.DataNascimento, candidate.DataNascimento,
                        nameStratum, motherStratum));
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PessoaUuid)
            .ToArray();
        var best = scored[0];
        var second = scored.Length > 1 ? scored[1] : null;
        var secondScore = second?.Score;
        decimal? margin = secondScore is null ? null : best.Score - secondScore.Value;

        if (best.Score < model.Threshold)
            return new ProbabilisticLinkageDecision(ResolutionStatus.NAO_RESOLVIDO, null,
                best.PessoaUuid, best.Score, second?.PessoaUuid, secondScore, margin, model.ModelId, "ABAIXO_T_LINKAGE");
        if (second is not null && margin!.Value < model.ConflictMargin)
            return new ProbabilisticLinkageDecision(ResolutionStatus.CONFLITO, null,
                best.PessoaUuid, best.Score, second.PessoaUuid, second.Score, margin, model.ModelId,
                "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE");
        return new ProbabilisticLinkageDecision(ResolutionStatus.RESOLVIDO, best.PessoaUuid,
            best.PessoaUuid, best.Score, second?.PessoaUuid, secondScore, margin, model.ModelId);
    }

    private static decimal? ConservativeProbability(decimal? left, decimal? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return Math.Max(left.Value, right.Value);
    }
}

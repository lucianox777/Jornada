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
/// Snapshot operacional imutável de um modelo consumível e do ruleset que governa seu blocking.
/// Um modelo validado é semanticamente imutável; congelar o par evita reler regras durante cada
/// observação e impede que um mesmo processo misture gerações de blocking para o mesmo modelo.
/// </summary>
internal sealed record LinkageRuntimeSnapshot(LinkageModel Model, LinkageDynamicRuleSet? RuleSet)
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
    internal static LinkageModel Create(Guid modelId, int version, string algorithm,
        IReadOnlyDictionary<string, decimal> parameters)
    {
        var missing = LinkageParameterCatalog.CoreScoringRequired
            .Where(x => !parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo incompleto. Parâmetros ausentes: {string.Join(", ", missing)}");
        var model = new LinkageModel(modelId, version, algorithm, parameters,
            parameters[LinkageParameterCatalog.Threshold], parameters[LinkageParameterCatalog.ConflictMargin]);
        // Flags habilitadas nunca podem degradar silenciosamente para uma geração anterior.
        _ = SupportsSingleBirthScoring(model);
        _ = SupportsBirthComponentScoring(model);
        return model;
    }

    internal static bool SupportsSingleBirthScoring(LinkageModel model)
    {
        if (!model.Parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var enabled)
            || enabled < 1m)
            return false;

        var missing = LinkageParameterCatalog.BirthSingleEvidenceRequired
            .Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Modelo V3 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }

    internal static bool SupportsBirthComponentScoring(LinkageModel model)
    {
        // O V3 ainda pode usar blocking de nascimento ampliado, mas o score conta a data inteira uma vez.
        if (SupportsSingleBirthScoring(model))
            return true;

        // Novos modelos V2 usam SCORING_ porque isto seleciona o cálculo Fellegi-Sunter,
        // não a geração de candidatos. O alias BLOCKING_ é aceito somente para leitura
        // de modelos históricos e seeds já publicados.
        var enabled = model.Parameters.TryGetValue(LinkageParameterCatalog.BirthComponentScoring, out var current)
            ? current
            : model.Parameters.TryGetValue(LinkageParameterCatalog.LegacyBirthComponentScoring, out var legacy)
                ? legacy
                : 0m;
        if (enabled < 1m)
            return false;
        var missing = LinkageParameterCatalog.BirthComponentRequired
            .Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo V2 incompleto. Parâmetros de nascimento ausentes: {string.Join(", ", missing)}");
        return true;
    }
}

internal static class ProbabilisticLinkageDecisions
{
    internal static ProbabilisticLinkageDecision Resolve(
        LinkageModel model, IdentityObservation observation, IReadOnlyList<LinkageCandidate> candidates)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");
        if (candidates.Count == 0)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null, model.ModelId,
                LinkageModelPolicy.SupportsBirthComponentScoring(model)
                    ? "SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE"
                    : "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO");

        static NameComparisonState? CompareOptionalName(string? left, string? right) =>
            IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null
                ? null
                : IdentityComparison.CompareName(left, right);

        var scored = candidates
            .Select(candidate => new CandidateScore(candidate.PessoaUuid,
                FellegiSunterScoring.CalculatePosterior(model.Parameters,
                    IdentityComparison.CompareName(observation.NomeCompleto, candidate.NomeCompleto),
                    CompareOptionalName(observation.NomeMae, candidate.NomeMae),
                    candidates.Count, observation.DataNascimento, candidate.DataNascimento)))
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
}

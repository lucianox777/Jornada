using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

internal sealed record LinkageModel(Guid ModelId, int Version, string AlgorithmVersion,
    IReadOnlyDictionary<string, decimal> Parameters, decimal Threshold, decimal ConflictMargin,
    NominalTermFrequencySnapshot? TermFrequency = null)
{
    // Snapshot de cálculo: conversão única dos parâmetros persistidos.
    internal IReadOnlyDictionary<string, double> NumericParameters { get; } =
        FellegiSunterScoring.ToDoubleParameters(Parameters);

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

internal sealed record LinkageCandidate(Guid PessoaUuid, string? NomeCompleto, DateOnly? DataNascimento, string? NomeMae);
internal sealed record CandidateScore(Guid PessoaUuid, decimal Score, decimal LogOdds, bool DemographicExactCollisionRisk = false);

internal static class LinkageModelPolicy
{
    internal static LinkageModel Create(
        Guid modelId,
        int version,
        string algorithm,
        IReadOnlyDictionary<string, decimal> parameters,
        NominalTermFrequencySnapshot? termFrequency = null)
    {
        var missing = LinkageParameterCatalog.CoreScoringRequired.Where(x => !parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo incompleto. Parâmetros ausentes: {string.Join(", ", missing)}");
        var decisionEvidence = LinkageParameterCatalog.UsesDecisionEvidence(algorithm);
        if (decisionEvidence)
        {
            var neutralMissing = string.Equals(algorithm, LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);
            var required = neutralMissing ? LinkageParameterCatalog.NeutralMissingDecisionEvidenceRequired : LinkageParameterCatalog.DecisionEvidenceRequired;
            var missingDecision = required.Where(x => !parameters.ContainsKey(x)).ToArray();
            if (missingDecision.Length > 0) throw new InvalidOperationException($"Modelo de decisão/evidência incompleto. Parâmetros ausentes: {string.Join(", ", missingDecision)}");
            if (parameters[LinkageParameterCatalog.DecisionEvidenceScoring] < 1m)
                throw new InvalidOperationException($"Modelo de decisão/evidência incompleto. {LinkageParameterCatalog.DecisionEvidenceScoring} deve estar habilitado.");
            if (neutralMissing)
            {
                if (parameters[LinkageParameterCatalog.NeutralMissingEvidenceScoring] < 1m)
                    throw new InvalidOperationException($"V8 exige {LinkageParameterCatalog.NeutralMissingEvidenceScoring} habilitado.");
                if (parameters.ContainsKey("M_NOME_MAE_MISSING") || parameters.ContainsKey("U_NOME_MAE_MISSING"))
                    throw new InvalidOperationException("V8 não admite probabilidades M/U para ausência; somente SUPPORT_*_MISSING diagnóstico.");
                var tfEnabled = parameters.TryGetValue(
                    LinkageParameterCatalog.TermFrequencyScoring,
                    out var tfFlag) && tfFlag >= 1m;
                if (tfEnabled)
                {
                    if (!parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyFirstTokenContract, out var tfContract) || tfContract < 1m)
                        throw new InvalidOperationException($"V8 com TF exige {LinkageParameterCatalog.TermFrequencyFirstTokenContract}.");
                    if (!parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyWeight, out var tfWeight) || tfWeight <= 0m)
                        throw new InvalidOperationException($"V8 com TF exige {LinkageParameterCatalog.TermFrequencyWeight} positivo.");
                    if (!parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyMinimumU, out var tfMinimumU)
                        || tfMinimumU <= 0m || tfMinimumU > 1m)
                        throw new InvalidOperationException($"V8 com TF exige {LinkageParameterCatalog.TermFrequencyMinimumU} em (0,1].");
                    if (tfMinimumU > parameters["U_NOME_EXACT"]
                        || tfMinimumU > parameters["U_NOME_MAE_EXACT"])
                        throw new InvalidOperationException("Piso TF não pode superar o u_EXACT de nome ou nome da mãe.");
                    if (termFrequency is null || termFrequency.PersonFirstNameCount == 0 || termFrequency.MotherFirstNameCount == 0)
                        throw new InvalidOperationException("V8 com TF exige snapshot nominal persistido de pessoa e mãe.");
                    if (parameters.ContainsKey(LinkageParameterCatalog.NonUniqueDemographicExactGuard))
                        throw new InvalidOperationException("Novo contrato V8 com TF não admite o guard demográfico fixo legado.");
                }
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.NeutralMissingEvidenceScoring, out var neutralFlag) && neutralFlag >= 1m)
                throw new InvalidOperationException("Modelos V6/V7 não admitem o contrato de ausência neutra V8.");

            var floorV2 = parameters.TryGetValue(LinkageParameterCatalog.DualThresholdConflictFloorV2, out var floorFlag) && floorFlag >= 1m;
            if (floorV2)
            {
                if (!parameters.TryGetValue(LinkageParameterCatalog.DualThresholdConflictFloor, out var floor))
                    throw new InvalidOperationException($"Modelo com {LinkageParameterCatalog.DualThresholdConflictFloorV2} sem {LinkageParameterCatalog.DualThresholdConflictFloor}.");
                if (floor is < 0m or > 1m)
                    throw new InvalidOperationException($"{LinkageParameterCatalog.DualThresholdConflictFloor} deve estar em [0,1].");
            }
        }

        if (string.Equals(algorithm, LinkageParameterCatalog.NominalGuardDecisionEvidenceAlgorithmVersion, StringComparison.Ordinal))
        {
            var missingV7 = LinkageParameterCatalog.NominalGuardV7Required.Where(x => !parameters.ContainsKey(x)).ToArray();
            if (missingV7.Length > 0)
                throw new InvalidOperationException($"Modelo V7 nominal incompleto. Proveniência ausente: {string.Join(", ", missingV7)}");
            if (parameters[LinkageParameterCatalog.NameComparisonPtBrContentTokenGuardV2] < 1m)
                throw new InvalidOperationException($"Modelo V7 nominal incompleto. {LinkageParameterCatalog.NameComparisonPtBrContentTokenGuardV2} deve estar habilitado.");
        }

        var margin = decisionEvidence ? parameters[LinkageParameterCatalog.LogOddsConflictMargin] : parameters[LinkageParameterCatalog.ConflictMargin];
        var model = new LinkageModel(
            modelId, version, algorithm, parameters,
            parameters[LinkageParameterCatalog.Threshold], margin, termFrequency);
        _ = SupportsSemanticBirthScoring(model); _ = SupportsJointBirthScoring(model); _ = SupportsSingleBirthScoring(model); _ = SupportsBirthComponentScoring(model);
        return model;
    }

    internal static bool SupportsSemanticBirthScoring(LinkageModel model)
    {
        var requiresSemantic = LinkageParameterCatalog.RequiresSemanticBirthEvidence(model.AlgorithmVersion);
        var enabled = model.Parameters.TryGetValue(LinkageParameterCatalog.BirthSemanticEvidenceScoring, out var current) && current >= 1m;
        if (requiresSemantic && !enabled) throw new InvalidOperationException($"Modelo semântico incompleto. Proveniência {model.AlgorithmVersion} exige {LinkageParameterCatalog.BirthSemanticEvidenceScoring} habilitado.");
        if (!enabled) return false;
        var missing = LinkageParameterCatalog.BirthSemanticEvidenceRequired.Where(x => !model.Parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Modelo V5/V6/V7 incompleto. Parâmetros semânticos de nascimento ausentes: {string.Join(", ", missing)}");
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

        var decisionEvidence = LinkageParameterCatalog.UsesDecisionEvidence(model.AlgorithmVersion);
        var nameComparisonContract = LinkageParameterCatalog.NameComparisonContractForAlgorithm(model.AlgorithmVersion);
        var uniqueCandidates = DeduplicateCandidates(candidates);
        if (uniqueCandidates.Count == 0)
            return Array.Empty<CandidateScore>();

        NameComparisonState? CompareOptionalName(string? left, string? right) =>
            IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null
                ? null
                : IdentityComparison.CompareName(left, right, nameComparisonContract);

        return uniqueCandidates.Select(candidate =>
            {
                var nameState = CompareOptionalName(observation.NomeCompleto, candidate.NomeCompleto);
                var motherNameState = CompareOptionalName(observation.NomeMae, candidate.NomeMae);
                var rawScore = FellegiSunterScoring.CalculateRaw(
                    model.NumericParameters,
                    nameState,
                    motherNameState,
                    uniqueCandidates.Count,
                    observation.DataNascimento,
                    candidate.DataNascimento);
                rawScore = ApplyTermFrequency(
                    model,
                    observation,
                    candidate,
                    nameState,
                    motherNameState,
                    rawScore);
                var score = FellegiSunterScoring.ToContractScore(rawScore);
                var demographicExactCollisionRisk =
                    nameState == NameComparisonState.EXACT &&
                    observation.DataNascimento is { } observedBirth &&
                    candidate.DataNascimento is { } candidateBirth &&
                    observedBirth == candidateBirth;
                return new CandidateScore(
                    candidate.PessoaUuid,
                    score.Posterior,
                    score.LogOdds,
                    demographicExactCollisionRisk);
            })
            .OrderByDescending(x => decisionEvidence ? x.LogOdds : x.Score)
            .ThenBy(x => x.PessoaUuid)
            .ToArray();
    }

    private static FellegiSunterRawScore ApplyTermFrequency(
        LinkageModel model,
        IdentityObservation observation,
        LinkageCandidate candidate,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        FellegiSunterRawScore raw)
    {
        var isV8 = string.Equals(
            model.AlgorithmVersion,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            StringComparison.Ordinal);
        if (!isV8
            || !model.Parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyScoring, out var enabled)
            || enabled < 1m)
            return raw;

        var snapshot = model.TermFrequency
            ?? throw new InvalidOperationException("V8 com TF habilitado exige snapshot nominal persistido.");
        var weight = model.Parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyWeight, out var configuredWeight)
            ? configuredWeight
            : throw new InvalidOperationException("V8 com TF habilitado exige TERM_FREQUENCY_WEIGHT.");
        var minimumU = model.Parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyMinimumU, out var configuredMinimum)
            ? configuredMinimum
            : throw new InvalidOperationException("V8 com TF habilitado exige TERM_FREQUENCY_MIN_U.");

        double adjustment = 0d;
        // Splink-compatible fuzzy TF is intentional: any non-missing comparison level may receive TF.
        // The exact-level u remains the reference u unless exact-match detection is explicitly disabled;
        // Jornada's V8 contract does not expose that alternate mode.
        if (nameState is not null
            && snapshot.TryGetPersonFirstName(observation.NomeCompleto, out var leftName)
            && snapshot.TryGetPersonFirstName(candidate.NomeCompleto, out var rightName))
        {
            adjustment += SplinkCompatibleTermFrequency.LogBayesAdjustment(
                leftName,
                rightName,
                model.Parameters["U_NOME_EXACT"],
                weight,
                minimumU);
        }

        if (motherNameState is not null
            && snapshot.TryGetMotherFirstName(observation.NomeMae, out var leftMother)
            && snapshot.TryGetMotherFirstName(candidate.NomeMae, out var rightMother))
        {
            adjustment += SplinkCompatibleTermFrequency.LogBayesAdjustment(
                leftMother,
                rightMother,
                model.Parameters["U_NOME_MAE_EXACT"],
                weight,
                minimumU);
        }

        if (adjustment == 0d)
            return raw;

        var logOdds = raw.LogOdds + adjustment;
        var posterior = 1d / (1d + Math.Exp(-Math.Clamp(logOdds, -40d, 40d)));
        return new FellegiSunterRawScore(posterior, logOdds);
    }

    internal static ProbabilisticLinkageDecision Resolve(LinkageModel model, IdentityObservation observation, IReadOnlyList<LinkageCandidate> candidates)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf)) throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");
        var decisionEvidence = LinkageParameterCatalog.UsesDecisionEvidence(model.AlgorithmVersion);
        var noCandidateReason = decisionEvidence
            ? "SEM_CANDIDATO_NO_RULESET_BLOCKING"
            : LinkageModelPolicy.SupportsBirthComponentScoring(model)
                ? "SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE"
                : "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO";
        return ResolveRanked(model, Rank(model, observation, candidates), noCandidateReason);
    }

    internal static ProbabilisticLinkageDecision ResolveRanked(
        LinkageModel model,
        IReadOnlyList<CandidateScore> scored,
        string noCandidateReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noCandidateReason);
        var decisionEvidence = LinkageParameterCatalog.UsesDecisionEvidence(model.AlgorithmVersion);
        if (scored.Count == 0)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null,
                model.ModelId, noCandidateReason);

        var best = scored[0];
        var second = scored.Count > 1 ? scored[1] : null;
        if (second is not null && second.PessoaUuid == best.PessoaUuid)
            throw new InvalidOperationException("Ranking probabilístico inválido: melhor e segundo candidato possuem o mesmo UUID.");
        var secondScore = second?.Score;
        decimal? margin = null;
        if (second is not null)
        {
            if (decisionEvidence)
            {
                margin = best.LogOdds - second.LogOdds;
            }
            else
            {
                if (secondScore is null)
                    throw new InvalidOperationException("Segundo candidato sem posterior no ranking congelado.");
                margin = best.Score - secondScore.Value;
            }
        }

        if (best.Score < model.Threshold)
            return new ProbabilisticLinkageDecision(ResolutionStatus.NAO_RESOLVIDO, null, best.PessoaUuid, best.Score, second?.PessoaUuid, secondScore, margin, model.ModelId, "ABAIXO_T_LINKAGE");

        var nonUniqueDemographicExactGuard = model.Parameters.TryGetValue(
            LinkageParameterCatalog.NonUniqueDemographicExactGuard,
            out var demographicGuardFlag) && demographicGuardFlag >= 1m;
        if (nonUniqueDemographicExactGuard && best.DemographicExactCollisionRisk)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.CONFLITO, null,
                best.PessoaUuid, best.Score,
                second?.PessoaUuid, secondScore,
                margin, model.ModelId,
                "NUCLEO_DEMOGRAFICO_EXATO_NAO_UNICO");

        var dualThresholdGuard = model.Parameters.TryGetValue(
            LinkageParameterCatalog.DualThresholdConflictGuard,
            out var dualThresholdFlag) && dualThresholdFlag >= 1m;
        var independentConflictFloor = model.Parameters.TryGetValue(
            LinkageParameterCatalog.DualThresholdConflictFloorV2,
            out var floorV2Flag) && floorV2Flag >= 1m;
        var secondCandidateConflictFloor = independentConflictFloor
            ? model.Parameters[LinkageParameterCatalog.DualThresholdConflictFloor]
            : model.Threshold;
        if (dualThresholdGuard && second is not null && second.Score >= secondCandidateConflictFloor)
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.CONFLITO, null,
                best.PessoaUuid, best.Score,
                second.PessoaUuid, second.Score,
                margin, model.ModelId,
                independentConflictFloor
                    ? "SEGUNDO_CANDIDATO_ACIMA_PISO_CONFLITO"
                    : "DOIS_CANDIDATOS_ACIMA_T_LINKAGE");

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
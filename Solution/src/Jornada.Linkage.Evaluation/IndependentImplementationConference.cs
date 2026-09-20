using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Conferência independente da implementação operacional a partir de estados de comparação
/// já formados. Este componente deliberadamente NÃO deriva estados de nome/data nem o
/// flag DemographicExactCollisionRisk usado pelo guard correspondente; esses inputs
/// chegam pré-computados. Também NÃO depende de Jornada.Linkage.Runner nem Jornada.Linkage.Core.
/// </summary>
public static class IndependentImplementationConference
{
    public const string MethodVersion = "JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1";
    public const string Scope =
        "SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE";

    public static ImplementationConferenceReport Evaluate(ImplementationConferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        ArgumentNullException.ThrowIfNull(request.Candidates);
        ArgumentNullException.ThrowIfNull(request.CanonicalDecision);
        ArgumentNullException.ThrowIfNull(request.Tolerance);

        if (!request.Tolerance.TryGetFrozen(out var tolerance, out var toleranceReason))
            return NotExecuted(request, toleranceReason);

        if (!LinkageParameterCatalog.UsesDecisionEvidence(request.AlgorithmVersion))
            return NotExecuted(request, "ALGORITHM_OUTSIDE_DECISION_EVIDENCE_SCOPE");

        if (request.ModelId == Guid.Empty || request.ModelVersion <= 0)
            return NotExecuted(request, "INVALID_MODEL_ID_OR_VERSION");

        if (request.Candidates.Count == 0)
            return NotExecuted(request, "EMPTY_CONFERENCE_SAMPLE");

        if (request.Candidates.Any(static x =>
                x.CandidateId == Guid.Empty
                || x.Evidence is null
                || x.CanonicalPosterior is < 0m or > 1m))
            return NotExecuted(request, "INVALID_CANDIDATE_VECTOR");

        var candidateIds = request.Candidates.Select(static x => x.CandidateId).ToArray();
        if (candidateIds.Distinct().Count() != candidateIds.Length)
            return NotExecuted(request, "DUPLICATE_CANDIDATE_ID");

        var canonicalRanks = request.Candidates.Select(static x => x.CanonicalRank).OrderBy(static x => x).ToArray();
        if (!canonicalRanks.SequenceEqual(Enumerable.Range(1, request.Candidates.Count)))
            return NotExecuted(request, "INVALID_CANONICAL_RANKS");

        foreach (var candidate in request.Candidates)
        {
            if (!HasValidDecisionEvidenceShape(candidate.Evidence))
                return NotExecuted(request, "INVALID_EVIDENCE_VECTOR_SHAPE");
        }

        var rows = new List<ImplementationConferenceCandidateResult>(request.Candidates.Count);
        try
        {
            var prior = Required(request.Parameters, LinkageParameterCatalog.PriorMatchProbability);
            var priorLogOdds = Logit((double)prior);

            foreach (var candidate in request.Candidates)
            {
                double totalLlr = 0d;
                foreach (var evidence in candidate.Evidence)
                {
                    if (string.IsNullOrWhiteSpace(evidence.Evidence) || string.IsNullOrWhiteSpace(evidence.State))
                        return NotExecuted(request, "EMPTY_EVIDENCE_OR_STATE");

                    if (string.Equals(evidence.State, "MISSING_NEUTRAL", StringComparison.Ordinal))
                        continue;

                    var m = ClampProbability(Required(
                        request.Parameters,
                        $"M_{evidence.Evidence}_{evidence.State}"));
                    var u = ClampProbability(Required(
                        request.Parameters,
                        $"U_{evidence.Evidence}_{evidence.State}"));
                    totalLlr += Math.Log((double)m / (double)u);
                }

                var logOddsRaw = priorLogOdds + totalLlr;
                var posteriorRaw = 1d / (1d + Math.Exp(-Math.Clamp(logOddsRaw, -40d, 40d)));
                var llr = (decimal)totalLlr;
                var logOdds = Math.Round((decimal)logOddsRaw, 8, MidpointRounding.AwayFromZero);
                var posterior = Math.Round((decimal)posteriorRaw, 8, MidpointRounding.AwayFromZero);

                rows.Add(new ImplementationConferenceCandidateResult(
                    candidate.CandidateId,
                    candidate.CanonicalRank,
                    llr,
                    logOdds,
                    posterior,
                    Math.Abs(candidate.CanonicalLogLikelihoodRatio - llr),
                    Math.Abs(candidate.CanonicalLogOdds - logOdds),
                    Math.Abs(candidate.CanonicalPosterior - posterior),
                    candidate.DemographicExactCollisionRisk));
            }
        }
        catch (InvalidDataException)
        {
            return NotExecuted(request, "MODEL_OR_VECTOR_CONTRACT_INVALID");
        }
        catch (OverflowException)
        {
            return NotExecuted(request, "NUMERIC_INPUT_OUT_OF_RANGE");
        }

        var ranked = rows
            .OrderByDescending(static x => x.IndependentLogOdds)
            .ThenBy(static x => x.CandidateId)
            .Select((x, i) => x with { IndependentRank = i + 1 })
            .ToArray();

        ImplementationConferenceDecision independentDecision;
        try
        {
            independentDecision = ResolveDecision(request, ranked);
        }
        catch (InvalidDataException)
        {
            return NotExecuted(request, "MODEL_OR_VECTOR_CONTRACT_INVALID");
        }

        if (!CanonicalDecisionReferencesCandidateUniverse(request.CanonicalDecision, candidateIds))
            return NotExecuted(request, "CANONICAL_DECISION_OUTSIDE_CANDIDATE_UNIVERSE");

        var sameDecision = Equivalent(request.CanonicalDecision, independentDecision);
        var maxLlrDifference = ranked.Length == 0
            ? 0m
            : ranked.Max(static x => x.AbsoluteLlrDifference);
        var maxLogOddsDifference = ranked.Length == 0
            ? 0m
            : ranked.Max(static x => x.AbsoluteLogOddsDifference);
        var top1Same = ranked.Length == 0
            ? request.CanonicalDecision.BestCandidateId is null
            : ranked[0].CandidateId == request.Candidates.Single(x => x.CanonicalRank == 1).CandidateId;
        var spearman = Spearman(ranked);

        var withinTolerance = ranked.All(x => x.AbsoluteLlrDifference <= tolerance);
        return new ImplementationConferenceReport(
            withinTolerance && sameDecision
                ? ImplementationConferenceStatus.CONFORME
                : ImplementationConferenceStatus.DIVERGENTE,
            MethodVersion,
            Scope,
            request.ModelId,
            request.ModelVersion,
            request.AlgorithmVersion,
            request.Tolerance.Version,
            tolerance,
            ranked,
            maxLlrDifference,
            maxLogOddsDifference,
            sameDecision,
            top1Same,
            spearman,
            independentDecision,
            withinTolerance
                ? sameDecision ? null : "FINAL_DECISION_DIVERGENCE"
                : "PAIR_LLR_DIVERGENCE",
            "NOT_ASSESSED_ISSUE_31");
    }

    private static ImplementationConferenceDecision ResolveDecision(
        ImplementationConferenceRequest request,
        IReadOnlyList<ImplementationConferenceCandidateResult> ranked)
    {
        if (ranked.Count == 0)
            return new(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                null,
                null,
                "SEM_CANDIDATO_NO_RULESET_BLOCKING");

        var best = ranked[0];
        var second = ranked.Count > 1 ? ranked[1] : null;
        decimal? margin = second is null
            ? null
            : best.IndependentLogOdds - second.IndependentLogOdds;

        var threshold = Required(request.Parameters, LinkageParameterCatalog.Threshold);
        if (best.IndependentPosterior < threshold)
            return new(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                best.CandidateId,
                second?.CandidateId,
                "ABAIXO_T_LINKAGE");

        var nonUniqueDemographicExactGuard =
            Enabled(request.Parameters, LinkageParameterCatalog.NonUniqueDemographicExactGuard);
        if (nonUniqueDemographicExactGuard && best.DemographicExactCollisionRisk)
            return new(
                ResolutionStatus.CONFLITO,
                null,
                best.CandidateId,
                second?.CandidateId,
                "NUCLEO_DEMOGRAFICO_EXATO_NAO_UNICO");

        var dualThresholdGuard =
            Enabled(request.Parameters, LinkageParameterCatalog.DualThresholdConflictGuard);
        var independentConflictFloor =
            Enabled(request.Parameters, LinkageParameterCatalog.DualThresholdConflictFloorV2);
        var secondCandidateConflictFloor = independentConflictFloor
            ? Required(request.Parameters, LinkageParameterCatalog.DualThresholdConflictFloor)
            : threshold;

        if (dualThresholdGuard
            && second is not null
            && second.IndependentPosterior >= secondCandidateConflictFloor)
            return new(
                ResolutionStatus.CONFLITO,
                null,
                best.CandidateId,
                second.CandidateId,
                independentConflictFloor
                    ? "SEGUNDO_CANDIDATO_ACIMA_PISO_CONFLITO"
                    : "DOIS_CANDIDATOS_ACIMA_T_LINKAGE");

        var conflictMargin = Required(request.Parameters, LinkageParameterCatalog.LogOddsConflictMargin);
        if (second is not null && margin!.Value < conflictMargin)
            return new(
                ResolutionStatus.CONFLITO,
                null,
                best.CandidateId,
                second.CandidateId,
                "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE");

        return new(
            ResolutionStatus.RESOLVIDO,
            best.CandidateId,
            best.CandidateId,
            second?.CandidateId,
            null);
    }

    private static bool CanonicalDecisionReferencesCandidateUniverse(
        ImplementationConferenceDecision decision,
        IReadOnlyCollection<Guid> candidateIds)
    {
        var known = candidateIds.ToHashSet();
        return (decision.ResolvedCandidateId is null || known.Contains(decision.ResolvedCandidateId.Value))
            && (decision.BestCandidateId is null || known.Contains(decision.BestCandidateId.Value))
            && (decision.SecondCandidateId is null || known.Contains(decision.SecondCandidateId.Value));
    }

    private static bool Equivalent(
        ImplementationConferenceDecision expected,
        ImplementationConferenceDecision actual) =>
        expected.Status == actual.Status
        && expected.ResolvedCandidateId == actual.ResolvedCandidateId
        && expected.BestCandidateId == actual.BestCandidateId
        && expected.SecondCandidateId == actual.SecondCandidateId
        && string.Equals(expected.Reason, actual.Reason, StringComparison.Ordinal);

    private static decimal? Spearman(IReadOnlyList<ImplementationConferenceCandidateResult> ranked)
    {
        if (ranked.Count < 2)
            return ranked.Count == 1 ? 1m : null;

        var n = ranked.Count;
        decimal sumSquared = 0m;
        foreach (var row in ranked)
        {
            var d = row.CanonicalRank - row.IndependentRank;
            sumSquared += d * d;
        }

        return 1m - (6m * sumSquared) / (n * (n * n - 1m));
    }

    private static bool HasValidDecisionEvidenceShape(
        IReadOnlyList<ImplementationConferenceEvidence> evidence)
    {
        if (evidence.Count != 3)
            return false;

        if (evidence.Count(x => string.Equals(x.Evidence, "NOME", StringComparison.Ordinal)) != 1)
            return false;
        if (evidence.Count(x => string.Equals(x.Evidence, "NOME_MAE", StringComparison.Ordinal)) != 1)
            return false;

        var birth = evidence.Where(x =>
            string.Equals(x.Evidence, "NASCIMENTO_SEMANTICO", StringComparison.Ordinal)
            || string.Equals(x.Evidence, "NASCIMENTO", StringComparison.Ordinal)).ToArray();
        if (birth.Length != 1)
            return false;

        return string.Equals(birth[0].Evidence, "NASCIMENTO_SEMANTICO", StringComparison.Ordinal)
            || string.Equals(birth[0].State, "MISSING_NEUTRAL", StringComparison.Ordinal);
    }

    private static bool Enabled(IReadOnlyDictionary<string, decimal> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value >= 1m;

    private static decimal Required(IReadOnlyDictionary<string, decimal> parameters, string name) =>
        parameters.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"Parâmetro ausente na conferência independente: {name}.");

    private static decimal ClampProbability(decimal value) =>
        Math.Clamp(value, 0.000000001m, 0.999999999m);

    private static double Logit(double probability)
    {
        var p = Math.Clamp(probability, 0.0000001d, 0.9999999d);
        return Math.Log(p / (1d - p));
    }

    private static ImplementationConferenceReport NotExecuted(
        ImplementationConferenceRequest request,
        string reason) =>
        new(
            ImplementationConferenceStatus.NAO_EXECUTADA,
            MethodVersion,
            Scope,
            request.ModelId,
            request.ModelVersion,
            request.AlgorithmVersion,
            request.Tolerance.Version,
            null,
            [],
            null,
            null,
            false,
            false,
            null,
            null,
            reason,
            "NOT_ASSESSED_ISSUE_31");
}

public enum ImplementationConferenceStatus
{
    NAO_EXECUTADA,
    CONFORME,
    DIVERGENTE
}

public sealed record ImplementationConferenceToleranceContract(
    string Version,
    string Status,
    decimal? MaxAbsolutePairLlrDifference)
{
    public bool TryGetFrozen(out decimal tolerance, out string reason)
    {
        tolerance = default;
        if (!string.Equals(Status, "FROZEN", StringComparison.Ordinal))
        {
            reason = "TOLERANCE_NOT_FROZEN";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version)
            || MaxAbsolutePairLlrDifference is null
            || MaxAbsolutePairLlrDifference < 0m)
        {
            reason = "INVALID_FROZEN_TOLERANCE";
            return false;
        }

        tolerance = MaxAbsolutePairLlrDifference.Value;
        reason = string.Empty;
        return true;
    }
}

public sealed record ImplementationConferenceEvidence(string Evidence, string State);

public sealed record ImplementationConferenceCandidate(
    Guid CandidateId,
    int CanonicalRank,
    IReadOnlyList<ImplementationConferenceEvidence> Evidence,
    bool DemographicExactCollisionRisk,
    decimal CanonicalLogLikelihoodRatio,
    decimal CanonicalLogOdds,
    decimal CanonicalPosterior);

public sealed record ImplementationConferenceDecision(
    ResolutionStatus Status,
    Guid? ResolvedCandidateId,
    Guid? BestCandidateId,
    Guid? SecondCandidateId,
    string? Reason);

public sealed record ImplementationConferenceRequest(
    Guid ModelId,
    int ModelVersion,
    string AlgorithmVersion,
    IReadOnlyDictionary<string, decimal> Parameters,
    IReadOnlyList<ImplementationConferenceCandidate> Candidates,
    ImplementationConferenceDecision CanonicalDecision,
    ImplementationConferenceToleranceContract Tolerance);

public sealed record ImplementationConferenceCandidateResult(
    Guid CandidateId,
    int CanonicalRank,
    decimal IndependentLogLikelihoodRatio,
    decimal IndependentLogOdds,
    decimal IndependentPosterior,
    decimal AbsoluteLlrDifference,
    decimal AbsoluteLogOddsDifference,
    decimal AbsolutePosteriorDifference,
    bool DemographicExactCollisionRisk)
{
    public int IndependentRank { get; init; }
}

public sealed record ImplementationConferenceReport(
    ImplementationConferenceStatus Status,
    string MethodVersion,
    string Scope,
    Guid ModelId,
    int ModelVersion,
    string AlgorithmVersion,
    string ToleranceVersion,
    decimal? MaxAllowedPairLlrDifference,
    IReadOnlyList<ImplementationConferenceCandidateResult> Candidates,
    decimal? MaxObservedPairLlrDifference,
    decimal? MaxObservedLogOddsDifference,
    bool SameFinalDecision,
    bool SameTop1,
    decimal? SpearmanRankCorrelation,
    ImplementationConferenceDecision? IndependentDecision,
    string? Reason,
    string StatisticalValidation);

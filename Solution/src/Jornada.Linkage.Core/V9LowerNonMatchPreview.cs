using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Diagnostic decision AFTER the shared frozen V9 Rank/Resolve kernel.
/// No operational dispatch, no SQL publication, no replacement of V8.
/// In particular, rejecting all retrieved candidates is NOT evidence that
/// a person's identity is absent from the full Gold reference universe.
/// </summary>
public static class V9LowerNonMatchPreview
{
    public const string Version = "V9_TRAIN_LOWER_NON_MATCH_PREVIEW_V1";

    public enum Disposition
    {
        ExistingLink,
        ExistingNoCandidateRule,
        CandidateRejectedNewReferenceHypothesis,
        PossibleLink,
        TechnicalAbstention
    }

    public sealed record VerifiedRetrieval(
        string SnapshotSha256,
        string CandidateFrameSha256,
        int CandidateCount,
        bool IndexMaterializationVerified,
        bool CompleteCandidateUniverse,
        bool EligibleBlockingPasses);

    public sealed record Result(Disposition Decision, string Reason,
        bool AllowedToPublishNewReference, bool OperationalActivation);

    public static Result Evaluate(
        ProbabilisticLinkageDecision original,
        VerifiedRetrieval retrieval,
        string expectedSnapshotSha256,
        decimal lowerThreshold,
        decimal fixedUpperThreshold)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(retrieval);
        CheckSha(expectedSnapshotSha256, "frozen fitted snapshot");
        CheckSha(retrieval.SnapshotSha256, "candidate snapshot");
        CheckSha(retrieval.CandidateFrameSha256, "retrieval frame");
        if (!string.Equals(expectedSnapshotSha256, retrieval.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ranking e quadro de candidatos não pertencem ao mesmo fit V9.");
        if (lowerThreshold is <= 0m or >= 1m || fixedUpperThreshold is <= 0m or > 1m
            || lowerThreshold >= fixedUpperThreshold)
            throw new ArgumentOutOfRangeException(nameof(lowerThreshold), "Exigir 0 < piso < limiar superior.");
        if (retrieval.CandidateCount < 0 || original.MelhorScore is < 0m or > 1m)
            throw new InvalidDataException("Quadro físico ou posterior inválidos.");
        // The same V9 scorer and existing-link decision remain authoritative:
        // never manufacture an accepted link from a low-threshold classifier.
        if (original.Status == ResolutionStatus.RESOLVIDO)
            return new(Disposition.ExistingLink, "DECISAO_SUPERIOR_ORIGINAL_PRESERVADA",
                AllowedToPublishNewReference: false, OperationalActivation: false);
        if (original.Status == ResolutionStatus.CONFLITO)
            return new(Disposition.PossibleLink, "CONFLITO_ORIGINAL_NAO_SUPRIMIDO",
                AllowedToPublishNewReference: false, OperationalActivation: false);
        if (original.Status != ResolutionStatus.NAO_RESOLVIDO)
            throw new InvalidDataException("Estado FS desconhecido.");
        if (!retrieval.IndexMaterializationVerified || !retrieval.CompleteCandidateUniverse
            || !retrieval.EligibleBlockingPasses)
            return new(Disposition.TechnicalAbstention, "BUSCA_OU_EVIDENCIA_INCOMPLETA",
                AllowedToPublishNewReference: false, OperationalActivation: false);
        if (retrieval.CandidateCount == 0)
        {
            if (original.MelhorCandidatoUuid is not null || original.Motivo is null
                || !original.Motivo.StartsWith("SEM_CANDIDATO_", StringComparison.Ordinal))
                throw new InvalidDataException("Busca vazia não corresponde ao motivo de ausência de candidato.");
            // The existing SQL progressive policy still owns this decision.
            return new(Disposition.ExistingNoCandidateRule, "POLITICA_SEM_CANDIDATO_EXISTENTE",
                AllowedToPublishNewReference: false, OperationalActivation: false);
        }
        if (original.MelhorCandidatoUuid is null)
            throw new InvalidDataException("Busca com candidatos exige melhor candidato em Rank.");
        if (!string.Equals(original.Motivo, "ABAIXO_T_LINKAGE", StringComparison.Ordinal))
            return new(Disposition.PossibleLink, "MOTIVO_NAO_ELEGIVEL_PARA_REJEICAO",
                AllowedToPublishNewReference: false, OperationalActivation: false);
        if (original.MelhorScore >= fixedUpperThreshold)
            throw new InvalidDataException("Resultado abaixo do limiar contradiz o limiar superior congelado.");
        return original.MelhorScore < lowerThreshold
            ? new(Disposition.CandidateRejectedNewReferenceHypothesis,
                "TODOS_CANDIDATOS_RECUPERADOS_REJEITADOS_NAO_PROVA_AUSENCIA_DO_GOLD",
                AllowedToPublishNewReference: false, OperationalActivation: false)
            : new(Disposition.PossibleLink, "FAIXA_INTERMEDIARIA_POSSIVEL",
                AllowedToPublishNewReference: false, OperationalActivation: false);
    }

    private static void CheckSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("SHA-256 inválido: " + label);
    }
}

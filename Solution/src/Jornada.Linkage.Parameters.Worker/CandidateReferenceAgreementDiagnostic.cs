using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record CandidateReferenceJudgment(
    Guid SourceId,
    Guid CandidateId,
    string IndependenceUnitFingerprint,
    IndependentMatchLabel Decision,
    IndependentLabelMethod Method,
    string EvidenceReference,
    string EvidenceFingerprint,
    DateTimeOffset DecidedAt);

public sealed record CandidateReferenceAgreementEvidence(
    string Reference,
    string LabelingReference,
    string FrameFingerprint,
    string SelectionFingerprint,
    DateTimeOffset AttestedAt,
    IReadOnlyList<CandidateReferenceJudgment> Judgments);

public sealed record CandidateReferenceAgreementMethodCount(
    IndependentLabelMethod Method,
    int Judgments,
    int Pairs);

public sealed record CandidateReferenceAgreementReport(
    string Version,
    string Reference,
    string LabelingReference,
    string FrameFingerprint,
    string SelectionFingerprint,
    DateTimeOffset AttestedAt,
    int EvaluationPairs,
    int AssessedPairs,
    int ReplicatedPairs,
    int UnanimousPairs,
    int DiscordantPairs,
    int PairsWithInconclusiveJudgment,
    int DistinctIndependenceUnits,
    int DistinctEvidenceArtifacts,
    int PairwiseComparisons,
    int PairwiseAgreements,
    decimal? PairwiseAgreement,
    int ConclusivePairwiseComparisons,
    int ConclusivePairwiseAgreements,
    decimal? ConclusivePairwiseAgreement,
    decimal EvaluationDesignWeight,
    decimal AssessedDesignWeight,
    decimal ReplicatedDesignWeight,
    decimal AssessedWeightCoverage,
    decimal ReplicatedWeightCoverage,
    IReadOnlyList<CandidateReferenceAgreementMethodCount> MethodCounts,
    string FingerprintSha256);

public static class CandidateReferenceAgreementDiagnostic
{
    public const string Version = "CANDIDATE_REFERENCE_AGREEMENT_DIAGNOSTIC_V1";
}

using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Ephemeral in-memory observations from the physical BASELINE Gold TRAIN run.
/// Never written as per-source JSON and never consumed by operational services.
/// The original FS decision and complete prepublication retrieval are captured
/// before each wave is published; true identity is joined AFTER publication.
/// </summary>
public sealed record V9GoldTrainPhysicalEvidence(
    string SourceFingerprintSha256,
    string IndependentBaseIdentityFingerprintSha256,
    bool GoldAlreadyContainsTrueIdentity,
    ProbabilisticLinkageDecision OriginalDecision,
    V9LowerNonMatchPreview.VerifiedRetrieval Retrieval);

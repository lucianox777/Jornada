using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Aggregate scientific evidence, never a production approval credential.</summary>
public sealed record V9CalibrationValidationEvidence(
    long TruePositive, long FalsePositive, long TrueEligibleBeforeBlocking,
    long Abstentions, long IndependentAcceptedIdentities,
    decimal RecallGlobal, decimal Fdr, decimal FdrUpperBound,
    decimal RecallLowerBound, decimal RecallUpperBound,
    string StatisticalMethod, string ProtocolSha256, string EnvironmentSha256,
    string DecisionsSha256, decimal PhysicalP95Milliseconds);

public sealed record V9CalibrationFreezePolicy(
    string Version, decimal MaximumP95Milliseconds, decimal MaximumFdrUpperBound,
    long MinimumIndependentAcceptedIdentities, string AbstentionPolicy,
    decimal MinimumGlobalRecall = 0m);

/// <summary>Contains the complete catalog, including unavailable and ineligible fields.</summary>
public sealed record V9CalibrationCatalogEntry(
    string Code, string Semantic, bool EligibleForResolution, bool MultiValued,
    string? CompatibilityProfile);

/// <summary>The grouping values must be those actually consumed by the measured complete-link run.</summary>
public sealed record V9CalibrationGroupingConfiguration(
    string CandidateGrouping, decimal MinimumPairPosterior, int MaximumObservationsPerIdentity);

public sealed record V9CalibrationFreezeMetadata(
    string Version, string? PredecessorEnvelopeSha256,
    string Origin, string Actor, DateTimeOffset CreatedUtc,
    string SelectorVersion, string SelectionReportSha256,
    string CatalogVersion, IReadOnlyList<V9CalibrationCatalogEntry> Catalog,
    V9CalibrationFreezePolicy Policy, V9CalibrationValidationEvidence Validation,
    string SelectedConfigurationId, V9CalibrationGroupingConfiguration Grouping);

public sealed record V9CalibrationSnapshotEnvelope(
    string SchemaVersion, string FittedSnapshotSha256,
    V9CalibrationFreezeMetadata Metadata, V9BootstrapPareto2D.Result Selection);

public sealed record V9FrozenCalibration(
    V9CalibrationSnapshotEnvelope Envelope, string EnvelopeSha256,
    V9FittedSnapshot FittedSnapshot);

/// <summary>
/// Fixed before labels are opened. Attestation hashes refer to independently reviewed
/// sampling/physical evidence; this service neither authenticates nor creates attestations.
/// Their absence makes TEST inconclusive. A SHA alone never proves IID sampling.
/// </summary>
public sealed record V9IndependentTestProtocol(
    double Alpha, int PredeclaredMaximumAcceptedPerIdentity,
    string? IndependentSamplingAttestationSha256, string? PhysicalMeasurementAttestationSha256);

/// <summary>Reserved before opening TEST labels. A failed/interrupted run cannot be rerolled.</summary>
public sealed record V9IndependentTestReservation(
    string StudyId, string RunId, string EnvelopeSha256, string FittedSnapshotSha256,
    int Seed, string CorpusSha256, string IdentityGroupsSha256,
    string ProtocolSha256, DateTimeOffset ReservedUtc, V9IndependentTestProtocol Protocol);

/// <summary>Whole preblocking identity frame, including zero-accept identities.</summary>
public sealed record V9IndependentTestEvidence(
    string CorpusSha256, string IdentityGroupsSha256,
    V9CalibrationValidationEvidence Metrics,
    IReadOnlyList<V9IdentityClusterFdr.IdentityGroup> IdentityGroups);

public sealed record V9IndependentTestReceipt(
    V9IndependentTestReservation Reservation, string EvidenceSha256,
    bool Passed, string Outcome, DateTimeOffset CompletedUtc,
    V9IndependentTestEvidence Evidence);

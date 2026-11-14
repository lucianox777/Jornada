using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Production-RUNNER-shaped accounting for a strictly synthetic, nonpublishing
/// V9 batch rehearsal. No run row exists: ExpectedTerminalStatus is only a
/// validated expected transition, not a persisted linkage_run.status.
/// Full legacy production batch equivalence remains uncertified.
/// </summary>
public sealed record V9NonPublishingBatchExecutionReceipt(
    Guid PinnedModelId,
    string WinnerSha256,
    LinkageRunStatus ExpectedTerminalStatus,
    int Eligible,
    int Evaluated,
    int Resolved,
    int Unresolved,
    int Conflicts,
    int NoCandidateInBlock,
    int BatchSize,
    int BatchCount,
    string CandidateGraphSha256,
    string DecisionsSha256,
    string IdentityGroupsSha256,
    bool PersistedRun = false,
    bool Published = false,
    bool ProductionBatchPipelineCertified = false);

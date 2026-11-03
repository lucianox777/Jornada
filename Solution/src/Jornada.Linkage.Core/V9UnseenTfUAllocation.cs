namespace Jornada.Linkage.Runner;

/// <summary>
/// Snapshot deserialization compatibility only. V9 positive TF strictly
/// rejects this retired IBGE-proportional residual U allocation, even for
/// snapshots containing an otherwise valid pinned IBGE reference.
/// </summary>
public sealed record V9UnseenTfUAllocation(
    string PolicyVersion, decimal ReservedUProbability, decimal UnseenReferenceMass)
{
    public const string LegacyPolicyVersion = "V9_TRAIN_U_UNSEEN_IBGE_CONDITIONAL_V1";
}

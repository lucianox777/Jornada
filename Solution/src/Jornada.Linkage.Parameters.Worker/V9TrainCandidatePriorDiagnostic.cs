using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Diagnostic only. A SQL observation edge is one unit. Repeated base identities
/// are not repeated SQL edges; never silently discard them from M/U calibration.
/// </summary>
public static class V9TrainCandidatePriorDiagnostic
{
    public const string Version = "V9_TRAIN_CANDIDATE_PRIOR_DENOMINATOR_AUDIT_V1";

    public sealed record Report(
        string Version, string PhysicalCandidatePairsSha256,
        long TrainCandidateObservationPairs, long TrainTrueObservationPairs,
        long TrainFalseObservationPairs, decimal RawPairCandidateMatchShare,
        long DistinctMBaseIdentities, long DistinctUnorderedUBasePairs,
        long RepeatedMBaseIdentityContributions, long RepeatedUBasePairContributions,
        long MaximumMObservationPairsFromOneBase,
        long MaximumUObservationPairIncidencesOnOneBase,
        decimal BaseCollapsedShareSensitivityOnly,
        int PreBlockingBaseIdentityCount, int BaseIdentitiesRepresentedInTrainPairs,
        bool SqlPhysicalObservationEdgesDeduplicated,
        bool BaseCollapsedShareIsCalibratedPrior = false,
        bool TransportabilityToCpfAbsentPopulationEstablished = false,
        bool IndependentIdentitySupportEstablished = false,
        bool ChangesFittedPriorOrDecisions = false);

    public static Report Analyze(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> physicalTrainPairs,
        int preBlockingIdentityCount, string physicalSqlCandidatePairsSha256)
    {
        ArgumentNullException.ThrowIfNull(physicalTrainPairs);
        if (preBlockingIdentityCount < 2
            || physicalSqlCandidatePairsSha256 is not { Length: 64 }
            || !physicalSqlCandidatePairsSha256.All(Uri.IsHexDigit)
            || physicalTrainPairs.Count == 0)
            throw new InvalidDataException("Prior diagnóstico exige censo TRAIN SQL, SHA e pessoas-base.");

        long m = 0, u = 0;
        var mBaseCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var uBaseIncidences = new Dictionary<string, long>(StringComparer.Ordinal);
        var uBasePairs = new HashSet<(string Left, string Right)>();
        var represented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in physicalTrainPairs)
        {
            if (row is null || row.Partition != "TRAIN"
                || string.IsNullOrWhiteSpace(row.LeftIdentityGroupId)
                || string.IsNullOrWhiteSpace(row.RightIdentityGroupId)
                || row.CandidateDegreeForSource <= 0
                || row.IsTruePair != (row.LeftIdentityGroupId == row.RightIdentityGroupId))
                throw new InvalidDataException("Prior diagnóstico recebeu par fora do TRAIN ou rótulo inconsistente.");
            represented.Add(row.LeftIdentityGroupId);
            represented.Add(row.RightIdentityGroupId);
            if (row.IsTruePair)
            {
                ++m;
                mBaseCounts[row.LeftIdentityGroupId] =
                    checked(mBaseCounts.GetValueOrDefault(row.LeftIdentityGroupId) + 1);
            }
            else
            {
                ++u;
                var a = row.LeftIdentityGroupId;
                var b = row.RightIdentityGroupId;
                uBasePairs.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a));
                uBaseIncidences[a] = checked(uBaseIncidences.GetValueOrDefault(a) + 1);
                uBaseIncidences[b] = checked(uBaseIncidences.GetValueOrDefault(b) + 1);
            }
        }

        if (m < 1 || u < 1 || represented.Count > preBlockingIdentityCount)
            throw new InvalidDataException("Prior diagnóstico sem classes M/U ou censo pré-blocking inconsistente.");
        var collapsedTotal = checked(mBaseCounts.Count + uBasePairs.Count);
        return new Report(
            Version, physicalSqlCandidatePairsSha256.ToLowerInvariant(),
            checked(m + u), m, u, (decimal)m / (m + u),
            mBaseCounts.Count, uBasePairs.Count,
            m - mBaseCounts.Count, u - uBasePairs.Count,
            mBaseCounts.Values.Max(), uBaseIncidences.Values.Max(),
            (decimal)mBaseCounts.Count / collapsedTotal,
            preBlockingIdentityCount, represented.Count,
            SqlPhysicalObservationEdgesDeduplicated: true);
    }

    /// <summary>
    /// Reconcile the frozen *original* fitted prior. Does not promote a new one.
    /// </summary>
    public static void RequireOriginalPriorReconciled(
        Report diagnostic, V9FittedSnapshot snapshot, decimal alpha)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (diagnostic.Version != Version || !diagnostic.SqlPhysicalObservationEdgesDeduplicated
            || alpha <= 0 || diagnostic.TrainTrueObservationPairs < 1
            || diagnostic.TrainFalseObservationPairs < 1
            || diagnostic.TrainCandidateObservationPairs !=
                diagnostic.TrainTrueObservationPairs + diagnostic.TrainFalseObservationPairs)
            throw new InvalidDataException("Diagnóstico/proveniência inválido para reconciliar prior TRAIN.");
        var expected = (diagnostic.TrainTrueObservationPairs + alpha)
            / (diagnostic.TrainCandidateObservationPairs + 2m * alpha);
        if (!snapshot.Payload.Parameters.TryGetValue(LinkageParameterCatalog.PriorMatchProbability, out var actual)
            || expected != actual)
            throw new InvalidDataException("Prior do snapshot não reconcilia com M/U do frame SQL físico.");
    }
}

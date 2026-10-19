using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9PostResolveSelectionReport(
    string Version, string StudyFingerprintSha256, string EvidenceFingerprintSha256,
    string Status, V9PhysicalSourceResolveMetric? SuggestedDiagnosticConfiguration,
    V9PhysicalSourceResolveMetric? BestDescriptiveConfiguration,
    int ComparedConfigurations, int ExcludedOracleConfigurations,
    bool TestUsed, bool OperationalModelCreated, bool AutomaticActivationAllowed);

/// <summary>
/// Selects a diagnostic configuration using ONE final Resolve decision per
/// source observation, including eligible sources lost before SQL blocking.
/// Candidate-pair recall and oracle groupings never participate in selection.
/// Statistical bounds remain conditional; this type has no activation path.
/// </summary>
public static class V9PostResolveAdaptiveSelector
{
    public const string Version = "V9_POST_RESOLVE_SOURCE_SELECTION_V1";

    public static V9PostResolveSelectionReport EvaluateValidation(
        string partition, bool usesTestLabels,
        AdaptiveCalibrationPreviewBudget budget,
        IReadOnlyList<V9PhysicalSourceResolveMetric> measurements,
        IReadOnlyDictionary<string, double>? blockingFalseDegreeP99ByOption = null)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(measurements);
        // Report rows must remain consistent with their evidence digest even
        // if the caller later mutates its input dictionaries or list.
        measurements = Array.AsReadOnly(measurements.Select(row => row is null ? null! : row with
        {
            ReasonsByResolveStatus = row.ReasonsByResolveStatus is null ? null!
                : new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(
                    row.ReasonsByResolveStatus.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal))
        }).ToArray());
        blockingFalseDegreeP99ByOption = blockingFalseDegreeP99ByOption is null ? null
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(
                blockingFalseDegreeP99ByOption.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        if (partition != "VALIDATION" || usesTestLabels)
            throw new InvalidDataException("Seleção exige VALIDATION; TEST é proibido.");
        if (budget.Version != AdaptiveCalibrationPreviewBudget.CurrentVersion
            || budget.StudyFingerprintSha256 is not { Length: 64 }
            || !budget.StudyFingerprintSha256.All(Uri.IsHexDigit)
            || !double.IsFinite(budget.MinimumGlobalAcceptedRecall)
            || budget.MinimumGlobalAcceptedRecall is < 0 or > 1
            || !double.IsFinite(budget.MaximumObservedAcceptedFdrPoint)
            || budget.MaximumObservedAcceptedFdrPoint is < 0 or > .01
            || budget.MinimumValidationIdentityGroups < 2
            || !double.IsFinite(budget.MaximumBlockingFalseDegreeP99)
            || budget.MaximumBlockingFalseDegreeP99 < 0)
            throw new InvalidDataException("Orçamento de seleção inválido.");
        if (measurements.Count == 0)
            throw new InvalidDataException("Decisões finais por fonte obrigatórias.");

        var keys = new HashSet<(string, string, decimal)>();
        var configurations = new Dictionary<string, (decimal Threshold, decimal TfWeight)>(StringComparer.Ordinal);
        foreach (var row in measurements)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.OptionId)
                || !keys.Add((row.OptionId, row.CandidateGrouping, row.ConflictMargin))
                || row.Threshold is <= 0 or >= 1 || row.TfWeight is < 0 or > 1
                || row.ConflictMargin is < 0 or > 1000
                || row.AllValidationObservations <= 0
                || row.EligibleSourceObservationsBeforeBlocking <= 0
                || row.EligibleSourceObservationsBeforeBlocking > row.AllValidationObservations
                || row.EligibleSourceObservationsWithTrueSqlCandidate < 0
                || row.EligibleSourceObservationsWithTrueSqlCandidate > row.EligibleSourceObservationsBeforeBlocking
                || row.TrueAcceptedSourceObservations < 0
                || row.TrueAcceptedSourceObservations > row.EligibleSourceObservationsWithTrueSqlCandidate
                || row.FalseAcceptedSourceObservations < 0 || row.Conflicts < 0 || row.Unresolved < 0
                || row.PreBlockingBaseIdentities < 2 || row.ImpureBlindGroups < 0
                || row.ReasonsByResolveStatus is null
                || row.OperationallyCertified
                || row.SyntheticGoldOracleGrouping != (row.CandidateGrouping == "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL")
                || row.CandidateGrouping is not ("RAW_SQL_OBSERVATION_CANDIDATES"
                    or "BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING"
                    or "BLIND_FS_COMPLETE_LINK_CANDIDATES"
                    or "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL"))
                throw new InvalidDataException("Métricas finais por fonte inválidas ou agrupamento desconhecido.");
            var config = (row.Threshold, row.TfWeight);
            if (configurations.TryGetValue(row.OptionId, out var previous) && previous != config)
                throw new InvalidDataException("OptionId reutilizado com parâmetros diferentes.");
            configurations[row.OptionId] = config;
            if (checked(row.TrueAcceptedSourceObservations + row.FalseAcceptedSourceObservations
                + row.Conflicts + row.Unresolved) != row.AllValidationObservations)
                throw new InvalidDataException("Decisões finais não reconciliam por fonte.");
            if (row.ReasonsByResolveStatus.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value <= 0)
                || row.ReasonsByResolveStatus.Values.Sum() != row.AllValidationObservations)
                throw new InvalidDataException("Motivos finais não reconciliam com as decisões por fonte.");
            var recall = (double)row.TrueAcceptedSourceObservations / row.EligibleSourceObservationsBeforeBlocking;
            var blocking = (double)row.EligibleSourceObservationsWithTrueSqlCandidate / row.EligibleSourceObservationsBeforeBlocking;
            var accepted = checked(row.TrueAcceptedSourceObservations + row.FalseAcceptedSourceObservations);
            double? fdr = accepted == 0 ? null : (double)row.FalseAcceptedSourceObservations / accepted;
            if (!Equal(row.GlobalSourceRecall, recall) || !Equal(row.BlockingSourceRecall, blocking)
                || row.FdrAfterResolve.HasValue != fdr.HasValue
                || (fdr.HasValue && !Equal(row.FdrAfterResolve!.Value, fdr.Value))
                || !ValidBound(row.HoeffdingUpperBySourceBaseIdentity)
                || !ValidBound(row.ConditionalClusterCpFdrUpper)
                || (fdr.HasValue && row.ConditionalClusterCpFdrUpper.HasValue
                    && row.ConditionalClusterCpFdrUpper.Value + 1e-12 < fdr.Value))
                throw new InvalidDataException("Razões ou limites não reconciliam com as contagens.");
        }
        var first = measurements[0];
        if (measurements.Any(x => x.AllValidationObservations != first.AllValidationObservations
            || x.EligibleSourceObservationsBeforeBlocking != first.EligibleSourceObservationsBeforeBlocking
            || x.PreBlockingBaseIdentities != first.PreBlockingBaseIdentities))
            throw new InvalidDataException("Configurações devem usar a mesma coorte pré-blocking.");

        // This ranks measured final outcomes only. Even independently attested
        // IID clusters would not prove operational parity, provenance or TEST.
        var descriptive = measurements.Where(x => !x.SyntheticGoldOracleGrouping
                && blockingFalseDegreeP99ByOption is not null
                && blockingFalseDegreeP99ByOption.TryGetValue(x.OptionId, out var fanout)
                && double.IsFinite(fanout) && fanout >= 0
                && fanout <= budget.MaximumBlockingFalseDegreeP99
                && x.GlobalSourceRecall >= budget.MinimumGlobalAcceptedRecall
                && x.FdrAfterResolve.HasValue
                && x.FdrAfterResolve <= budget.MaximumObservedAcceptedFdrPoint
                && x.PreBlockingBaseIdentities >= budget.MinimumValidationIdentityGroups)
            .OrderByDescending(x => x.GlobalSourceRecall)
            .ThenBy(x => x.FdrAfterResolve)
            .ThenBy(x => x.Conflicts).ThenBy(x => x.Unresolved)
            .ThenBy(x => x.CandidateGrouping, StringComparer.Ordinal)
            .ThenBy(x => x.OptionId, StringComparer.Ordinal)
            .ThenBy(x => x.ConflictMargin).ToArray();
        // A point estimate can be displayed for comparison but cannot become
        // a suggestion without independently supported cluster assumptions and
        // a multiplicity-adjusted bound covering every compared configuration.
        var suggestion = descriptive.FirstOrDefault(x => x.ClusterIidIndependentlyValidated
            && x.CandidateGrouping == "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            && x.ConflictMargin > 0
            && x.BonferroniComparedConfigurations >= measurements.Count
            && x.ConditionalClusterCpFdrUpper.HasValue
            && x.ConditionalClusterCpFdrUpper <= budget.MaximumObservedAcceptedFdrPoint);
        var canonical = new StringBuilder().Append(Version).Append('\n')
            .Append(budget.StudyFingerprintSha256).Append('\n')
            .Append(budget.MinimumGlobalAcceptedRecall.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(budget.MaximumObservedAcceptedFdrPoint.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(budget.MaximumBlockingFalseDegreeP99.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(budget.MinimumValidationIdentityGroups).Append('\n');
        if (blockingFalseDegreeP99ByOption is not null)
            foreach (var pair in blockingFalseDegreeP99ByOption.OrderBy(x => x.Key, StringComparer.Ordinal))
                canonical.Append("FANOUT:").Append(pair.Key).Append('|')
                    .Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var row in measurements.OrderBy(x => x.OptionId, StringComparer.Ordinal)
                     .ThenBy(x => x.CandidateGrouping, StringComparer.Ordinal).ThenBy(x => x.ConflictMargin))
            canonical.Append(System.Text.Json.JsonSerializer.Serialize(row with
            {
                ReasonsByResolveStatus = row.ReasonsByResolveStatus
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
            })).Append('\n');
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        return new(Version, budget.StudyFingerprintSha256, fingerprint,
            descriptive.Length == 0 ? "NO_SOURCE_CONFIGURATION_MEETS_DESCRIPTIVE_BUDGET"
                : suggestion is null ? "NO_SOURCE_SUGGESTION_WITHOUT_SUPPORTED_CLUSTER_FDR_BOUND"
                : "SOURCE_SUGGESTION_ONLY_UNCERTIFIED_CLUSTER_FDR_PHYSICAL_PARITY_AND_TEST",
            suggestion, descriptive.FirstOrDefault(), measurements.Count, measurements.Count(x => x.SyntheticGoldOracleGrouping),
            TestUsed: false, OperationalModelCreated: false, AutomaticActivationAllowed: false);
    }

    private static bool Equal(double actual, double expected) =>
        double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-12;
    private static bool ValidBound(double? value) =>
        !value.HasValue || double.IsFinite(value.Value) && value.Value is >= 0 and <= 1;
}

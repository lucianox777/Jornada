using System.Globalization;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Evidence for a complete configuration measured on the same preblocking source cohort.</summary>
public sealed record V9ParetoPhysicalEvidence(string OptionId, string CandidateGrouping, decimal ConflictMargin,
    string ConfigurationFingerprintSha256, string TrainingCorpusFingerprintSha256,
    string ValidationCohortFingerprintSha256, string SourceEvidenceFingerprintSha256,
    string MeasurementProtocolFingerprintSha256, string MeasurementEnvironmentFingerprintSha256,
    double? PhysicalEndToEndP95Milliseconds, bool PhysicalEndToEndAttested);

public sealed record V9ParetoSourceSelectionReport(V9PostResolveSelectionReport SourceReport,
    V9BootstrapPareto2D.Result Pareto, string TrainingCorpusFingerprintSha256,
    string ValidationCohortFingerprintSha256, bool TestUsed = false);

/// <summary>
/// Adds the approved two-dimensional convention to the existing source-unit selector.
/// Reuses its reconciliation and digest; never substitutes pair counts or oracle grouping.
/// </summary>
public static class V9ParetoSourceSelection
{
    public static string ConfigurationId(string optionId, string grouping, decimal margin) =>
        $"{optionId.Length}:{optionId}|{grouping.Length}:{grouping}|{margin.ToString("G29", CultureInfo.InvariantCulture)}";

    public static V9ParetoSourceSelectionReport EvaluateValidation(string partition, bool usesTestLabels,
        AdaptiveCalibrationPreviewBudget budget, IReadOnlyList<V9PhysicalSourceResolveMetric> measurements,
        IReadOnlyDictionary<string, double>? blockingFalseDegreeP99ByOption,
        IReadOnlyList<V9ParetoPhysicalEvidence> physicalEvidence,
        V9BootstrapPareto2D.SelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(physicalEvidence);
        ArgumentNullException.ThrowIfNull(policy);
        // Existing service enforces VALIDATION, no TEST, same denominator/cohort,
        // one decision per source, coherent TP/FP and actual cluster bound semantics.
        var source = V9PostResolveAdaptiveSelector.EvaluateValidation(partition, usesTestLabels,
            budget, measurements, blockingFalseDegreeP99ByOption);
        if (policy.MinimumGlobalRecall < budget.MinimumGlobalAcceptedRecall
            || policy.MaximumAcceptedFdrUpper > budget.MaximumObservedAcceptedFdrPoint
            || policy.MinimumIndependentIdentityGroups < budget.MinimumValidationIdentityGroups)
            throw new InvalidDataException("Política Pareto não pode relaxar o orçamento de seleção por fonte.");
        var rowsByKey = measurements.ToDictionary(
            x => ConfigurationId(x.OptionId, x.CandidateGrouping, x.ConflictMargin), StringComparer.Ordinal);
        var evidence = new Dictionary<string, V9ParetoPhysicalEvidence>(StringComparer.Ordinal);
        foreach (var item in physicalEvidence)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.OptionId) || string.IsNullOrWhiteSpace(item.CandidateGrouping))
                throw new InvalidDataException("Evidência física inválida.");
            var key = ConfigurationId(item.OptionId, item.CandidateGrouping, item.ConflictMargin);
            if (!rowsByKey.ContainsKey(key) || !evidence.TryAdd(key, item))
                throw new InvalidDataException("Evidência física duplicada ou sem configuração medida.");
        }
        var validProvenance = physicalEvidence.Where(x => V9BootstrapPareto2D.Hash(x.TrainingCorpusFingerprintSha256)
            && V9BootstrapPareto2D.Hash(x.ValidationCohortFingerprintSha256)).ToArray();
        if (validProvenance.Select(x => x.TrainingCorpusFingerprintSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            || validProvenance.Select(x => x.ValidationCohortFingerprintSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new InvalidDataException("Configurações não compartilham TRAIN e coorte VALIDATION.");
        var train = validProvenance.Length == 0 ? "" : validProvenance[0].TrainingCorpusFingerprintSha256;
        var validation = validProvenance.Length == 0 ? "" : validProvenance[0].ValidationCohortFingerprintSha256;
        var conflictingHashes = physicalEvidence.Where(x => V9BootstrapPareto2D.Hash(x.ConfigurationFingerprintSha256))
            .GroupBy(x => x.ConfigurationFingerprintSha256, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x =>
            {
                var row = rowsByKey[ConfigurationId(x.OptionId, x.CandidateGrouping, x.ConflictMargin)];
                return (row.Threshold, row.TfWeight, row.ConflictMargin, row.CandidateGrouping);
            }).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = measurements.Select(row =>
        {
            var key = ConfigurationId(row.OptionId, row.CandidateGrouping, row.ConflictMargin);
            evidence.TryGetValue(key, out var physical);
            var reasons = new List<string>();
            if (row.SyntheticGoldOracleGrouping) reasons.Add("ORACLE_NAO_OPERACIONAL");
            if (row.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES") reasons.Add("AGRUPAMENTO_NAO_OPERACIONAL");
            if (row.ConflictMargin <= 0) reasons.Add("MARGEM_DE_CONFLITO_NULA");
            if (row.ImpureBlindGroups > 0) reasons.Add("AGRUPAMENTO_CEGO_IMPURO");
            if (row.PreBlockingBaseIdentities > row.AllValidationObservations)
                reasons.Add("SUPORTE_INDEPENDENTE_INCONSISTENTE");
            // The producer uses the existing CP numerator / Hoeffding denominator
            // ratio bound. The CP numerator must exist and cannot exceed that ratio:
            // the accepted mean lower bound is at most the predeclared identity cap.
            if (!row.ClopperPearsonUpperAnyFpByIdentity.HasValue
                || !double.IsFinite(row.ClopperPearsonUpperAnyFpByIdentity.Value)
                || row.ClopperPearsonUpperAnyFpByIdentity.Value is <= 0 or > 1
                || row.ConditionalClusterCpFdrUpper.HasValue
                    && row.ClopperPearsonUpperAnyFpByIdentity.Value > row.ConditionalClusterCpFdrUpper.Value + 1e-12)
                reasons.Add("COMPONENTE_CP_AUSENTE_OU_INCONSISTENTE");
            if (blockingFalseDegreeP99ByOption is null
                || !blockingFalseDegreeP99ByOption.TryGetValue(row.OptionId, out var fanout)
                || !double.IsFinite(fanout) || fanout < 0 || fanout > budget.MaximumBlockingFalseDegreeP99)
                reasons.Add("FANOUT_AUSENTE_OU_ACIMA_DA_POLITICA");
            if (physical is null) reasons.Add("EVIDENCIA_FISICA_AUSENTE");
            else
            {
                if (conflictingHashes.Contains(physical.ConfigurationFingerprintSha256))
                    reasons.Add("FINGERPRINT_REUTILIZADO_COM_PARAMETROS_DIVERGENTES");
                if (!V9BootstrapPareto2D.Hash(physical.TrainingCorpusFingerprintSha256)
                    || !V9BootstrapPareto2D.Hash(physical.ValidationCohortFingerprintSha256)
                    || !V9BootstrapPareto2D.Hash(physical.MeasurementProtocolFingerprintSha256)
                    || !V9BootstrapPareto2D.Hash(physical.MeasurementEnvironmentFingerprintSha256)
                    || !V9BootstrapPareto2D.Hash(physical.SourceEvidenceFingerprintSha256))
                    reasons.Add("PROVENIENCIA_FISICA_INCOMPLETA");
                if (!string.Equals(source.EvidenceFingerprintSha256, physical.SourceEvidenceFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase)) reasons.Add("EVIDENCIA_FISICA_NAO_VINCULADA_A_METRICAS");
                if (string.Equals(physical.TrainingCorpusFingerprintSha256, physical.ValidationCohortFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase)) reasons.Add("TRAIN_VALIDATION_NAO_INDEPENDENTES");
            }
            return new V9BootstrapPareto2D.CandidateOption(key, physical?.ConfigurationFingerprintSha256 ?? "",
                row.GlobalSourceRecall, row.FdrAfterResolve, physical?.PhysicalEndToEndP95Milliseconds,
                row.TrueAcceptedSourceObservations, row.FalseAcceptedSourceObservations,
                row.EligibleSourceObservationsBeforeBlocking, row.PreBlockingBaseIdentities,
                physical?.PhysicalEndToEndAttested ?? false, row.ClusterIidIndependentlyValidated,
                row.ConditionalClusterCpFdrUpper, row.BonferroniComparedConfigurations, reasons);
        }).ToArray();
        return new(source, V9BootstrapPareto2D.Evaluate(options, policy), train, validation);
    }
}

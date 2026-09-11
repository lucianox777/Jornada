using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Desenho amostral de uma observação da avaliação independente. O peso representa a
/// contribuição da observação para o universo-alvo e o conglomerado identifica a unidade
/// primária que deve permanecer inteira nas réplicas de incerteza.
/// </summary>
public sealed record IndependentResolutionSurveyObservation(
    IndependentResolutionObservation Observation,
    decimal DesignWeight,
    string IndependenceGroupFingerprintSha256);

public sealed record IndependentResolutionWeightedSliceMetrics(
    int Observations,
    int IndependentGroups,
    decimal ObservationWeight,
    decimal ReferenceWeight,
    decimal ResolvedWeight,
    decimal ConflictWeight,
    decimal UnresolvedWeight,
    decimal TrueLinkWeight,
    decimal FalseLinkWeight,
    decimal MissedLinkWeight,
    decimal TrueNonLinkWeight,
    decimal RecoveredReferenceWeight,
    decimal ScoredWeight,
    decimal? Recall,
    decimal? Precision,
    decimal? FalseLinkRate,
    decimal? FalsePositiveRate,
    decimal? ResolutionRate,
    decimal? CandidateRecoveryRate,
    decimal? TopCandidateBrierScore);

public sealed record IndependentResolutionJackknifeInterval(
    string Metric,
    decimal Estimate,
    decimal StandardError,
    decimal Lower95,
    decimal Upper95,
    int IndependentGroups,
    int Replicates);

public sealed record IndependentResolutionWeightedSubgroupMetrics(
    string Dimension,
    string Value,
    IndependentResolutionWeightedSliceMetrics Metrics,
    IReadOnlyList<IndependentResolutionJackknifeInterval> Uncertainty);

public sealed record IndependentResolutionWeightedCalibrationBin(
    int LowerInclusivePercent,
    int UpperInclusivePercent,
    int Observations,
    decimal DesignWeight,
    decimal MeanPredictedProbability,
    decimal ObservedMatchRate);

public sealed record IndependentResolutionSurveyEvaluationReport(
    string Version,
    string BaseEvaluationFingerprintSha256,
    string EvaluationManifestFingerprintSha256,
    string RuleSetVersion,
    string RuleSetFingerprintSha256,
    string SamplingDesignFingerprintSha256,
    decimal Threshold,
    decimal ConflictMargin,
    IndependentResolutionWeightedSliceMetrics Overall,
    IReadOnlyList<IndependentResolutionJackknifeInterval> OverallUncertainty,
    IReadOnlyList<IndependentResolutionWeightedSubgroupMetrics> Subgroups,
    IReadOnlyList<IndependentResolutionWeightedCalibrationBin> Calibration,
    string FingerprintSha256);

/// <summary>
/// Camada de inferência amostral do avaliador independente. Mantém o relatório descritivo
/// de <see cref="IndependentResolutionEvaluator"/> intacto e acrescenta estimativas ponderadas
/// e variância delete-one-cluster jackknife. Não define critérios de aprovação e não promove
/// qualquer modelo, ruleset ou threshold.
/// </summary>
public static class IndependentResolutionSurveyEvaluator
{
    public const string Version = "LINKAGE_INDEPENDENT_RESOLUTION_SURVEY_V1";
    public const string UncertaintyMethod = "DELETE_ONE_CLUSTER_JACKKNIFE_NORMAL95_V1";
    private const decimal Normal95 = 1.959963984540054m;

    public static IndependentResolutionSurveyEvaluationReport Evaluate(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<IndependentResolutionSurveyObservation> surveyObservations,
        decimal threshold,
        decimal conflictMargin)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(surveyObservations);
        if (surveyObservations.Count == 0)
            throw new ArgumentException("At least one survey observation is required.", nameof(surveyObservations));

        var rawObservations = surveyObservations
            .Select(static row => row?.Observation
                ?? throw new InvalidOperationException("Survey observation cannot be null."))
            .ToArray();

        // Reuse the exact descriptive evaluator as the first fail-closed gate for manifest
        // denominators, candidate identity, scores, threshold and conflict margin.
        var baseReport = IndependentResolutionEvaluator.Evaluate(
            manifest,
            rawObservations,
            threshold,
            conflictMargin);

        var evaluated = NormalizeAndEvaluate(surveyObservations, threshold, conflictMargin);
        var groups = evaluated.Select(static row => row.GroupFingerprintSha256)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (groups.Length < 3)
            throw new InvalidOperationException(
                "Clustered uncertainty requires at least three independent groups.");

        var overall = Metrics(evaluated);
        var overallUncertainty = Jackknife(evaluated, overall);
        var subgroups = BuildSubgroups(evaluated);
        var calibration = BuildCalibration(evaluated);
        var designFingerprint = SamplingDesignFingerprint(evaluated);
        var fingerprint = Fingerprint(
            baseReport,
            designFingerprint,
            overall,
            overallUncertainty,
            subgroups,
            calibration);

        return new IndependentResolutionSurveyEvaluationReport(
            Version,
            baseReport.FingerprintSha256,
            manifest.Evaluation.FingerprintSha256,
            manifest.RuleSetVersion,
            manifest.RuleSetFingerprintSha256,
            designFingerprint,
            threshold,
            conflictMargin,
            overall,
            Array.AsReadOnly(overallUncertainty),
            Array.AsReadOnly(subgroups),
            Array.AsReadOnly(calibration),
            fingerprint);
    }

    private static SurveyEvaluatedObservation[] NormalizeAndEvaluate(
        IReadOnlyList<IndependentResolutionSurveyObservation> rows,
        decimal threshold,
        decimal conflictMargin)
    {
        var result = new List<SurveyEvaluatedObservation>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null || row.Observation is null)
                throw new InvalidOperationException("Survey observation cannot be null.");
            if (row.DesignWeight <= 0m)
                throw new InvalidOperationException("DesignWeight must be positive.");

            var group = Sha256(row.IndependenceGroupFingerprintSha256, nameof(row.IndependenceGroupFingerprintSha256));
            var observationFingerprint = Sha256(
                row.Observation.ObservationFingerprintSha256,
                nameof(row.Observation.ObservationFingerprintSha256));
            var reference = row.Observation.ReferenceCandidateFingerprintSha256 is null
                ? null
                : Sha256(row.Observation.ReferenceCandidateFingerprintSha256, nameof(row.Observation.ReferenceCandidateFingerprintSha256));

            var candidates = row.Observation.Candidates
                .Select(candidate => new SurveyCandidate(
                    Sha256(candidate.CandidateFingerprintSha256, nameof(candidate.CandidateFingerprintSha256)),
                    candidate.Score))
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.FingerprintSha256, StringComparer.Ordinal)
                .ToArray();

            var best = candidates.FirstOrDefault();
            var second = candidates.Length > 1 ? candidates[1] : null;
            string? resolved = null;
            var state = SurveyResolutionState.Unresolved;
            if (best is not null && best.Score >= threshold)
            {
                if (second is not null && best.Score - second.Score < conflictMargin)
                    state = SurveyResolutionState.Conflict;
                else
                {
                    state = SurveyResolutionState.Resolved;
                    resolved = best.FingerprintSha256;
                }
            }

            var trueLink = reference is not null && string.Equals(resolved, reference, StringComparison.Ordinal);
            var falseLink = resolved is not null && !string.Equals(resolved, reference, StringComparison.Ordinal);
            var missedLink = reference is not null && !string.Equals(resolved, reference, StringComparison.Ordinal);
            var trueNonLink = reference is null && resolved is null;
            var recovered = reference is not null && candidates.Any(candidate =>
                string.Equals(candidate.FingerprintSha256, reference, StringComparison.Ordinal));

            decimal? brier = null;
            var topMatchesReference = false;
            if (best is not null)
            {
                topMatchesReference = reference is not null &&
                    string.Equals(best.FingerprintSha256, reference, StringComparison.Ordinal);
                var outcome = topMatchesReference ? 1m : 0m;
                var delta = best.Score - outcome;
                brier = delta * delta;
            }

            var subgroups = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (row.Observation.Subgroups is not null)
            {
                foreach (var pair in row.Observation.Subgroups)
                    subgroups.Add(pair.Key.Trim(), pair.Value.Trim());
            }

            result.Add(new SurveyEvaluatedObservation(
                observationFingerprint,
                group,
                row.DesignWeight,
                reference,
                state,
                trueLink,
                falseLink,
                missedLink,
                trueNonLink,
                recovered,
                best?.Score,
                topMatchesReference,
                brier,
                subgroups));
        }

        return result
            .OrderBy(static row => row.ObservationFingerprintSha256, StringComparer.Ordinal)
            .ToArray();
    }

    private static IndependentResolutionWeightedSliceMetrics Metrics(
        IReadOnlyList<SurveyEvaluatedObservation> rows)
    {
        var total = rows.Sum(static row => row.Weight);
        var references = Sum(rows, static row => row.ReferenceCandidateFingerprintSha256 is not null);
        var resolved = Sum(rows, static row => row.State == SurveyResolutionState.Resolved);
        var conflicts = Sum(rows, static row => row.State == SurveyResolutionState.Conflict);
        var unresolved = Sum(rows, static row => row.State == SurveyResolutionState.Unresolved);
        var trueLinks = Sum(rows, static row => row.TrueLink);
        var falseLinks = Sum(rows, static row => row.FalseLink);
        var missedLinks = Sum(rows, static row => row.MissedLink);
        var trueNonLinks = Sum(rows, static row => row.TrueNonLink);
        var recovered = Sum(rows, static row => row.RecoveredReferenceCandidate);
        var scored = Sum(rows, static row => row.TopScore is not null);
        var brierDenominator = rows.Where(static row => row.Brier is not null).Sum(static row => row.Weight);
        var brierNumerator = rows.Where(static row => row.Brier is not null)
            .Sum(static row => row.Weight * row.Brier!.Value);

        return new IndependentResolutionWeightedSliceMetrics(
            rows.Count,
            rows.Select(static row => row.GroupFingerprintSha256).Distinct(StringComparer.Ordinal).Count(),
            total,
            references,
            resolved,
            conflicts,
            unresolved,
            trueLinks,
            falseLinks,
            missedLinks,
            trueNonLinks,
            recovered,
            scored,
            Ratio(trueLinks, trueLinks + missedLinks),
            Ratio(trueLinks, trueLinks + falseLinks),
            Ratio(falseLinks, resolved),
            Ratio(falseLinks, falseLinks + trueNonLinks),
            Ratio(resolved, total),
            Ratio(recovered, references),
            brierDenominator == 0m ? null : brierNumerator / brierDenominator);
    }

    private static IndependentResolutionJackknifeInterval[] Jackknife(
        IReadOnlyList<SurveyEvaluatedObservation> rows,
        IndependentResolutionWeightedSliceMetrics full)
    {
        var groups = rows.Select(static row => row.GroupFingerprintSha256)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (groups.Length < 3)
            return Array.Empty<IndependentResolutionJackknifeInterval>();

        var replicates = groups
            .Select(group => Metrics(rows.Where(row => !string.Equals(
                row.GroupFingerprintSha256,
                group,
                StringComparison.Ordinal)).ToArray()))
            .ToArray();

        var intervals = new List<IndependentResolutionJackknifeInterval>();
        AddInterval(intervals, "RECALL", full.Recall, replicates.Select(static metric => metric.Recall).ToArray(), groups.Length);
        AddInterval(intervals, "PRECISION", full.Precision, replicates.Select(static metric => metric.Precision).ToArray(), groups.Length);
        AddInterval(intervals, "FALSE_LINK_RATE", full.FalseLinkRate, replicates.Select(static metric => metric.FalseLinkRate).ToArray(), groups.Length);
        AddInterval(intervals, "FALSE_POSITIVE_RATE", full.FalsePositiveRate, replicates.Select(static metric => metric.FalsePositiveRate).ToArray(), groups.Length);
        AddInterval(intervals, "RESOLUTION_RATE", full.ResolutionRate, replicates.Select(static metric => metric.ResolutionRate).ToArray(), groups.Length);
        AddInterval(intervals, "CANDIDATE_RECOVERY_RATE", full.CandidateRecoveryRate, replicates.Select(static metric => metric.CandidateRecoveryRate).ToArray(), groups.Length);
        AddInterval(intervals, "TOP_CANDIDATE_BRIER", full.TopCandidateBrierScore, replicates.Select(static metric => metric.TopCandidateBrierScore).ToArray(), groups.Length);
        return intervals.ToArray();
    }

    private static void AddInterval(
        ICollection<IndependentResolutionJackknifeInterval> output,
        string metric,
        decimal? estimate,
        IReadOnlyList<decimal?> replicateValues,
        int groups)
    {
        if (estimate is null || replicateValues.Any(static value => value is null))
            return;

        var values = replicateValues.Select(static value => value!.Value).ToArray();
        var mean = values.Average();
        var sumSquares = values.Sum(value =>
        {
            var delta = value - mean;
            return delta * delta;
        });
        var variance = (groups - 1m) / groups * sumSquares;
        var standardError = Sqrt(variance);
        var lower = Clamp01(estimate.Value - Normal95 * standardError);
        var upper = Clamp01(estimate.Value + Normal95 * standardError);
        output.Add(new IndependentResolutionJackknifeInterval(
            metric,
            estimate.Value,
            standardError,
            lower,
            upper,
            groups,
            values.Length));
    }

    private static IndependentResolutionWeightedSubgroupMetrics[] BuildSubgroups(
        IReadOnlyList<SurveyEvaluatedObservation> rows) =>
        rows.SelectMany(static row => row.Subgroups.Select(pair => (pair.Key, pair.Value, Row: row)))
            .GroupBy(static item => (item.Key, item.Value))
            .OrderBy(static group => group.Key.Key, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Value, StringComparer.Ordinal)
            .Select(group =>
            {
                var selected = group.Select(static item => item.Row).ToArray();
                var metrics = Metrics(selected);
                return new IndependentResolutionWeightedSubgroupMetrics(
                    group.Key.Key,
                    group.Key.Value,
                    metrics,
                    Array.AsReadOnly(Jackknife(selected, metrics)));
            })
            .ToArray();

    private static IndependentResolutionWeightedCalibrationBin[] BuildCalibration(
        IReadOnlyList<SurveyEvaluatedObservation> rows)
    {
        var scored = rows.Where(static row => row.TopScore is not null).ToArray();
        var bins = new List<IndependentResolutionWeightedCalibrationBin>();
        for (var index = 0; index < 10; index++)
        {
            var lower = index / 10m;
            var upper = (index + 1) / 10m;
            var selected = scored.Where(row => row.TopScore!.Value >= lower &&
                (index == 9 ? row.TopScore.Value <= upper : row.TopScore.Value < upper)).ToArray();
            if (selected.Length == 0)
                continue;
            var weight = selected.Sum(static row => row.Weight);
            bins.Add(new IndependentResolutionWeightedCalibrationBin(
                index * 10,
                (index + 1) * 10,
                selected.Length,
                weight,
                selected.Sum(static row => row.Weight * row.TopScore!.Value) / weight,
                selected.Sum(static row => row.Weight * (row.TopCandidateMatchesReference ? 1m : 0m)) / weight));
        }
        return bins.ToArray();
    }

    private static string SamplingDesignFingerprint(IReadOnlyList<SurveyEvaluatedObservation> rows)
    {
        var canonical = new StringBuilder().Append("LINKAGE_INDEPENDENT_RESOLUTION_SAMPLING_DESIGN_V1\n");
        foreach (var row in rows.OrderBy(static row => row.ObservationFingerprintSha256, StringComparer.Ordinal))
        {
            canonical.Append(row.ObservationFingerprintSha256).Append('|')
                .Append(Decimal(row.Weight)).Append('|')
                .Append(row.GroupFingerprintSha256).Append('\n');
        }
        return Hash(canonical.ToString());
    }

    private static string Fingerprint(
        IndependentResolutionEvaluationReport baseReport,
        string designFingerprint,
        IndependentResolutionWeightedSliceMetrics overall,
        IReadOnlyList<IndependentResolutionJackknifeInterval> overallUncertainty,
        IReadOnlyList<IndependentResolutionWeightedSubgroupMetrics> subgroups,
        IReadOnlyList<IndependentResolutionWeightedCalibrationBin> calibration)
    {
        var canonical = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(UncertaintyMethod).Append('\n')
            .Append(baseReport.FingerprintSha256).Append('\n')
            .Append(designFingerprint).Append('\n');
        AppendMetrics(canonical, "OVERALL", overall);
        AppendIntervals(canonical, "OVERALL", overallUncertainty);
        foreach (var subgroup in subgroups)
        {
            var prefix = $"SUBGROUP:{subgroup.Dimension}={subgroup.Value}";
            AppendMetrics(canonical, prefix, subgroup.Metrics);
            AppendIntervals(canonical, prefix, subgroup.Uncertainty);
        }
        foreach (var bin in calibration)
        {
            canonical.Append("CAL:")
                .Append(bin.LowerInclusivePercent).Append(':')
                .Append(bin.UpperInclusivePercent).Append(':')
                .Append(bin.Observations).Append(':')
                .Append(Decimal(bin.DesignWeight)).Append(':')
                .Append(Decimal(bin.MeanPredictedProbability)).Append(':')
                .Append(Decimal(bin.ObservedMatchRate)).Append('\n');
        }
        return Hash(canonical.ToString());
    }

    private static void AppendMetrics(
        StringBuilder builder,
        string prefix,
        IndependentResolutionWeightedSliceMetrics metrics)
    {
        builder.Append(prefix).Append(':')
            .Append(metrics.Observations).Append(':')
            .Append(metrics.IndependentGroups).Append(':')
            .Append(Decimal(metrics.ObservationWeight)).Append(':')
            .Append(Decimal(metrics.ReferenceWeight)).Append(':')
            .Append(Decimal(metrics.ResolvedWeight)).Append(':')
            .Append(Decimal(metrics.ConflictWeight)).Append(':')
            .Append(Decimal(metrics.UnresolvedWeight)).Append(':')
            .Append(Decimal(metrics.TrueLinkWeight)).Append(':')
            .Append(Decimal(metrics.FalseLinkWeight)).Append(':')
            .Append(Decimal(metrics.MissedLinkWeight)).Append(':')
            .Append(Decimal(metrics.TrueNonLinkWeight)).Append(':')
            .Append(Decimal(metrics.RecoveredReferenceWeight)).Append(':')
            .Append(Decimal(metrics.ScoredWeight)).Append(':')
            .Append(Decimal(metrics.Recall)).Append(':')
            .Append(Decimal(metrics.Precision)).Append(':')
            .Append(Decimal(metrics.FalseLinkRate)).Append(':')
            .Append(Decimal(metrics.FalsePositiveRate)).Append(':')
            .Append(Decimal(metrics.ResolutionRate)).Append(':')
            .Append(Decimal(metrics.CandidateRecoveryRate)).Append(':')
            .Append(Decimal(metrics.TopCandidateBrierScore)).Append('\n');
    }

    private static void AppendIntervals(
        StringBuilder builder,
        string prefix,
        IReadOnlyList<IndependentResolutionJackknifeInterval> intervals)
    {
        foreach (var interval in intervals.OrderBy(static interval => interval.Metric, StringComparer.Ordinal))
        {
            builder.Append(prefix).Append(":JK:")
                .Append(interval.Metric).Append(':')
                .Append(Decimal(interval.Estimate)).Append(':')
                .Append(Decimal(interval.StandardError)).Append(':')
                .Append(Decimal(interval.Lower95)).Append(':')
                .Append(Decimal(interval.Upper95)).Append(':')
                .Append(interval.IndependentGroups).Append(':')
                .Append(interval.Replicates).Append('\n');
        }
    }

    private static decimal Sum(
        IEnumerable<SurveyEvaluatedObservation> rows,
        Func<SurveyEvaluatedObservation, bool> predicate) =>
        rows.Where(predicate).Sum(static row => row.Weight);

    private static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0m ? null : numerator / denominator;

    private static decimal Clamp01(decimal value) => value < 0m ? 0m : value > 1m ? 1m : value;

    private static decimal Sqrt(decimal value)
    {
        if (value <= 0m)
            return 0m;
        var current = value >= 1m ? value : 1m;
        for (var i = 0; i < 64; i++)
        {
            var next = (current + value / current) / 2m;
            if (Math.Abs(next - current) <= 0.000000000000000000000000001m)
                return next;
            current = next;
        }
        return current;
    }

    private static string Decimal(decimal? value) =>
        value is null ? "-" : Decimal(value.Value);

    private static string Decimal(decimal value) =>
        value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

    private static string Sha256(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        value = value.Trim().ToLowerInvariant();
        if (value.Length != 64 || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException($"{name} must be a 64-character SHA-256 hex fingerprint.");
        return value;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record SurveyCandidate(string FingerprintSha256, decimal Score);

    private sealed record SurveyEvaluatedObservation(
        string ObservationFingerprintSha256,
        string GroupFingerprintSha256,
        decimal Weight,
        string? ReferenceCandidateFingerprintSha256,
        SurveyResolutionState State,
        bool TrueLink,
        bool FalseLink,
        bool MissedLink,
        bool TrueNonLink,
        bool RecoveredReferenceCandidate,
        decimal? TopScore,
        bool TopCandidateMatchesReference,
        decimal? Brier,
        IReadOnlyDictionary<string, string> Subgroups);

    private enum SurveyResolutionState
    {
        Unresolved,
        Conflict,
        Resolved
    }
}

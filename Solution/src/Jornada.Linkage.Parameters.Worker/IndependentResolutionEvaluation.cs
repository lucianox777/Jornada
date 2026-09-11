using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IndependentResolutionCandidate(
    string CandidateFingerprintSha256,
    decimal Score);

public sealed record IndependentResolutionObservation(
    string ObservationFingerprintSha256,
    string? ReferenceCandidateFingerprintSha256,
    IReadOnlyList<IndependentResolutionCandidate> Candidates,
    IReadOnlyDictionary<string, string>? Subgroups = null);

public sealed record IndependentResolutionSliceMetrics(
    int Observations,
    int ReferenceLinks,
    int Resolved,
    int Conflicts,
    int Unresolved,
    int TrueLinks,
    int FalseLinks,
    int MissedLinks,
    int TrueNonLinks,
    int RecoveredReferenceCandidates,
    int ScoredObservations,
    decimal? Recall,
    decimal? Precision,
    decimal? FalseLinkRate,
    decimal? FalsePositiveRate,
    decimal? ResolutionRate,
    decimal? CandidateRecoveryRate,
    decimal? TopCandidateBrierScore);

public sealed record IndependentResolutionSubgroupMetrics(
    string Dimension,
    string Value,
    IndependentResolutionSliceMetrics Metrics);

public sealed record IndependentResolutionCalibrationBin(
    int LowerInclusivePercent,
    int UpperInclusivePercent,
    int Observations,
    decimal MeanPredictedProbability,
    decimal ObservedMatchRate);

public sealed record IndependentResolutionEvaluationReport(
    string Version,
    string EvaluationManifestFingerprintSha256,
    string RuleSetVersion,
    string RuleSetFingerprintSha256,
    decimal Threshold,
    decimal ConflictMargin,
    IndependentResolutionSliceMetrics Overall,
    IReadOnlyList<IndependentResolutionSubgroupMetrics> Subgroups,
    IReadOnlyList<IndependentResolutionCalibrationBin> Calibration,
    string FingerprintSha256);

/// <summary>
/// Avaliador determinístico e somente leitura do resolver probabilístico.
/// Consome scores já produzidos contra um corpus independente e nunca promove modelo,
/// ruleset, threshold ou decisão operacional.
/// </summary>
public static class IndependentResolutionEvaluator
{
    public const string Version = "LINKAGE_INDEPENDENT_RESOLUTION_EVALUATION_V1";

    public static IndependentResolutionEvaluationReport Evaluate(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<IndependentResolutionObservation> observations,
        decimal threshold,
        decimal conflictMargin)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(observations);

        if (threshold is <= 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in (0, 1].");
        if (conflictMargin is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(conflictMargin), "ConflictMargin must be in [0, 1].");
        if (observations.Count == 0)
            throw new ArgumentException("At least one evaluation observation is required.", nameof(observations));

        var normalized = NormalizeAndValidate(manifest, observations);
        var evaluated = normalized
            .Select(x => EvaluateObservation(x, threshold, conflictMargin))
            .ToArray();

        var overall = Metrics(evaluated);
        var subgroups = BuildSubgroups(evaluated);
        var calibration = BuildCalibration(evaluated);
        var fingerprint = Fingerprint(manifest, threshold, conflictMargin, overall, subgroups, calibration);

        return new IndependentResolutionEvaluationReport(
            Version,
            manifest.Evaluation.FingerprintSha256,
            manifest.RuleSetVersion,
            manifest.RuleSetFingerprintSha256,
            threshold,
            conflictMargin,
            overall,
            Array.AsReadOnly(subgroups),
            Array.AsReadOnly(calibration),
            fingerprint);
    }

    private static NormalizedObservation[] NormalizeAndValidate(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<IndependentResolutionObservation> observations)
    {
        var seenObservations = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NormalizedObservation>(observations.Count);
        long candidatePairs = 0;
        long referenceLinks = 0;

        foreach (var observation in observations)
        {
            if (observation is null)
                throw new InvalidOperationException("Evaluation observation cannot be null.");

            var observationFingerprint = Sha256(
                observation.ObservationFingerprintSha256,
                nameof(observation.ObservationFingerprintSha256));
            if (!seenObservations.Add(observationFingerprint))
                throw new InvalidOperationException("Observation fingerprint must be unique inside one evaluation.");

            var reference = observation.ReferenceCandidateFingerprintSha256 is null
                ? null
                : Sha256(observation.ReferenceCandidateFingerprintSha256, nameof(observation.ReferenceCandidateFingerprintSha256));
            if (reference is not null)
                referenceLinks++;

            if (observation.Candidates is null)
                throw new InvalidOperationException("Candidate list cannot be null.");

            var seenCandidates = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<NormalizedCandidate>(observation.Candidates.Count);
            foreach (var candidate in observation.Candidates)
            {
                if (candidate is null)
                    throw new InvalidOperationException("Evaluation candidate cannot be null.");
                var candidateFingerprint = Sha256(candidate.CandidateFingerprintSha256, nameof(candidate.CandidateFingerprintSha256));
                if (!seenCandidates.Add(candidateFingerprint))
                    throw new InvalidOperationException("Candidate fingerprint must be unique inside one observation.");
                if (candidate.Score is < 0m or > 1m)
                    throw new InvalidOperationException("Candidate score must be in [0, 1].");
                candidates.Add(new NormalizedCandidate(candidateFingerprint, candidate.Score));
                candidatePairs++;
            }

            var subgroups = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (observation.Subgroups is not null)
            {
                foreach (var pair in observation.Subgroups)
                {
                    var dimension = RequiredCode(pair.Key, "Subgroup dimension");
                    var value = RequiredCode(pair.Value, "Subgroup value");
                    if (!subgroups.TryAdd(dimension, value))
                        throw new InvalidOperationException("Subgroup dimension must be unique per observation.");
                }
            }

            result.Add(new NormalizedObservation(
                observationFingerprint,
                reference,
                candidates
                    .OrderByDescending(static c => c.Score)
                    .ThenBy(static c => c.FingerprintSha256, StringComparer.Ordinal)
                    .ToArray(),
                subgroups));
        }

        if (candidatePairs != manifest.Evaluation.CandidatePairs)
            throw new InvalidOperationException(
                $"Evaluation candidate-pair denominator mismatch: manifest={manifest.Evaluation.CandidatePairs}, actual={candidatePairs}.");
        if (referenceLinks != manifest.Evaluation.ReferenceLinks)
            throw new InvalidOperationException(
                $"Evaluation reference-link denominator mismatch: manifest={manifest.Evaluation.ReferenceLinks}, actual={referenceLinks}.");

        return result
            .OrderBy(static x => x.ObservationFingerprintSha256, StringComparer.Ordinal)
            .ToArray();
    }

    private static EvaluatedObservation EvaluateObservation(
        NormalizedObservation observation,
        decimal threshold,
        decimal conflictMargin)
    {
        var best = observation.Candidates.FirstOrDefault();
        var second = observation.Candidates.Length > 1 ? observation.Candidates[1] : null;
        string? resolved = null;
        var state = ResolutionState.Unresolved;

        if (best is not null && best.Score >= threshold)
        {
            if (second is not null && best.Score - second.Score < conflictMargin)
            {
                state = ResolutionState.Conflict;
            }
            else
            {
                state = ResolutionState.Resolved;
                resolved = best.FingerprintSha256;
            }
        }

        var truth = observation.ReferenceCandidateFingerprintSha256;
        var trueLink = truth is not null && string.Equals(resolved, truth, StringComparison.Ordinal);
        var falseLink = resolved is not null && !string.Equals(resolved, truth, StringComparison.Ordinal);
        var missedLink = truth is not null && !string.Equals(resolved, truth, StringComparison.Ordinal);
        var trueNonLink = truth is null && resolved is null;
        var recovered = truth is not null && observation.Candidates.Any(c =>
            string.Equals(c.FingerprintSha256, truth, StringComparison.Ordinal));

        decimal? brier = null;
        if (best is not null)
        {
            var outcome = truth is not null && string.Equals(best.FingerprintSha256, truth, StringComparison.Ordinal) ? 1m : 0m;
            var delta = best.Score - outcome;
            brier = delta * delta;
        }

        return new EvaluatedObservation(
            observation,
            state,
            resolved,
            trueLink,
            falseLink,
            missedLink,
            trueNonLink,
            recovered,
            best?.Score,
            brier);
    }

    private static IndependentResolutionSliceMetrics Metrics(IReadOnlyList<EvaluatedObservation> rows)
    {
        var observations = rows.Count;
        var references = rows.Count(static x => x.Observation.ReferenceCandidateFingerprintSha256 is not null);
        var resolved = rows.Count(static x => x.State == ResolutionState.Resolved);
        var conflicts = rows.Count(static x => x.State == ResolutionState.Conflict);
        var unresolved = rows.Count(static x => x.State == ResolutionState.Unresolved);
        var trueLinks = rows.Count(static x => x.TrueLink);
        var falseLinks = rows.Count(static x => x.FalseLink);
        var missedLinks = rows.Count(static x => x.MissedLink);
        var trueNonLinks = rows.Count(static x => x.TrueNonLink);
        var recovered = rows.Count(static x => x.RecoveredReferenceCandidate);
        var scored = rows.Count(static x => x.TopScore is not null);
        var brierRows = rows.Where(static x => x.Brier is not null).ToArray();

        return new IndependentResolutionSliceMetrics(
            observations,
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
            Ratio(resolved, observations),
            Ratio(recovered, references),
            brierRows.Length == 0 ? null : brierRows.Average(static x => x.Brier!.Value));
    }

    private static IndependentResolutionSubgroupMetrics[] BuildSubgroups(
        IReadOnlyList<EvaluatedObservation> rows) =>
        rows.SelectMany(static row => row.Observation.Subgroups.Select(pair => (pair.Key, pair.Value, Row: row)))
            .GroupBy(static x => (x.Key, x.Value))
            .OrderBy(static group => group.Key.Key, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Value, StringComparer.Ordinal)
            .Select(static group => new IndependentResolutionSubgroupMetrics(
                group.Key.Key,
                group.Key.Value,
                Metrics(group.Select(static x => x.Row).ToArray())))
            .ToArray();

    private static IndependentResolutionCalibrationBin[] BuildCalibration(
        IReadOnlyList<EvaluatedObservation> rows)
    {
        var scored = rows.Where(static x => x.TopScore is not null).ToArray();
        var bins = new List<IndependentResolutionCalibrationBin>();
        for (var index = 0; index < 10; index++)
        {
            var lower = index / 10m;
            var upper = (index + 1) / 10m;
            var selected = scored.Where(x => x.TopScore!.Value >= lower &&
                (index == 9 ? x.TopScore.Value <= upper : x.TopScore.Value < upper)).ToArray();
            if (selected.Length == 0)
                continue;
            bins.Add(new IndependentResolutionCalibrationBin(
                index * 10,
                (index + 1) * 10,
                selected.Length,
                selected.Average(static x => x.TopScore!.Value),
                selected.Average(static x => x.TopCandidateMatchesReference ? 1m : 0m)));
        }
        return bins.ToArray();
    }

    private static string Fingerprint(
        IndependentRuleSetEvaluationManifest manifest,
        decimal threshold,
        decimal conflictMargin,
        IndependentResolutionSliceMetrics overall,
        IReadOnlyList<IndependentResolutionSubgroupMetrics> subgroups,
        IReadOnlyList<IndependentResolutionCalibrationBin> calibration)
    {
        var builder = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(manifest.Evaluation.FingerprintSha256).Append('\n')
            .Append(manifest.RuleSetVersion).Append('\n')
            .Append(manifest.RuleSetFingerprintSha256).Append('\n')
            .Append(Decimal(threshold)).Append('\n')
            .Append(Decimal(conflictMargin)).Append('\n');

        AppendMetrics(builder, "OVERALL", overall);
        foreach (var subgroup in subgroups)
            AppendMetrics(builder, $"SUBGROUP:{subgroup.Dimension}={subgroup.Value}", subgroup.Metrics);
        foreach (var bin in calibration)
        {
            builder.Append("CAL:")
                .Append(bin.LowerInclusivePercent).Append(':')
                .Append(bin.UpperInclusivePercent).Append(':')
                .Append(bin.Observations).Append(':')
                .Append(Decimal(bin.MeanPredictedProbability)).Append(':')
                .Append(Decimal(bin.ObservedMatchRate)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendMetrics(StringBuilder builder, string prefix, IndependentResolutionSliceMetrics metrics)
    {
        builder.Append(prefix).Append(':')
            .Append(metrics.Observations).Append(':')
            .Append(metrics.ReferenceLinks).Append(':')
            .Append(metrics.Resolved).Append(':')
            .Append(metrics.Conflicts).Append(':')
            .Append(metrics.Unresolved).Append(':')
            .Append(metrics.TrueLinks).Append(':')
            .Append(metrics.FalseLinks).Append(':')
            .Append(metrics.MissedLinks).Append(':')
            .Append(metrics.TrueNonLinks).Append(':')
            .Append(metrics.RecoveredReferenceCandidates).Append(':')
            .Append(metrics.ScoredObservations).Append(':')
            .Append(Decimal(metrics.Recall)).Append(':')
            .Append(Decimal(metrics.Precision)).Append(':')
            .Append(Decimal(metrics.FalseLinkRate)).Append(':')
            .Append(Decimal(metrics.FalsePositiveRate)).Append(':')
            .Append(Decimal(metrics.ResolutionRate)).Append(':')
            .Append(Decimal(metrics.CandidateRecoveryRate)).Append(':')
            .Append(Decimal(metrics.TopCandidateBrierScore)).Append('\n');
    }

    private static decimal? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : numerator / (decimal)denominator;

    private static string Decimal(decimal? value) =>
        value is null ? "-" : value.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

    private static string RequiredCode(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        var normalized = value.Trim();
        if (normalized.Length > 100 || normalized.Any(static c => char.IsControl(c) || c is '\n' or '\r' or ':' or '='))
            throw new InvalidOperationException($"{name} contains unsupported characters or is too long.");
        return normalized;
    }

    private static string Sha256(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        value = value.Trim().ToLowerInvariant();
        if (value.Length != 64 || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException($"{name} must be a 64-character SHA-256 hex fingerprint.");
        return value;
    }

    private sealed record NormalizedCandidate(string FingerprintSha256, decimal Score);

    private sealed record NormalizedObservation(
        string ObservationFingerprintSha256,
        string? ReferenceCandidateFingerprintSha256,
        NormalizedCandidate[] Candidates,
        IReadOnlyDictionary<string, string> Subgroups);

    private sealed record EvaluatedObservation(
        NormalizedObservation Observation,
        ResolutionState State,
        string? ResolvedCandidateFingerprintSha256,
        bool TrueLink,
        bool FalseLink,
        bool MissedLink,
        bool TrueNonLink,
        bool RecoveredReferenceCandidate,
        decimal? TopScore,
        decimal? Brier)
    {
        internal bool TopCandidateMatchesReference =>
            Observation.Candidates.Length > 0 &&
            Observation.ReferenceCandidateFingerprintSha256 is not null &&
            string.Equals(
                Observation.Candidates[0].FingerprintSha256,
                Observation.ReferenceCandidateFingerprintSha256,
                StringComparison.Ordinal);
    }

    private enum ResolutionState
    {
        Unresolved,
        Conflict,
        Resolved
    }
}

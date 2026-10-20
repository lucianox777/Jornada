using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// TRAIN_SELECTION diagnostic. "No-match" is an observation-level hypothesis
/// over all retrieved Gold candidates, NOT a proof of global novelty.
/// Keeps existing FS acceptance/FDR decisions unchanged. Does not publish.
/// </summary>
public static class V9LowerNonMatchTrainCalibrator
{
    public const string Version = "V9_TRAIN_SELECTION_LOWER_NONMATCH_PARETO_V1";

    public sealed record Policy(
        string FrozenSnapshotSha256,
        string TrainingEvidenceSha256,
        decimal FixedUpperThreshold,
        IReadOnlyList<decimal> CandidateLowerThresholds,
        double MaximumFalseNewAmongCreates,
        double MaximumFalseNewAmongAlreadyRepresented,
        int MinimumIndependentIdentityGroups,
        int MinimumAlreadyRepresentedSources,
        bool ManualCalibrationRequested,
        bool TestUsed);

    public sealed record TrainingSource(
        string SourceFingerprintSha256,
        string IndependentBaseIdentityFingerprintSha256,
        string Partition,
        bool GoldAlreadyContainsTrueIdentity,
        bool IndependentlyLabeled,
        ProbabilisticLinkageDecision OriginalDecision,
        V9LowerNonMatchPreview.VerifiedRetrieval Retrieval);

    public sealed record Candidate(
        decimal LowerThreshold,
        long ProposedNewReferences,
        long CorrectNewReferences,
        long FalseNewReferences,
        long BaselinePending,
        long PendingAfter,
        double? FalseNewAmongCreates,
        double FalseNewAmongAlreadyRepresented,
        bool MeetsPointBudgets,
        bool IsParetoFrontier);

    public sealed record Study(
        string Version,
        string FrozenSnapshotSha256,
        string TrainingEvidenceSha256,
        string PolicyFingerprintSha256,
        int IndependentIdentityGroups,
        int AlreadyRepresentedSources,
        long OriginalAcceptedLinks,
        IReadOnlyList<Candidate> Candidates,
        decimal? DescriptiveSuggestedLowerThreshold,
        string Status,
        bool ExternalValidationUsed,
        bool TestUsed,
        bool OperationalModelCreated,
        bool AutomaticCalibrationOrActivationAllowed,
        bool DuplicateConfidenceCertified,
        bool LinkageFdrRecalibrated);

    public static Study Search(Policy policy, IReadOnlyList<TrainingSource> sources)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sources);
        CheckSha(policy.FrozenSnapshotSha256, "snapshot");
        CheckSha(policy.TrainingEvidenceSha256, "TRAIN evidence");
        if (!policy.ManualCalibrationRequested || policy.TestUsed)
            throw new InvalidOperationException("Calibração inferior requer execução manual e TEST lacrado.");
        if (policy.FixedUpperThreshold is <= 0m or >= 1m
            || policy.CandidateLowerThresholds is null
            || policy.CandidateLowerThresholds.Count is < 1 or > 256
            || policy.CandidateLowerThresholds.Any(x => x is <= 0m or >= 1m || x >= policy.FixedUpperThreshold)
            || policy.CandidateLowerThresholds.Distinct().Count() != policy.CandidateLowerThresholds.Count
            || !double.IsFinite(policy.MaximumFalseNewAmongCreates)
            || policy.MaximumFalseNewAmongCreates is < 0 or > .1
            || !double.IsFinite(policy.MaximumFalseNewAmongAlreadyRepresented)
            || policy.MaximumFalseNewAmongAlreadyRepresented is < 0 or > .1
            || policy.MinimumIndependentIdentityGroups < 2
            || policy.MinimumAlreadyRepresentedSources < 1)
            throw new InvalidDataException("Parâmetros e orçamento de duplicação inválidos.");
        if (sources.Count == 0 || sources.Count > 1_000_000)
            throw new InvalidDataException("TRAIN_SELECTION com pelo menos uma observação e cap explícito.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null || source.OriginalDecision is null || source.Retrieval is null
                || source.Partition != "TRAIN_SELECTION" || !source.IndependentlyLabeled)
                throw new InvalidDataException("Fonte rotulada independente de TRAIN_SELECTION obrigatória; TEST/VALIDATION proibidos.");
            CheckSha(source.SourceFingerprintSha256, "source");
            CheckSha(source.IndependentBaseIdentityFingerprintSha256, "identity group");
            if (!seen.Add(source.SourceFingerprintSha256))
                throw new InvalidDataException("Fonte repetida contaria risco de duplicação duas vezes.");
        }
        var nGroups = sources.Select(x => x.IndependentBaseIdentityFingerprintSha256)
            .Distinct(StringComparer.Ordinal).Count();
        var nRepresented = sources.Count(x => x.GoldAlreadyContainsTrueIdentity);
        var originalAccepted = sources.LongCount(x => x.OriginalDecision.Status == ResolutionStatus.RESOLVIDO);
        if (nGroups < policy.MinimumIndependentIdentityGroups
            || nRepresented < policy.MinimumAlreadyRepresentedSources)
            throw new InvalidDataException("Suporte TRAIN insuficiente para política de não-vínculo.");
        var candidates = new List<Candidate>();
        foreach (var threshold in policy.CandidateLowerThresholds.OrderBy(x => x))
        {
            long creates = 0, good = 0, wrong = 0, pending0 = 0;
            foreach (var source in sources)
            {
                var decision = V9LowerNonMatchPreview.Evaluate(
                    source.OriginalDecision, source.Retrieval,
                    policy.FrozenSnapshotSha256, threshold, policy.FixedUpperThreshold);
                if (decision.Decision is V9LowerNonMatchPreview.Disposition.PossibleLink
                    or V9LowerNonMatchPreview.Disposition.TechnicalAbstention)
                    pending0++;
                if (decision.Decision == V9LowerNonMatchPreview.Disposition.CandidateRejectedNewReferenceHypothesis)
                {
                    creates++;
                    if (source.GoldAlreadyContainsTrueIdentity) wrong++;
                    else good++;
                }
            }
            // Original baseline treats every candidate-rejected hypothesis as
            // pending, not as an existing no-candidate creation.
            var baselinePending = pending0 + creates;
            var falseRate = creates == 0 ? (double?)null : (double)wrong / creates;
            var alreadyRate = (double)wrong / nRepresented;
            candidates.Add(new Candidate(threshold, creates, good, wrong,
                baselinePending, pending0, falseRate, alreadyRate,
                MeetsPointBudgets: creates > 0 && falseRate.HasValue
                    && falseRate.Value <= policy.MaximumFalseNewAmongCreates
                    && alreadyRate <= policy.MaximumFalseNewAmongAlreadyRepresented,
                IsParetoFrontier: false));
        }
        if (candidates.Select(x => x.BaselinePending).Distinct().Count() != 1)
            throw new InvalidDataException("Baseline alterou pendências ao variar só o limiar inferior.");
        // Nondominated on duplicate errors and pending count, with lower
        // threshold as deterministic tiebreaker only.
        var frontier = candidates.Select(x => x with
        {
            IsParetoFrontier = !candidates.Any(y =>
                y.FalseNewReferences <= x.FalseNewReferences
                && y.PendingAfter <= x.PendingAfter
                && (y.FalseNewReferences < x.FalseNewReferences
                    || y.PendingAfter < x.PendingAfter))
        }).ToArray();
        var suggested = frontier.Where(x => x.MeetsPointBudgets)
            .OrderBy(x => x.PendingAfter)
            .ThenBy(x => x.FalseNewReferences)
            .ThenBy(x => x.LowerThreshold)
            .FirstOrDefault();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Version + "|" + policy.FrozenSnapshotSha256 + "|"
            + policy.TrainingEvidenceSha256 + "|"
            + policy.FixedUpperThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|" + string.Join(",", policy.CandidateLowerThresholds.OrderBy(x => x)
                .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            + "|" + policy.MaximumFalseNewAmongCreates.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "|" + policy.MaximumFalseNewAmongAlreadyRepresented.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "|" + policy.MinimumIndependentIdentityGroups
            + "|" + policy.MinimumAlreadyRepresentedSources
        ))).ToLowerInvariant();
        return new Study(Version, policy.FrozenSnapshotSha256, policy.TrainingEvidenceSha256,
            fingerprint, nGroups, nRepresented, originalAccepted,
            frontier, suggested?.LowerThreshold,
            suggested is null ? "NO_TRAIN_POINT_CANDIDATE_MEETS_DUPLICATE_BUDGET"
                : "TRAIN_POINT_SUGGESTION_ONLY_UNCERTIFIED_BLOCKING_MISS_AND_DUPLICATE_DEPENDENCE",
            ExternalValidationUsed: false, TestUsed: false, OperationalModelCreated: false,
            AutomaticCalibrationOrActivationAllowed: false,
            DuplicateConfidenceCertified: false, LinkageFdrRecalibrated: false);
    }

    private static void CheckSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("SHA-256 inválido: " + label);
    }
}

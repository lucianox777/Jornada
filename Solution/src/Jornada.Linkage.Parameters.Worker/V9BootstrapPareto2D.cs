namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Pure VALIDATION source-unit Pareto selection. Never freezes or activates a model.</summary>
public static class V9BootstrapPareto2D
{
    public const double InitialMaximumP95Milliseconds = 60000d;
    public const string SelectionVersion = "V9_BOOTSTRAP_PARETO_2D_IDEAL_DISTANCE_V2";
    public sealed record SelectionPolicy(double MinimumGlobalRecall, double MaximumAcceptedFdrUpper,
        int MinimumIndependentIdentityGroups, double MaximumP95Milliseconds = InitialMaximumP95Milliseconds);
    public sealed record CandidateOption(string OptionId, string ConfigurationFingerprintSha256,
        double GlobalRecall, double? AcceptedFdr, double? PhysicalP95Milliseconds,
        long TrueAccepted, long FalseAccepted, long GlobalEligibleTruth,
        int IndependentValidationIdentityGroups, bool PhysicalEndToEndAttested,
        bool IdentityClusterUncertaintyAttested,
        double? IdentityClusterFdrUpper = null, int MultiplicityComparedConfigurations = 0,
        IReadOnlyList<string>? EvidenceRejectionReasons = null);
    public sealed record Evaluation(CandidateOption Option, bool Eligible, string? ExclusionReason,
        bool OnFrontier, IReadOnlyList<string> ExclusionReasons,
        IReadOnlyList<string> DominatedByOptionIds, IReadOnlyList<string> EquivalentOptionIds,
        double? IdealSquaredDistance);
    public sealed record Result(string Version, double MaximumP95Milliseconds,
        IReadOnlyList<Evaluation> Table, string? InitialSuggestedOptionId, string Status,
        SelectionPolicy Policy);

    // Compatibility only: no implicit risk certification. Callers should supply their declared policy.
    public static Result Evaluate(IEnumerable<CandidateOption> options,
        double maximumP95Milliseconds = InitialMaximumP95Milliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Evaluate(options.Select(p => p is null ? null! : p with
        {
            EvidenceRejectionReasons = (p.EvidenceRejectionReasons ?? [])
                .Append("POLITICA_DE_RISCO_NAO_DECLARADA").ToArray()
        }), new SelectionPolicy(0, 1, 2, maximumP95Milliseconds));
    }

    public static Result Evaluate(IEnumerable<CandidateOption> options, SelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Probability(policy.MinimumGlobalRecall) || !Probability(policy.MaximumAcceptedFdrUpper)
            || policy.MinimumIndependentIdentityGroups < 2
            || !double.IsFinite(policy.MaximumP95Milliseconds) || policy.MaximumP95Milliseconds <= 0)
            throw new ArgumentException("Política de seleção inválida.", nameof(policy));
        var all = options.Select(p => p is null ? null! : p with
        {
            EvidenceRejectionReasons = Array.AsReadOnly((p.EvidenceRejectionReasons ?? []).ToArray())
        }).ToArray();
        if (all.Length == 0 || all.Any(p => p is null || string.IsNullOrWhiteSpace(p.OptionId)))
            throw new ArgumentException("Configurações e identificadores obrigatórios.", nameof(options));
        if (all.Select(p => p.OptionId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("OptionId duplicado.", nameof(options));
        var sorted = all.OrderBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
            .ThenBy(p => p.OptionId, StringComparer.Ordinal).ToArray();
        IReadOnlyList<string> Reasons(CandidateOption p)
        {
            var reasons = new List<string>();
            if (!Hash(p.ConfigurationFingerprintSha256)) reasons.Add("FINGERPRINT_INVALIDO");
            if (!Probability(p.GlobalRecall) || p.AcceptedFdr.HasValue && !Probability(p.AcceptedFdr.Value)
                || p.IdentityClusterFdrUpper.HasValue && !Probability(p.IdentityClusterFdrUpper.Value))
                reasons.Add("METRICAS_INVALIDAS");
            var countsValid = p.TrueAccepted >= 0 && p.FalseAccepted >= 0
                && p.GlobalEligibleTruth > 0 && p.TrueAccepted <= p.GlobalEligibleTruth
                && p.TrueAccepted <= long.MaxValue - p.FalseAccepted;
            if (!countsValid) reasons.Add("CONTAGENS_OU_DENOMINADOR_INVALIDOS");
            var accepted = countsValid ? p.TrueAccepted + p.FalseAccepted : 0;
            if (!p.AcceptedFdr.HasValue || accepted == 0) reasons.Add("FDR_INDEFINIDO_SEM_ACEITOS");
            if (countsValid && (Math.Abs(p.GlobalRecall - (double)p.TrueAccepted / p.GlobalEligibleTruth) > 1e-10
                || accepted > 0 && p.AcceptedFdr.HasValue
                    && Math.Abs(p.AcceptedFdr.Value - (double)p.FalseAccepted / accepted) > 1e-10))
                reasons.Add("METRICAS_INCONSISTENTES");
            if (p.GlobalRecall < policy.MinimumGlobalRecall) reasons.Add("RECALL_ABAIXO_DA_POLITICA");
            if (!p.PhysicalP95Milliseconds.HasValue || !p.PhysicalEndToEndAttested)
                reasons.Add("P95_FISICO_NAO_ATESTADO");
            if (p.PhysicalP95Milliseconds.HasValue && (!double.IsFinite(p.PhysicalP95Milliseconds.Value)
                || p.PhysicalP95Milliseconds < 0)) reasons.Add("P95_INVALIDO");
            if (p.PhysicalP95Milliseconds > policy.MaximumP95Milliseconds) reasons.Add("P95_ACIMA_DO_LIMITE");
            if (p.IndependentValidationIdentityGroups < policy.MinimumIndependentIdentityGroups
                || !p.IdentityClusterUncertaintyAttested || !p.IdentityClusterFdrUpper.HasValue
                || p.MultiplicityComparedConfigurations < all.Length)
                reasons.Add("EVIDENCIA_ESTATISTICA_INSUFICIENTE");
            if (p.IdentityClusterFdrUpper.HasValue && p.AcceptedFdr.HasValue
                && p.IdentityClusterFdrUpper.Value + 1e-12 < p.AcceptedFdr.Value)
                reasons.Add("LIMITE_FDR_INCONSISTENTE");
            if (p.IdentityClusterFdrUpper > policy.MaximumAcceptedFdrUpper)
                reasons.Add("LIMITE_FDR_ACIMA_DA_POLITICA");
            reasons.AddRange(p.EvidenceRejectionReasons!.Where(x => !string.IsNullOrWhiteSpace(x)));
            return Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray());
        }
        var reasonsById = sorted.ToDictionary(p => p.OptionId, Reasons, StringComparer.Ordinal);
        var eligible = sorted.Where(p => reasonsById[p.OptionId].Count == 0).ToArray();
        static bool Dominates(CandidateOption a, CandidateOption b) =>
            a.GlobalRecall >= b.GlobalRecall && a.AcceptedFdr!.Value <= b.AcceptedFdr!.Value
            && (a.GlobalRecall > b.GlobalRecall || a.AcceptedFdr.Value < b.AcceptedFdr.Value);
        var dominators = sorted.ToDictionary(p => p.OptionId,
            p => reasonsById[p.OptionId].Count == 0 ? eligible.Where(q => Dominates(q, p)).Select(q => q.OptionId).ToArray() : [], StringComparer.Ordinal);
        var front = eligible.Where(p => dominators[p.OptionId].Length == 0).ToArray();
        var distances = new Dictionary<string, double>(StringComparer.Ordinal);
        if (front.Length > 0)
        {
            var rMin = front.Min(p => p.GlobalRecall); var rMax = front.Max(p => p.GlobalRecall);
            var fMin = front.Min(p => p.AcceptedFdr!.Value); var fMax = front.Max(p => p.AcceptedFdr!.Value);
            foreach (var p in front)
            {
                var r = rMax == rMin ? 0 : (rMax - p.GlobalRecall) / (rMax - rMin);
                var f = fMax == fMin ? 0 : (p.AcceptedFdr!.Value - fMin) / (fMax - fMin);
                distances[p.OptionId] = r * r + f * f;
            }
        }
        var selected = front.OrderBy(p => distances[p.OptionId])
            .ThenBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
            .ThenBy(p => p.OptionId, StringComparer.Ordinal).FirstOrDefault();
        var table = sorted.Select(p => new Evaluation(p, reasonsById[p.OptionId].Count == 0,
            reasonsById[p.OptionId].Count == 0 ? null : reasonsById[p.OptionId][0], distances.ContainsKey(p.OptionId), reasonsById[p.OptionId],
            Array.AsReadOnly(dominators[p.OptionId]), Array.AsReadOnly(eligible.Where(q => q.OptionId != p.OptionId
                && q.GlobalRecall == p.GlobalRecall && q.AcceptedFdr == p.AcceptedFdr).Select(q => q.OptionId).ToArray()),
            distances.TryGetValue(p.OptionId, out var distance) ? distance : null)).ToArray();
        return new(SelectionVersion, policy.MaximumP95Milliseconds, Array.AsReadOnly(table), selected?.OptionId,
            selected is null ? "NO_ELIGIBLE_CONFIGURATION" : "SUGGESTION_ONLY_NOT_FROZEN", policy);
    }
    private static bool Probability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

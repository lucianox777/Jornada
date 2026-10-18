namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Deterministic, pure TRAIN/VALIDATION bootstrap selection. Not a physical runner,
/// freeze store, TEST gate, or authorization for activation.
/// </summary>
public static class V9BootstrapPareto2D
{
    public const double InitialMaximumP95Milliseconds = 60000d;
    public const string SelectionVersion = "V9_BOOTSTRAP_PARETO_2D_IDEAL_DISTANCE_V1";

    public sealed record Option(
        string OptionId,
        string ConfigurationFingerprintSha256,
        double GlobalRecall,
        double? AcceptedFdr,
        double? PhysicalP95Milliseconds,
        long TrueAccepted,
        long FalseAccepted,
        long GlobalEligibleTruth,
        int IndependentValidationIdentityGroups,
        bool PhysicalEndToEndAttested,
        bool IdentityClusterUncertaintyAttested);

    public sealed record Evaluation(
        Option Option,
        bool Eligible,
        string? ExclusionReason,
        bool OnFrontier);

    public sealed record Result(
        string Version,
        double MaximumP95Milliseconds,
        IReadOnlyList<Evaluation> Table,
        string? InitialSuggestedOptionId,
        string Status);

    public static Result Evaluate(
        IEnumerable<Option> options,
        double maximumP95Milliseconds = InitialMaximumP95Milliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(maximumP95Milliseconds) || maximumP95Milliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumP95Milliseconds));
        var all = options.ToArray();
        if (all.Length == 0) throw new ArgumentException("Nenhuma configuração.", nameof(options));
        if (all.Any(p => p is null || string.IsNullOrWhiteSpace(p.OptionId)
            || p.ConfigurationFingerprintSha256 is null
            || p.ConfigurationFingerprintSha256.Length != 64
            || p.ConfigurationFingerprintSha256.Any(c => !Uri.IsHexDigit(c))
            || !double.IsFinite(p.GlobalRecall) || p.GlobalRecall is < 0 or > 1
            || (p.AcceptedFdr.HasValue && (!double.IsFinite(p.AcceptedFdr.Value)
                || p.AcceptedFdr.Value is < 0 or > 1))
            || (p.PhysicalP95Milliseconds.HasValue && (!double.IsFinite(p.PhysicalP95Milliseconds.Value)
                || p.PhysicalP95Milliseconds.Value < 0))
            || p.TrueAccepted < 0 || p.FalseAccepted < 0 || p.GlobalEligibleTruth <= 0
            || p.TrueAccepted > p.GlobalEligibleTruth || p.IndependentValidationIdentityGroups < 0))
            throw new ArgumentException("Opção inválida.", nameof(options));
        if (all.Select(p => p.OptionId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("OptionId duplicado.", nameof(options));

        var sorted = all.OrderBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
            .ThenBy(p => p.OptionId, StringComparer.Ordinal).ToArray();
        string? Reason(Option p)
        {
            var accepted = checked(p.TrueAccepted + p.FalseAccepted);
            if (p.AcceptedFdr is null || accepted == 0) return "FDR_INDEFINIDO_SEM_ACEITOS";
            if (Math.Abs(p.GlobalRecall - (double)p.TrueAccepted / p.GlobalEligibleTruth) > 1e-10
                || Math.Abs(p.AcceptedFdr.Value - (double)p.FalseAccepted / accepted) > 1e-10)
                return "METRICAS_INCONSISTENTES";
            if (p.PhysicalP95Milliseconds is null || !p.PhysicalEndToEndAttested)
                return "P95_FISICO_NAO_ATESTADO";
            if (p.PhysicalP95Milliseconds > maximumP95Milliseconds)
                return "P95_ACIMA_DO_LIMITE";
            if (p.IndependentValidationIdentityGroups < 2 || !p.IdentityClusterUncertaintyAttested)
                return "EVIDENCIA_ESTATISTICA_INSUFICIENTE";
            return null;
        }
        var reasons = sorted.ToDictionary(p => p.OptionId, Reason, StringComparer.Ordinal);
        var eligible = sorted.Where(p => reasons[p.OptionId] is null).ToArray();
        static bool Dominates(Option a, Option b) =>
            a.GlobalRecall >= b.GlobalRecall && a.AcceptedFdr!.Value <= b.AcceptedFdr!.Value
            && (a.GlobalRecall > b.GlobalRecall || a.AcceptedFdr.Value < b.AcceptedFdr.Value);
        var front = eligible.Where(p => !eligible.Any(q => !ReferenceEquals(p, q) && Dominates(q, p)))
            .ToArray();
        var frontIds = front.Select(p => p.OptionId).ToHashSet(StringComparer.Ordinal);
        Option? suggestion = null;
        if (front.Length > 0)
        {
            var rMin = front.Min(p => p.GlobalRecall);
            var rMax = front.Max(p => p.GlobalRecall);
            var fMin = front.Min(p => p.AcceptedFdr!.Value);
            var fMax = front.Max(p => p.AcceptedFdr!.Value);
            suggestion = front.OrderBy(p =>
            {
                var r = rMax == rMin ? 0 : (rMax - p.GlobalRecall) / (rMax - rMin);
                var f = fMax == fMin ? 0 : (p.AcceptedFdr!.Value - fMin) / (fMax - fMin);
                return r * r + f * f;
            }).ThenBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
              .ThenBy(p => p.OptionId, StringComparer.Ordinal).First();
        }
        var table = sorted.Select(p => new Evaluation(p, reasons[p.OptionId] is null,
            reasons[p.OptionId], frontIds.Contains(p.OptionId))).ToArray();
        return new Result(SelectionVersion, maximumP95Milliseconds, table,
            suggestion?.OptionId, suggestion is null ? "NO_ELIGIBLE_CONFIGURATION" : "SUGGESTION_ONLY_NOT_FROZEN");
    }
}

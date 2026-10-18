namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Pure VALIDATION-only 3-objective Pareto diagnostic. This component does not
/// fit parameters, access TEST, certify evidence or activate models.
/// </summary>
public static class V9ThreeObjectivePareto
{
    public sealed record Point(
        string OptionId,
        string ConfigurationFingerprintSha256,
        double GlobalRecall,
        double? AcceptedFdr,
        double? PhysicalP95Milliseconds);

    public sealed record Result(
        IReadOnlyList<Point> NonDominated,
        IReadOnlyDictionary<string, string> PairSuggestions,
        IReadOnlyList<Point> Incomplete);

    public static Result Evaluate(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var all = points.ToArray();
        if (all.Any(p => p is null || string.IsNullOrWhiteSpace(p.OptionId)
            || string.IsNullOrWhiteSpace(p.ConfigurationFingerprintSha256)))
            throw new ArgumentException("Identidade de configuração obrigatória.");
        if (all.Select(p => p.OptionId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("OptionId duplicado.");
        if (all.Any(p => !double.IsFinite(p.GlobalRecall) || p.GlobalRecall is < 0 or > 1
            || (p.AcceptedFdr.HasValue &&
                (!double.IsFinite(p.AcceptedFdr.Value) || p.AcceptedFdr.Value is < 0 or > 1))
            || (p.PhysicalP95Milliseconds.HasValue &&
                (!double.IsFinite(p.PhysicalP95Milliseconds.Value) || p.PhysicalP95Milliseconds.Value < 0))))
            throw new ArgumentException("Métrica não finita ou fora do domínio.");

        var complete = all.Where(p => p.AcceptedFdr.HasValue && p.PhysicalP95Milliseconds.HasValue)
            .OrderBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
            .ThenBy(p => p.OptionId, StringComparer.Ordinal).ToArray();
        var incomplete = all.Except(complete).ToArray();
        var frontier = complete.Where(p => !complete.Any(q => !ReferenceEquals(p, q) && Dominates(q, p)))
            .ToArray();
        var suggestions = new Dictionary<string, string>(StringComparer.Ordinal);
        Suggest("RECALL_FDR", p => 1 - p.GlobalRecall, p => p.AcceptedFdr!.Value);
        Suggest("RECALL_P95", p => 1 - p.GlobalRecall, p => p.PhysicalP95Milliseconds!.Value);
        Suggest("FDR_P95", p => p.AcceptedFdr!.Value, p => p.PhysicalP95Milliseconds!.Value);
        return new Result(frontier, suggestions, incomplete);

        void Suggest(string name, Func<Point, double> first, Func<Point, double> second)
        {
            if (frontier.Length == 0) return;
            var loA = frontier.Min(first); var hiA = frontier.Max(first);
            var loB = frontier.Min(second); var hiB = frontier.Max(second);
            static double Norm(double value, double lo, double hi) =>
                hi == lo ? 0 : (value - lo) / (hi - lo);
            var winner = frontier.OrderBy(p =>
            {
                var a = Norm(first(p), loA, hiA);
                var b = Norm(second(p), loB, hiB);
                return a * a + b * b;
            }).ThenBy(p => p.ConfigurationFingerprintSha256, StringComparer.Ordinal)
              .ThenBy(p => p.OptionId, StringComparer.Ordinal).First();
            suggestions.Add(name, winner.OptionId);
        }
    }

    private static bool Dominates(Point a, Point b) =>
        a.GlobalRecall >= b.GlobalRecall
        && a.AcceptedFdr!.Value <= b.AcceptedFdr!.Value
        && a.PhysicalP95Milliseconds!.Value <= b.PhysicalP95Milliseconds!.Value
        && (a.GlobalRecall > b.GlobalRecall
            || a.AcceptedFdr.Value < b.AcceptedFdr.Value
            || a.PhysicalP95Milliseconds.Value < b.PhysicalP95Milliseconds.Value);
}

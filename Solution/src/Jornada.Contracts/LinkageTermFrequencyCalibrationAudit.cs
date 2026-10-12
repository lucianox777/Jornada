namespace Jornada.Contracts;

/// <summary>
/// Observability-only diagnostic for a V8 term-frequency model under construction.
/// Emits aggregate m/u factors and the most negative adjustment over published
/// first-token frequencies. Never changes scores, decisions, training or approvals.
/// This is NOT a certification of calibration, monotonicity or false-positive risk.
/// </summary>
public static class LinkageTermFrequencyCalibrationAudit
{
    public sealed record AttributeAssessment(
        string Attribute,
        decimal MExact,
        decimal UExact,
        decimal MHigh,
        decimal UHigh,
        decimal MaximumPublishedFirstNameFrequency,
        decimal Weight,
        decimal MinimumU,
        double ExactBaseLogOdds,
        double HighBaseLogOdds,
        double WorstTfAdjustment,
        double WorstExactLogOdds,
        double WorstExactMinusHigh)
    {
        public bool CouldInvert => !double.IsFinite(WorstExactMinusHigh) || WorstExactMinusHigh <= 0d;
    }

    /// <summary>
    /// Returns null when the diagnostic inputs are missing or invalid; an
    /// unavailable diagnostic must not alter legacy/operational code paths.
    /// Published term *values* and individual identifiers are never returned.
    /// </summary>
    public static IReadOnlyList<AttributeAssessment>? TryAssess(
        IReadOnlyDictionary<string, decimal> parameters,
        NominalTermFrequencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyScoring, out var enabled)
            || enabled < 1m
            || !parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyWeight, out var weight)
            || weight < 0m
            || !parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyMinimumU, out var minU)
            || minU <= 0m || minU > 1m
            || snapshot.PersonFirstNameCount == 0 || snapshot.MotherFirstNameCount == 0)
            return null;

        var person = snapshot.PersonFirstNames.Max(x => x.Frequency);
        var mother = snapshot.MotherFirstNames.Max(x => x.Frequency);
        var name = Assess("NOME", person);
        var motherName = Assess("NOME_MAE", mother);
        return name is null || motherName is null ? null : new[] { name, motherName };

        AttributeAssessment? Assess(string attribute, decimal maximum)
        {
            if (!TryProbability("M_" + attribute + "_EXACT", out var mExact)
                || !TryProbability("U_" + attribute + "_EXACT", out var uExact)
                || !TryProbability("M_" + attribute + "_HIGH", out var mHigh)
                || !TryProbability("U_" + attribute + "_HIGH", out var uHigh))
                return null;

            var exactBase = Math.Log((double)mExact / (double)uExact);
            var highBase = Math.Log((double)mHigh / (double)uHigh);
            var tf = SplinkCompatibleTermFrequency.LogBayesAdjustment(
                maximum, maximum, uExact, weight, minU);
            return new AttributeAssessment(attribute, mExact, uExact, mHigh, uHigh,
                maximum, weight, minU, exactBase, highBase, tf,
                exactBase + tf, exactBase + tf - highBase);
        }

        bool TryProbability(string name, out decimal value)
            => parameters.TryGetValue(name, out value) && value > 0m && value <= 1m;
    }
}

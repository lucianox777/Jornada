namespace Jornada.Linkage.Parameters.Worker;

public enum CalibrationDecision
{
    Match,
    NonMatch,
    Inconclusive
}

public sealed record CalibrationEvaluation(
    string CandidateId,
    long TruePositive,
    long TrueNegative,
    long FalsePositive,
    long FalseNegative,
    long Inconclusive,
    long Total)
{
    public decimal InconclusiveRate => Total == 0 ? 0m : (decimal)Inconclusive / Total;
}

/// <summary>
/// Seleção técnica sem impor custo institucional entre falso positivo e falso negativo.
/// Um candidato é eliminado somente quando outro é não-pior em FP e FN e estritamente
/// melhor em pelo menos um deles; em empate de FP/FN, menor inconclusão domina.
/// </summary>
public static class CalibrationCandidatePareto
{
    public static IReadOnlyList<CalibrationEvaluation> NonDominated(
        IEnumerable<CalibrationEvaluation> evaluations)
    {
        var items = evaluations.ToArray();
        if (items.Select(x => x.CandidateId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("CandidateId deve ser único.", nameof(evaluations));

        return items
            .Where(candidate => !items.Any(other =>
                !ReferenceEquals(candidate, other) && Dominates(other, candidate)))
            .OrderBy(x => x.FalsePositive)
            .ThenBy(x => x.FalseNegative)
            .ThenBy(x => x.Inconclusive)
            .ThenBy(x => x.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool Dominates(CalibrationEvaluation left, CalibrationEvaluation right)
    {
        var noWorseErrors = left.FalsePositive <= right.FalsePositive &&
                            left.FalseNegative <= right.FalseNegative;
        if (!noWorseErrors)
            return false;

        var strictlyBetterError = left.FalsePositive < right.FalsePositive ||
                                  left.FalseNegative < right.FalseNegative;
        if (strictlyBetterError)
            return true;

        return left.Inconclusive < right.Inconclusive;
    }
}

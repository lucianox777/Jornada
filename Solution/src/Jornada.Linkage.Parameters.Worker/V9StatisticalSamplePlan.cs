namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Predeclared conservative design calculations, never empirical certification.</summary>
public static class V9StatisticalSamplePlan
{
    public sealed record Design(double Alpha, double Power, double RecallNull,
        double RecallAlternative, double RecallHalfWidth, double CellHalfWidth,
        double MinimumCellProbability, int Cells, int ComparedConfigurations,
        int MaximumAcceptedPerIndependentUnit, double ExpectedAcceptedPerIndependentUnit,
        double ValidationFraction, double TestFraction, double RetainedIndependentUnitFraction,
        double ExpectedEligibleSourcesPerIndependentUnit);
    public sealed record RiskRequirement(double MaximumFdr, long? ValidationIndependentUnitsZeroError,
        long? TestIndependentUnitsZeroError);
    public sealed record Report(Design Assumptions, long RecallPrecisionIndependentUnits,
        long RecallPowerIndependentUnits, long TrainingIndependentPairsPerClassForCellPrecision,
        long TrainingIndependentPairsPerClassForAnyCellObservation,
        IReadOnlyList<RiskRequirement> RiskBands, long? RequiredBaseUnits,
        string Status = "CONDITIONAL_DESIGN_NOT_IID_OR_REPRESENTATIVENESS_EVIDENCE");

    public static Report Calculate(Design d, IReadOnlyCollection<double> riskCeilings)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(riskCeilings);
        var probabilities = new[] { d.Alpha, d.Power, d.RecallNull, d.RecallAlternative,
            d.RecallHalfWidth, d.CellHalfWidth, d.MinimumCellProbability,
            d.ValidationFraction, d.TestFraction, d.RetainedIndependentUnitFraction };
        if (probabilities.Any(x => !double.IsFinite(x) || x is <= 0 or >= 1)
            || d.RecallAlternative <= d.RecallNull || d.ValidationFraction + d.TestFraction >= 1
            || d.Cells < 1 || d.ComparedConfigurations < 1 || d.MaximumAcceptedPerIndependentUnit < 1
            || !double.IsFinite(d.ExpectedAcceptedPerIndependentUnit)
            || d.ExpectedAcceptedPerIndependentUnit <= 0
            || d.ExpectedAcceptedPerIndependentUnit > d.MaximumAcceptedPerIndependentUnit
            || !double.IsFinite(d.ExpectedEligibleSourcesPerIndependentUnit)
            || d.ExpectedEligibleSourcesPerIndependentUnit <= 0
            || d.ExpectedEligibleSourcesPerIndependentUnit > d.MaximumAcceptedPerIndependentUnit
            || riskCeilings.Count == 0 || riskCeilings.Any(x => !double.IsFinite(x) || x is <= 0 or >= 1)
            || riskCeilings.Distinct().Count() != riskCeilings.Count)
            throw new InvalidDataException("Desenho amostral exige hipóteses finitas, faixas e alocações válidas.");
        // Four predeclared design families: recall, cell precision, coverage, FDR.
        var familyAlpha = d.Alpha / 4;
        var recallAlpha = familyAlpha / d.ComparedConfigurations;
        // Recall is a ratio of bounded SOURCE COUNTS per independent identity,
        // not a Bernoulli observation per person. Simultaneous numerator and
        // denominator deviations t give ratio error <= 2t/(E[Y]-t).
        var t = d.RecallHalfWidth * d.ExpectedEligibleSourcesPerIndependentUnit / (2 + d.RecallHalfWidth);
        var precision = Ceiling(d.MaximumAcceptedPerIndependentUnit * (double)d.MaximumAcceptedPerIndependentUnit
            * Math.Log(4 / recallAlpha) / (2 * t * t));
        // Hoeffding: null-tail alpha plus alternative-tail beta, no Gaussian approximation.
        var gap = d.RecallAlternative - d.RecallNull;
        var power = Ceiling(Math.Pow(Math.Sqrt(Math.Log(1 / recallAlpha) / 2)
            + Math.Sqrt(Math.Log(1 / (1 - d.Power)) / 2), 2)
            * Math.Pow(d.MaximumAcceptedPerIndependentUnit / d.ExpectedEligibleSourcesPerIndependentUnit, 2) / (gap * gap));
        var cellPrecision = Ceiling(Math.Log(4d * d.Cells * d.ComparedConfigurations / familyAlpha) / (2 * d.CellHalfWidth * d.CellHalfWidth));
        var cellCoverage = Ceiling(Math.Log(familyAlpha / (2d * d.Cells * d.ComparedConfigurations)) / Math.Log(1 - d.MinimumCellProbability));
        var requirements = riskCeilings.OrderBy(x => x).Select(x => new RiskRequirement(x,
            RequiredZeroError(d, x, familyAlpha, d.ComparedConfigurations),
            RequiredZeroError(d, x, familyAlpha, 1))).ToArray();
        long? baseUnits = null;
        if (requirements.All(x => x.ValidationIndependentUnitsZeroError.HasValue && x.TestIndependentUnitsZeroError.HasValue))
        {
            var validation = Math.Max(Math.Max(precision, power), requirements.Max(x => x.ValidationIndependentUnitsZeroError!.Value));
            var test = Math.Max(Math.Max(precision, power), requirements.Max(x => x.TestIndependentUnitsZeroError!.Value));
            baseUnits = Ceiling(Math.Max(validation / d.ValidationFraction, test / d.TestFraction)
                / d.RetainedIndependentUnitFraction);
        }
        // Training requirements are PAIRS PER CLASS. No invented conversion into
        // independent people: the observed M/U yield and dependencies must be supplied.
        return new(d, precision, power, cellPrecision, cellCoverage, Array.AsReadOnly(requirements), baseUnits);
    }

    public sealed record FinalistRiskDesign(double Alpha, int ExternalValidationMultiplicity,
        int FrozenTestMultiplicity, int MaximumAcceptedPerIdentity, double AssumedMeanAcceptedPerIdentity,
        IReadOnlyList<RiskRequirement> RiskBands,
        string Scope = "FROZEN_FINALISTS_ONLY_SEARCH_AND_HISTORICAL_LOOKS_EXCLUDED_FROM_FRESH_HOLDOUT_FAMILY",
        string Assumptions = "IID_IDENTITIES_ZERO_ERRORS_ASSUMED_FUTURE_ACCEPTANCE_YIELD_NOT_CERTIFICATION");

    public static FinalistRiskDesign CalculateFinalistFdrOnly(int finalists, int cap,
        double assumedMeanAccepted, IReadOnlyCollection<double> ceilings, double alpha = .05)
    {
        if (finalists is < 1 or > 3 || cap < 1 || !double.IsFinite(assumedMeanAccepted)
            || assumedMeanAccepted <= 0 || assumedMeanAccepted > cap || !double.IsFinite(alpha)
            || alpha is <= 0 or >= 1 || ceilings.Count == 0
            || ceilings.Any(x => !double.IsFinite(x) || x is <= 0 or >= 1)
            || ceilings.Distinct().Count() != ceilings.Count)
            throw new InvalidDataException("Plano externo exige um a três finalistas, cap, rendimento e faixas explícitos.");
        return new(alpha, finalists, 1, cap, assumedMeanAccepted,
            Array.AsReadOnly(ceilings.OrderBy(x => x).Select(x => new RiskRequirement(x,
                RequiredZeroError(cap, assumedMeanAccepted, x, alpha, finalists),
                RequiredZeroError(cap, assumedMeanAccepted, x, alpha, 1))).ToArray()));
    }

    private static long? RequiredZeroError(Design d, double target, double alpha, int compared) =>
        RequiredZeroError(d.MaximumAcceptedPerIndependentUnit, d.ExpectedAcceptedPerIndependentUnit, target, alpha, compared);

    private static long? RequiredZeroError(int cap, double meanAccepted, double target, double alpha, int compared)
    {
        var logarithm = Math.Log(2d * compared / alpha);
        bool Meets(long n)
        {
            var lower = meanAccepted
                - cap * Math.Sqrt(logarithm / (2d * n));
            var cp = 1 - Math.Exp(-logarithm / n);
            return lower > 0 && cap * cp / lower <= target;
        }
        // Evidence DTO uses Int32 independent groups. Absence of a supported
        // design is explicit, never clipped into a feasible sample size.
        if (!Meets(int.MaxValue)) return null;
        long low = 2, high = int.MaxValue;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Meets(middle)) high = middle; else low = middle + 1;
        }
        return low;
    }

    private static long Ceiling(double value) => double.IsFinite(value) && value <= long.MaxValue
        ? checked((long)Math.Ceiling(value)) : throw new InvalidDataException("Desenho excede capacidade numérica.");
}

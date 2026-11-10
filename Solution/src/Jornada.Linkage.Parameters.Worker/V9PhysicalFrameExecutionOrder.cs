using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Operational-only, deterministic order for already-declared SQL candidate frames.
/// More OR blocking passes and fewer conjunctive fields per pass can create
/// broader candidate-pair materializations. This is a cheap STRUCTURAL PROXY,
/// NOT SQL Server's estimated/executed cost or proven elapsed runtime.
/// Scientific model selection and plan fingerprints must never depend on it.
/// </summary>
public static class V9PhysicalFrameExecutionOrder
{
    public const string Strategy = "STRUCTURAL_BROADNESS_DESC_V1_NOT_SQL_COST";

    public static long StructuralBroadnessScore(IReadOnlyList<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new InvalidDataException("V9_SQL_COST_ORDER_EMPTY_FRAME");
        long score = 0;
        foreach (var pass in passes)
        {
            if (pass is null || pass.Fields is null || pass.Fields.Count == 0)
                throw new InvalidDataException("V9_SQL_COST_ORDER_INVALID_PASS");
            // Every additional conjunctive equality typically reduces
            // candidate pairs; OR passes increase their possible union.
            // This integer heuristic is stable across OS, culture and CPU.
            score = checked(score + Math.Max(1, 4096 / pass.Fields.Count));
        }
        return score;
    }

    public static IReadOnlyList<T> BroadestLikelyFirst<T>(IReadOnlyList<T> inputs,
        Func<T, IReadOnlyList<LinkageBlockingPass>> getPasses,
        Func<T, string> stableKey)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(getPasses);
        ArgumentNullException.ThrowIfNull(stableKey);
        var scored = inputs.Select(input =>
        {
            var key = stableKey(input);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException("V9_SQL_COST_ORDER_MISSING_STABLE_KEY");
            return (Input: input, Key: key, Score: StructuralBroadnessScore(getPasses(input)));
        }).ToArray();
        if (scored.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != scored.Length)
            throw new InvalidDataException("V9_SQL_COST_ORDER_DUPLICATE_STABLE_KEY");
        return Array.AsReadOnly(scored.OrderByDescending(x => x.Score)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Input).ToArray());
    }
}

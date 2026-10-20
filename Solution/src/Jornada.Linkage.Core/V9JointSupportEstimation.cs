using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public sealed record V9JointSupportPolicy(decimal ClassMassCoverage, long MinimumM, long MinimumU,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SupportedTailRule = null);
public sealed record V9JointObservedCell(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother,
    long M, long U);
public sealed record V9JointEstimatedCell(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother,
    long ObservedM, long ObservedU, bool CoreM, bool CoreU, string MethodM, string MethodU);
public sealed record V9JointSupportProvenance(string Version, string TrainCorpusSha256,
    V9JointSupportPolicy Policy, decimal SmoothingAlpha, long TotalM, long TotalU,
    decimal ObservedCoreMassM, decimal ObservedCoreMassU, decimal FallbackScaleM, decimal FallbackScaleU,
    IReadOnlyList<V9JointEstimatedCell> Cells,
    string StructuralExclusionContract = "NONE_PROVEN_FOR_UNIVERSAL_81_STATE_NOMINAL_CONTRACT",
    string FallbackAssumption = "PERSON_MOTHER_CONDITIONAL_INDEPENDENCE_WITHIN_CLASS_AND_SAME_BLOCKED_TRAIN_FRAME");

/// <summary>
/// One estimator shared by fitting and portable-snapshot verification. The core is
/// selected separately in M and U, without looking at selection/holdout outcomes.
/// Rare states share the remaining smoothed mass in proportion to the product
/// of TRAIN marginals; this is an explicit modelling assumption, not observed support.
/// </summary>
public static class V9JointSupportEstimation
{
    public const string Version = "V9_CLASSWISE_99_MASS_JOINT_MARGINAL_BACKOFF_V1";
    public const string SupportedTailVersion = "V9_CLASSWISE_MASS_SUPPORTED_TAIL_MARGINAL_BACKOFF_V2";
    public const string SupportedTailRule = "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2";
    public const string Joint = "JOINT_DIRICHLET";
    public const string SupportedTail = "JOINT_DIRICHLET_SUPPORTED_TAIL";
    public const string Marginal = "CONDITIONAL_PRODUCT_MARGINALS";
    public sealed record Result(IReadOnlyList<FellegiSunterJointNominalCellV9> Probabilities,
        V9JointSupportProvenance Provenance);

    public static void ValidatePolicy(V9JointSupportPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.ClassMassCoverage is <= 0 or >= 1 || policy.MinimumM < 0 || policy.MinimumU < 0
            || policy.SupportedTailRule is not null && policy.SupportedTailRule != SupportedTailRule)
            throw new InvalidDataException("Cobertura de massa e suporte M/U inválidos.");
    }

    public static Result Estimate(IEnumerable<V9JointObservedCell> observed, V9JointSupportPolicy policy,
        decimal alpha, string trainCorpusSha256)
    {
        ValidatePolicy(policy);
        if (alpha is <= 0 or > 1000 || trainCorpusSha256 is not { Length: 64 }
            || trainCorpusSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Estimação exige alpha e proveniência TRAIN válidos.");
        var raw = observed.OrderBy(c => c.Person).ThenBy(c => c.Mother).ToArray();
        if (raw.Length != 81 || raw.Any(c => c.M < 0 || c.U < 0 || !Enum.IsDefined(c.Person) || !Enum.IsDefined(c.Mother))
            || raw.Select(c => (c.Person, c.Mother)).Distinct().Count() != 81)
            throw new InvalidDataException("Contagens das 81 células exclusivas obrigatórias.");
        var totalM = raw.Sum(c => c.M); var totalU = raw.Sum(c => c.U);
        if (totalM < 2 || totalU < 2) throw new InvalidDataException("TRAIN exige suporte total M e U.");
        var preserveSupportedTail = policy.SupportedTailRule == SupportedTailRule;
        var m = Fit(c => c.M, totalM, policy.MinimumM); var u = Fit(c => c.U, totalU, policy.MinimumU);
        var probabilities = raw.Select((c, i) => new FellegiSunterJointNominalCellV9(c.Person, c.Mother, m.P[i], u.P[i])).ToArray();
        var cells = raw.Select((c, i) => new V9JointEstimatedCell(c.Person, c.Mother, c.M, c.U,
            m.Core.Contains(i), u.Core.Contains(i), m.Core.Contains(i) ? Joint : m.Joint.Contains(i) ? SupportedTail : Marginal,
            u.Core.Contains(i) ? Joint : u.Joint.Contains(i) ? SupportedTail : Marginal)).ToArray();
        return new(Array.AsReadOnly(probabilities), new(preserveSupportedTail ? SupportedTailVersion : Version, trainCorpusSha256, policy, alpha, totalM, totalU,
            m.Coverage, u.Coverage, m.Scale, u.Scale, Array.AsReadOnly(cells)));

        (decimal[] P, HashSet<int> Core, HashSet<int> Joint, decimal Coverage, decimal Scale) Fit(Func<V9JointObservedCell, long> count, long total, long minimum)
        {
            var core = new HashSet<int>(); long accumulated = 0;
            // Stable enum-order tie break, including the cell crossing the coverage boundary.
            foreach (var index in Enumerable.Range(0, 81).OrderByDescending(i => count(raw[i])).ThenBy(i => i))
            {
                core.Add(index); accumulated = checked(accumulated + count(raw[index]));
                if ((decimal)accumulated / total >= policy.ClassMassCoverage) break;
            }
            // A rare but sufficiently observed class state must not lose its
            // empirical count merely because it lies outside the mass core.
            var joint = core.Concat(Enumerable.Range(0, 81).Where(i => preserveSupportedTail && count(raw[i]) >= minimum)).ToHashSet();
            var denominator = total + 81 * alpha;
            // Marginals of the SAME Dirichlet-smoothed TRAIN joint table.
            var person = raw.GroupBy(c => c.Person).ToDictionary(g => g.Key, g => (g.Sum(count) + 9 * alpha) / denominator);
            var mother = raw.GroupBy(c => c.Mother).ToDictionary(g => g.Key, g => (g.Sum(count) + 9 * alpha) / denominator);
            var rare = Enumerable.Range(0, 81).Where(i => !joint.Contains(i)).ToArray();
            var residual = rare.Sum(i => (count(raw[i]) + alpha) / denominator);
            var productMass = rare.Sum(i => person[raw[i].Person] * mother[raw[i].Mother]);
            if (rare.Length > 0 && (residual <= 0 || productMass <= 0))
                throw new InvalidDataException("Alpha não representa massa positiva para o fallback na precisão decimal disponível.");
            var scale = rare.Length == 0 ? 0m : residual / productMass;
            var p = raw.Select((c, i) => joint.Contains(i) ? (count(c) + alpha) / denominator
                : scale * person[c.Person] * mother[c.Mother]).ToArray();
            if (p.Any(x => x is <= 0 or > 1))
                throw new InvalidDataException("Estimação perdeu probabilidade positiva na precisão decimal disponível.");
            return (p, core, joint, (decimal)accumulated / total, scale);
        }
    }

    public static bool HasRequiredCoreSupport(V9JointSupportProvenance p) =>
        p.Cells.All(c => (!c.CoreM || c.ObservedM >= p.Policy.MinimumM) && (!c.CoreU || c.ObservedU >= p.Policy.MinimumU));

    public static V9JointSupportProvenance VerifyAndFreeze(V9JointSupportProvenance p,
        IReadOnlyList<FellegiSunterJointNominalCellV9> probabilities, string trainSha)
    {
        if (p.Cells is null || p.Policy is null || p.TrainCorpusSha256 != trainSha)
            throw new InvalidDataException("Proveniência de suporte diverge do TRAIN congelado.");
        var recomputed = Estimate(p.Cells.Select(c => new V9JointObservedCell(c.Person, c.Mother, c.ObservedM, c.ObservedU)),
            p.Policy, p.SmoothingAlpha, trainSha);
        // Check every decision/scale/count as well as every probability: metadata is not a free-form claim.
        if (JsonSerializer.Serialize(p) != JsonSerializer.Serialize(recomputed.Provenance)
            || probabilities.Count != 81 || probabilities.Zip(recomputed.Probabilities).Any(pair =>
                pair.First.Person != pair.Second.Person || pair.First.Mother != pair.Second.Mother
                || pair.First.M != pair.Second.M || pair.First.U != pair.Second.U))
            throw new InvalidDataException("Snapshot não reproduz a estimação conjunta/marginal declarada.");
        return recomputed.Provenance;
    }
}

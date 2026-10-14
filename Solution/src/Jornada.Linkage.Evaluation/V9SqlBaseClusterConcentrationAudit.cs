using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Measures concentration of SQL TRAIN pair contributions on synthetic BASE
/// identities. Kish-equivalent contributors from base incidence counts are
/// descriptive concentration indices, NOT independent effective sample sizes,
/// design effects, confidence intervals or permission to estimate FS weights.
/// </summary>
public static class V9SqlBaseClusterConcentrationAudit
{
    public const string Version = "V9_SQL_TRAIN_BASE_CONTRIBUTION_CONCENTRATION_V1";
    public const string Status =
        "INCONCLUSIVE_KISH_CONCENTRATION_NOT_INDEPENDENT_ESS_NO_PARAMETER_PROMOTION";

    public sealed record Cell(
        string Domain, CompositeNominalLevelV9? PersonLevel,
        CompositeNominalLevelV9? MotherLevel, string? BirthState,
        string ClassLabel, long ObservationPairs,
        int DistinctBaseContributors, long BaseContributionIncidences,
        long SquaredBaseContributionIncidences,
        long MaxBaseContributionIncidences,
        decimal? LargestBaseContributionShare,
        decimal? KishEquivalentBaseContributorsNotIndependentEss);

    public sealed record Report(
        string Version, string Status, string Partition, int UniqueTrainBasePeople,
        long SqlCandidateM, long SqlCandidateU,
        bool EstimatesIndependentEss, bool MayPromoteModelParameters,
        IReadOnlyList<Cell> Cells);

    private sealed class Counter
    {
        public long ObservationPairs;
        public readonly Dictionary<string, long> BaseWeights =
            new(StringComparer.Ordinal);

        public void Add(string a, string b, bool match)
        {
            checked { ObservationPairs++; }
            static void Increment(Dictionary<string, long> map, string key)
            {
                map.TryGetValue(key, out var count);
                checked { map[key] = count + 1; }
            }
            Increment(BaseWeights, a);
            if (!match)
                Increment(BaseWeights, b);
        }

        public Cell Build(
            string domain, CompositeNominalLevelV9? person,
            CompositeNominalLevelV9? mother, string? birth, string classLabel)
        {
            var incidence = BaseWeights.Values.Sum();
            var squared = BaseWeights.Values.Sum(x => checked(x * x));
            var largest = BaseWeights.Count == 0 ? 0 : BaseWeights.Values.Max();
            var multiplier = classLabel == "M" ? 1 : 2;
            if (incidence != checked(ObservationPairs * multiplier)
                || (ObservationPairs == 0) != (BaseWeights.Count == 0)
                || largest > incidence)
                throw new InvalidDataException("Contribuições por base não conciliam M/U.");
            return new Cell(domain, person, mother, birth, classLabel,
                ObservationPairs, BaseWeights.Count, incidence, squared, largest,
                incidence == 0 ? null : decimal.Divide(largest, incidence),
                squared == 0 ? null :
                    decimal.Divide(checked((decimal)incidence * incidence), squared));
        }
    }

    public static Report Analyze(
        IReadOnlyList<V9SqlConditionedNominalSupportAudit.Candidate> nominalPairs,
        IReadOnlyList<V9SqlConditionedBirthSupportAudit.Candidate> birthPairs,
        V9SqlConditionedNominalSupportAudit.Report nominal,
        V9SqlConditionedBirthSupportAudit.Report birth,
        int uniqueTrainBasePeople)
    {
        ArgumentNullException.ThrowIfNull(nominalPairs);
        ArgumentNullException.ThrowIfNull(birthPairs);
        ArgumentNullException.ThrowIfNull(nominal);
        ArgumentNullException.ThrowIfNull(birth);
        if (uniqueTrainBasePeople <= 0 || nominalPairs.Count == 0
            || nominalPairs.Count != birthPairs.Count
            || nominal.Partition != "TRAIN" || birth.Partition != "TRAIN"
            || nominal.Version != V9SqlConditionedNominalSupportAudit.Version
            || birth.Version != V9SqlConditionedBirthSupportAudit.Version
            || nominal.SqlCandidatePairs != nominalPairs.Count
            || birth.SqlCandidatePairs != nominalPairs.Count
            || nominal.SqlCandidateM != birth.SqlCandidateM
            || nominal.SqlCandidateU != birth.SqlCandidateU
            || nominal.Cells.Count != 81 || birth.SemanticCells.Count != 7)
            throw new InvalidDataException("Não há frame SQL TRAIN único para concentração por base.");

        var nominalGroups = (
            from p in Enum.GetValues<CompositeNominalLevelV9>()
            from m in Enum.GetValues<CompositeNominalLevelV9>()
            from match in new[] { true, false }
            select (p, m, match)).ToDictionary(x => x, _ => new Counter());
        var birthGroups = (
            from state in BirthDateSemanticEvidence.States
            from match in new[] { true, false }
            select (state, match)).ToDictionary(x => x, _ => new Counter());

        var seen = new HashSet<(int A, int B)>();
        for (var index = 0; index < nominalPairs.Count; index++)
        {
            var n = nominalPairs[index];
            var b = birthPairs[index];
            ArgumentNullException.ThrowIfNull(n);
            ArgumentNullException.ThrowIfNull(b);
            if (n.Partition != "TRAIN" || b.Partition != "TRAIN"
                || n.A < 0 || n.B <= n.A || n.A != b.A || n.B != b.B
                || n.BaseA != b.BaseA || n.BaseB != b.BaseB
                || string.IsNullOrWhiteSpace(n.BaseA)
                || string.IsNullOrWhiteSpace(n.BaseB)
                || !seen.Add((n.A, n.B)))
                throw new InvalidDataException("Candidatos nominais e nascimento divergem do mesmo cursor SQL TRAIN.");
            var match = n.BaseA == n.BaseB;
            var p = CompositeNominalComparisonV9.Classify(n.NameA, n.NameB);
            var m = CompositeNominalComparisonV9.Classify(n.MotherA, n.MotherB);
            var birthState = BirthDateSemanticEvidence.Classify(b.BirthA, b.BirthB);
            nominalGroups[(p, m, match)].Add(n.BaseA, n.BaseB, match);
            birthGroups[(birthState, match)].Add(n.BaseA, n.BaseB, match);
        }

        var cells = new List<Cell>(176);
        foreach (var n in nominal.Cells)
        foreach (var match in new[] { true, false })
        {
            var c = nominalGroups[(n.PersonLevel, n.MotherLevel, match)]
                .Build("NOMINAL_JOINT", n.PersonLevel, n.MotherLevel,
                    null, match ? "M" : "U");
            var support = match ? n.MCount : n.UCount;
            var contributors = match ? n.DistinctMBasePeople : n.DistinctUBasePeople;
            if (c.ObservationPairs != support || c.DistinctBaseContributors != contributors)
                throw new InvalidDataException("Concentração nominal diverge das 81 células SQL.");
            cells.Add(c);
        }
        foreach (var b in birth.SemanticCells)
        foreach (var match in new[] { true, false })
        {
            var c = birthGroups[(b.SemanticBirthState, match)]
                .Build("BIRTH_SEMANTIC", null, null,
                    b.SemanticBirthState, match ? "M" : "U");
            var support = match ? b.MCount : b.UCount;
            var contributors = match ? b.DistinctMBasePeople : b.DistinctUBasePeople;
            if (c.ObservationPairs != support || c.DistinctBaseContributors != contributors)
                throw new InvalidDataException("Concentração DOB diverge dos sete estados SQL.");
            cells.Add(c);
        }
        if (cells.Count != 176
            || cells.Any(x => x.DistinctBaseContributors > uniqueTrainBasePeople
                || (x.KishEquivalentBaseContributorsNotIndependentEss is { } ne
                    && (ne < 1m || ne > x.DistinctBaseContributors + .000000000000000001m)))
            || cells.Where(x => x.Domain == "NOMINAL_JOINT" && x.ClassLabel == "M")
                .Sum(x => x.ObservationPairs) != nominal.SqlCandidateM
            || cells.Where(x => x.Domain == "NOMINAL_JOINT" && x.ClassLabel == "U")
                .Sum(x => x.ObservationPairs) != nominal.SqlCandidateU
            || cells.Where(x => x.Domain == "BIRTH_SEMANTIC" && x.ClassLabel == "M")
                .Sum(x => x.ObservationPairs) != birth.SqlCandidateM
            || cells.Where(x => x.Domain == "BIRTH_SEMANTIC" && x.ClassLabel == "U")
                .Sum(x => x.ObservationPairs) != birth.SqlCandidateU)
            throw new InvalidDataException("Concentração de base não reconciliou com SQL TRAIN.");

        return new Report(Version, Status, "TRAIN", uniqueTrainBasePeople,
            nominal.SqlCandidateM, nominal.SqlCandidateU,
            false, false, Array.AsReadOnly(cells.ToArray()));
    }
}

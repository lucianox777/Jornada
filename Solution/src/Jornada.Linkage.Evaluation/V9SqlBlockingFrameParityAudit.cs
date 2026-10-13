using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Compare the ACTIVE SQL ruleset, its C1 companion and the physical SQL union
/// on the EXACT SAME independently labelled M pairs and projection vocabulary.
/// Only aggregate coverage is emitted. No model promotion or generator inference.
/// </summary>
public static class V9SqlBlockingFrameParityAudit
{
    public const string Version = "V9_ACTIVE_RULESET_SAME_FRAME_M_SQL_PARITY_V1";
    public const string Status = "ACTIVE_SQL_VS_LOGICAL_ON_ELIGIBLE_SYNTHETIC_M_NOT_CALIBRATOR_GATE";

    public sealed record Pass(string PassId, IReadOnlyList<string> Fields);
    public sealed record Pair(
        int A, int B, string Partition,
        IReadOnlySet<BlockingProjectionKey> LeftKeys,
        IReadOnlySet<BlockingProjectionKey> RightKeys);
    public sealed record Partition(
        string Name, long EligibleM, long C1M, long ActiveLogicalM,
        long BothC1AndActiveM, long C1OnlyM, long ActiveOnlyM,
        long LogicalUnionM, long PhysicalSqlUnionM, long NotRecoveredM);
    public sealed record PassCoverage(
        string Partition, string PassId, long RetainedM, long ExclusiveOutsideC1M);
    public sealed record Report(
        string Version, string Status,
        IReadOnlyList<Partition> Partitions,
        IReadOnlyList<PassCoverage> Passes);

    public static Report Analyze(
        IReadOnlyCollection<Pair> eligiblePairs,
        IReadOnlyCollection<Pass> activePasses,
        IReadOnlySet<(int A, int B)> c1TruePairs,
        IReadOnlySet<(int A, int B)> sqlTruePairs)
    {
        ArgumentNullException.ThrowIfNull(eligiblePairs);
        ArgumentNullException.ThrowIfNull(activePasses);
        ArgumentNullException.ThrowIfNull(c1TruePairs);
        ArgumentNullException.ThrowIfNull(sqlTruePairs);
        var passes = activePasses.OrderBy(x => x.PassId, StringComparer.Ordinal).ToArray();
        if (passes.Length == 0
            || passes.Any(x => x is null || string.IsNullOrWhiteSpace(x.PassId)
                || x.Fields is null || x.Fields.Count == 0
                || x.Fields.Any(string.IsNullOrWhiteSpace)
                || x.Fields.Distinct(StringComparer.Ordinal).Count() != x.Fields.Count)
            || passes.Select(x => x.PassId).Distinct(StringComparer.Ordinal).Count() != passes.Length)
            throw new InvalidDataException("Ruleset SQL não possui passes canônicos únicos.");

        var seen = new HashSet<(int A, int B)>();
        var perPartition = new Dictionary<string, long[]>(StringComparer.Ordinal);
        var perPass = new Dictionary<(string Part, string Pass), (long Count, long Exclusive)>();
        foreach (var part in new[] { "TRAIN", "VALIDATION", "TEST" })
        {
            perPartition[part] = new long[9];
            foreach (var pass in passes)
                perPass[(part, pass.PassId)] = (0, 0);
        }

        foreach (var pair in eligiblePairs)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (!perPartition.TryGetValue(pair.Partition, out var sums)
                || pair.A < 0 || pair.B <= pair.A
                || pair.LeftKeys is null || pair.RightKeys is null
                || !seen.Add((pair.A, pair.B)))
                throw new InvalidDataException("M elegível duplicado ou fora do frame SQL.");
            var c1 = c1TruePairs.Contains((pair.A, pair.B));
            var sql = sqlTruePairs.Contains((pair.A, pair.B));
            var matched = passes.Where(pass => pass.Fields.All(field =>
                pair.LeftKeys.Any(key => key.Feature == field && pair.RightKeys.Contains(key))))
                .Select(pass => pass.PassId).ToArray();
            var active = matched.Length > 0;
            var union = c1 || active;
            if (union != sql)
                throw new InvalidDataException(
                    "Paridade SQL != OR lógico (ACTIVE+C1) no mesmo par M elegível.");

            // eligible, C1, active, both, C1-only, active-only, logical union,
            // physical SQL union and missed.
            sums[0]++;
            if (c1) sums[1]++;
            if (active) sums[2]++;
            if (c1 && active) sums[3]++;
            if (c1 && !active) sums[4]++;
            if (!c1 && active) sums[5]++;
            if (union) sums[6]++;
            if (sql) sums[7]++;
            if (!union) sums[8]++;
            foreach (var passId in matched)
            {
                var previous = perPass[(pair.Partition, passId)];
                perPass[(pair.Partition, passId)] = (
                    previous.Count + 1,
                    previous.Exclusive + (!c1 && matched.Length == 1 ? 1 : 0));
            }
        }
        if (seen.Count != eligiblePairs.Count
            || c1TruePairs.Any(x => !seen.Contains(x))
            || sqlTruePairs.Any(x => !seen.Contains(x)))
            throw new InvalidDataException(
                "SQL C1/união contém M fora do denominador independente.");

        var partitions = perPartition.Select(x => new Partition(
            x.Key, x.Value[0], x.Value[1], x.Value[2],
            x.Value[3], x.Value[4], x.Value[5],
            x.Value[6], x.Value[7], x.Value[8]))
            .OrderBy(x => x.Name switch { "TRAIN" => 0, "VALIDATION" => 1, _ => 2 })
            .ToArray();
        if (partitions.Any(x =>
            x.EligibleM != x.LogicalUnionM + x.NotRecoveredM
            || x.LogicalUnionM != x.PhysicalSqlUnionM
            || x.LogicalUnionM != x.C1OnlyM + x.ActiveOnlyM + x.BothC1AndActiveM
            || x.C1M != x.C1OnlyM + x.BothC1AndActiveM
            || x.ActiveLogicalM != x.ActiveOnlyM + x.BothC1AndActiveM))
            throw new InvalidDataException("Paridade do blocking não concilia por split.");

        var coverages = perPass.Select(x =>
                new PassCoverage(x.Key.Part, x.Key.Pass, x.Value.Count, x.Value.Exclusive))
            .OrderBy(x => x.Partition switch { "TRAIN" => 0, "VALIDATION" => 1, _ => 2 })
            .ThenBy(x => x.PassId, StringComparer.Ordinal)
            .ToArray();
        return new Report(Version, Status, partitions, coverages);
    }
}

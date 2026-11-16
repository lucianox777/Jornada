using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

/// <summary>
/// Test-only exhaustive reference oracle: enumerates tiny complete-link
/// partitions independently of BuildTest's union-find/root bookkeeping.
/// Does not reuse ReplayTest, production grouping, Gold labels or SQL state.
/// This certifies only grouping semantics, NOT TF4 ranking or TEST admission.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9IndependentCompleteLinkOracleTests
{
    private static readonly (int A, int B)[] Pairs =
        [(1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4)];

    private static int[] ReferencePartition(
        IReadOnlyList<V9BlindFsSqlEdge> edges, int cap)
    {
        var groups = new List<HashSet<int>>
        {
            new() { 1 }, new() { 2 }, new() { 3 }, new() { 4 }
        };
        var strong = edges.Where(e => e.AToBLogOdds >= 9m
                && e.BToALogOdds >= 9m)
            .Select(e => (e.A, e.B)).ToHashSet();
        foreach (var edge in edges
            .OrderByDescending(e => Math.Min(e.AToBLogOdds, e.BToALogOdds))
            .ThenBy(e => e.A).ThenBy(e => e.B))
        {
            if (!strong.Contains((edge.A, edge.B))) continue;
            var left = groups.Single(g => g.Contains(edge.A));
            var right = groups.Single(g => g.Contains(edge.B));
            if (ReferenceEquals(left, right) || left.Count + right.Count > cap)
                continue;
            // Deliberately recompute every cross-product from the full
            // reference adjacency rather than using production roots.
            if (!left.All(a => right.All(b =>
                    strong.Contains((Math.Min(a, b), Math.Max(a, b))))))
                continue;
            left.UnionWith(right);
            groups.Remove(right);
        }
        return Enumerable.Range(1, 4)
            .Select(id => groups.Single(g => g.Contains(id)).Min()).ToArray();
    }

    [Test]
    public void Exhaustive_four_observation_TEST_graph_matches_independent_reference()
    {
        var rows = Enumerable.Range(1, 4).Select(id =>
            new V9PhysicalCalibrationObservation(
                id, "UNUSED_TRUTH_" + id, "TEST", "MANAGER_" + id,
                "ANA MARIA SILVA", "MARIA LIMA", new DateOnly(1980, 5, 6)))
            .ToArray();

        // 3^6 graphs: missing, bidirectionally strong, or one-sided weak.
        // Test both caps and every adjacency combination, including chains.
        for (var graph = 0; graph < 729; graph++)
        {
            var code = graph;
            var edges = new List<V9BlindFsSqlEdge>();
            foreach (var (a, b) in Pairs)
            {
                var state = code % 3;
                code /= 3;
                if (state == 1) edges.Add(new(a, b, 9m, 9m));
                if (state == 2) edges.Add(new(a, b, 9m, 0m));
            }
            foreach (var cap in new[] { 2, 3, 4 })
            {
                var expected = ReferencePartition(edges, cap);
                var actual = V9BlindIncrementalIdentityGrouping.BuildTest(
                    rows, edges, minimumPairPosterior: .99m,
                    maximumObservationsPerIdentity: cap);
                for (var id = 1; id <= 4; id++)
                {
                    var referenceGuid = new Guid(expected[id - 1],
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 4);
                    Assert.That(actual.IdentityByObservation[id],
                        Is.EqualTo(referenceGuid),
                        $"graph={graph}, cap={cap}, observation={id}");
                }
                Assert.That(actual.OperationallyCertified, Is.False);
            }
        }
    }
}

namespace Jornada.Linkage.Parameters.Worker;

public enum PairwiseLinkageDecisionState
{
    Accepted,
    Rejected,
    Inconclusive
}

public sealed record PairwiseLinkageDecision(
    Guid LeftId,
    Guid RightId,
    PairwiseLinkageDecisionState Decision,
    decimal? Score = null);

public sealed record PairwiseTransitivityComponent(
    Guid ComponentId,
    IReadOnlyList<Guid> Members,
    int AcceptedEdges);

public sealed record PairwiseTransitivityGap(
    Guid LeftId,
    Guid BridgeId,
    Guid RightId,
    PairwiseLinkageDecisionState? ClosureDecision,
    decimal? ClosureScore);

public sealed record PairwiseTransitivityReport(
    string Version,
    int NodeCount,
    int AcceptedEdges,
    int RejectedEdges,
    int InconclusiveEdges,
    IReadOnlyList<PairwiseTransitivityComponent> Components,
    IReadOnlyList<PairwiseTransitivityGap> OpenWedges)
{
    public int MultiMemberComponents => Components.Count(component => component.Members.Count > 1);
    public int MissingClosures => OpenWedges.Count(gap => gap.ClosureDecision is null);
    public int RejectedClosures => OpenWedges.Count(gap => gap.ClosureDecision == PairwiseLinkageDecisionState.Rejected);
    public int InconclusiveClosures => OpenWedges.Count(gap => gap.ClosureDecision == PairwiseLinkageDecisionState.Inconclusive);
}

/// <summary>
/// Diagnóstico somente-leitura para P24. Mede onde decisões par-a-par aceitas formam
/// cadeias A-B/B-C cuja aresta de fechamento A-C não foi aceita. Não cria clusters,
/// não aplica fechamento transitivo e não produz decisão de composição.
/// </summary>
public static class PairwiseTransitivityDiagnostic
{
    public const string Version = "PAIRWISE_TRANSITIVITY_DIAGNOSTIC_V1";

    public static PairwiseTransitivityReport Analyze(IReadOnlyCollection<PairwiseLinkageDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        var edges = new Dictionary<(Guid Left, Guid Right), PairwiseLinkageDecision>();
        foreach (var decision in decisions)
        {
            ArgumentNullException.ThrowIfNull(decision);
            if (decision.LeftId == Guid.Empty || decision.RightId == Guid.Empty || decision.LeftId == decision.RightId ||
                !Enum.IsDefined(decision.Decision) || decision.Score is < 0m or > 1m)
                throw new ArgumentException("Decisão par-a-par inválida.", nameof(decisions));

            var key = CanonicalPair(decision.LeftId, decision.RightId);
            if (!edges.TryAdd(key, decision with { LeftId = key.Left, RightId = key.Right }))
                throw new InvalidOperationException("Par duplicado no diagnóstico de transitividade.");
        }

        var nodes = edges.Keys.SelectMany(static pair => new[] { pair.Left, pair.Right }).Distinct().Order().ToArray();
        var acceptedAdjacency = nodes.ToDictionary(static id => id, static _ => new HashSet<Guid>());
        foreach (var edge in edges.Values.Where(static edge => edge.Decision == PairwiseLinkageDecisionState.Accepted))
        {
            acceptedAdjacency[edge.LeftId].Add(edge.RightId);
            acceptedAdjacency[edge.RightId].Add(edge.LeftId);
        }

        var components = BuildComponents(nodes, acceptedAdjacency);
        var gaps = new List<PairwiseTransitivityGap>();

        foreach (var bridge in nodes)
        {
            var neighbours = acceptedAdjacency[bridge].Order().ToArray();
            for (var i = 0; i < neighbours.Length; i++)
            for (var j = i + 1; j < neighbours.Length; j++)
            {
                var left = neighbours[i];
                var right = neighbours[j];
                var closureKey = CanonicalPair(left, right);
                if (edges.TryGetValue(closureKey, out var closure) &&
                    closure.Decision == PairwiseLinkageDecisionState.Accepted)
                    continue;

                gaps.Add(new PairwiseTransitivityGap(
                    left,
                    bridge,
                    right,
                    closure?.Decision,
                    closure?.Score));
            }
        }

        return new PairwiseTransitivityReport(
            Version,
            nodes.Length,
            edges.Values.Count(static edge => edge.Decision == PairwiseLinkageDecisionState.Accepted),
            edges.Values.Count(static edge => edge.Decision == PairwiseLinkageDecisionState.Rejected),
            edges.Values.Count(static edge => edge.Decision == PairwiseLinkageDecisionState.Inconclusive),
            components,
            gaps.AsReadOnly());
    }

    private static IReadOnlyList<PairwiseTransitivityComponent> BuildComponents(
        IReadOnlyList<Guid> nodes,
        IReadOnlyDictionary<Guid, HashSet<Guid>> adjacency)
    {
        var visited = new HashSet<Guid>();
        var components = new List<PairwiseTransitivityComponent>();

        foreach (var start in nodes)
        {
            if (!visited.Add(start))
                continue;

            var stack = new Stack<Guid>();
            stack.Push(start);
            var members = new List<Guid>();
            var acceptedDegreeSum = 0;

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                members.Add(current);
                acceptedDegreeSum += adjacency[current].Count;
                foreach (var neighbour in adjacency[current].OrderByDescending(static id => id))
                    if (visited.Add(neighbour))
                        stack.Push(neighbour);
            }

            members.Sort();
            components.Add(new PairwiseTransitivityComponent(
                members[0],
                members.AsReadOnly(),
                acceptedDegreeSum / 2));
        }

        return components.AsReadOnly();
    }

    private static (Guid Left, Guid Right) CanonicalPair(Guid left, Guid right) =>
        left.CompareTo(right) < 0 ? (left, right) : (right, left);
}

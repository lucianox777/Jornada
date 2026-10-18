using System.Collections.ObjectModel;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// A scored SQL edge in canonical observation-id order. Scores MUST come from
/// the same frozen TRAIN FS fit, evaluated in both directions on VALIDATION.
/// This type does not establish SQL provenance or statistical calibration.
/// </summary>
public sealed record V9BlindFsSqlEdge(
    int A, int B, decimal AToBLogOdds, decimal BToALogOdds);

public sealed record V9BlindIncrementalGroupingResult(
    IReadOnlyDictionary<int, Guid> IdentityByObservation,
    int AcceptedTransitions,
    int RejectedWeakOrIncompleteLinks,
    int RejectedConsistencyGuards,
    int RejectedSizeCap,
    bool OperationallyCertified);

/// <summary>
/// Experimental, label-blind, complete-link candidate-identity formation.
/// Uses ONLY observed fields, manager, physical SQL adjacency and frozen FS
/// scores. BasePersonId is deliberately never consulted for membership.
/// No CPF, TEST, Gold publication, model activation or oracle shortcuts.
/// </summary>
public static class V9BlindIncrementalIdentityGrouping
{
    private sealed record GuardKey(
        string? First, string? Last, string? Mother, DateOnly? Birth);

    public static V9BlindIncrementalGroupingResult Build(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyCollection<V9BlindFsSqlEdge> scoredSqlEdges,
        decimal minimumPairPosterior = .99m,
        int maximumObservationsPerIdentity = 16)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(scoredSqlEdges);
        if (minimumPairPosterior is <= .5m or >= 1m
            || maximumObservationsPerIdentity < 2)
            throw new ArgumentOutOfRangeException(nameof(minimumPairPosterior),
                "Exige posterior FS em (0.5,1) e cap de identidade >= 2.");

        var rows = observations.ToArray();
        if (rows.Length < 2 || rows.Any(x => x is null
            || x.Partition != "VALIDATION" || x.ObservationId <= 0
            || string.IsNullOrWhiteSpace(x.Gestor))
            || rows.Select(x => x.ObservationId).Distinct().Count() != rows.Length)
            throw new InvalidDataException(
                "Agrupamento exige observações únicas de VALIDATION, sem TEST.");
        var byId = rows.ToDictionary(x => x.ObservationId);
        var keys = rows.ToDictionary(x => x.ObservationId, x =>
        {
            var normalized = IdentityComparison.NormalizeText(x.PersonName);
            var tokens = normalized?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new GuardKey(
                tokens is { Length: >= 2 } ? tokens[0] : null,
                tokens is { Length: >= 2 } ? tokens[^1] : null,
                IdentityComparison.NormalizeText(x.MotherName),
                x.BirthDate);
        });

        // SQL must supply canonical, unique, cross-manager edges only.
        // Never silently repair malformed adjacency or manufacture missing links.
        var edges = new Dictionary<(int A, int B), V9BlindFsSqlEdge>();
        foreach (var edge in scoredSqlEdges)
        {
            if (edge is null || edge.A >= edge.B
                || !byId.TryGetValue(edge.A, out var left)
                || !byId.TryGetValue(edge.B, out var right)
                || string.Equals(left.Gestor, right.Gestor, StringComparison.Ordinal)
                || !edges.TryAdd((edge.A, edge.B), edge))
                throw new InvalidDataException(
                    "Aresta SQL duplicada, não canônica, desconhecida ou intra-gestor.");
        }

        var minimumLogOdds = (decimal)Math.Log(
            (double)minimumPairPosterior / (1d - (double)minimumPairPosterior));
        bool Compatible(int a, int b)
        {
            var x = keys[a];
            var y = keys[b];
            return !string.Equals(byId[a].Gestor, byId[b].Gestor,
                       StringComparison.Ordinal)
                && x.First is not null && y.First is not null
                && x.Last is not null && y.Last is not null
                && !string.IsNullOrWhiteSpace(x.Mother)
                && !string.IsNullOrWhiteSpace(y.Mother)
                && x.Birth.HasValue && y.Birth.HasValue
                && string.Equals(x.First, y.First, StringComparison.Ordinal)
                && string.Equals(x.Last, y.Last, StringComparison.Ordinal)
                && string.Equals(x.Mother, y.Mother, StringComparison.Ordinal)
                && x.Birth == y.Birth;
        }

        bool StrongEdge(int a, int b)
        {
            var pair = a < b ? (a, b) : (b, a);
            return edges.TryGetValue(pair, out var edge)
                && edge.AToBLogOdds >= minimumLogOdds
                && edge.BToALogOdds >= minimumLogOdds;
        }

        var roots = byId.Keys.ToDictionary(id => id, id => id);
        var members = byId.Keys.ToDictionary(
            id => id, id => new HashSet<int> { id });
        int accepted = 0, weakOrIncomplete = 0, guardRejected = 0, capRejected = 0;
        foreach (var edge in edges.Values
            .OrderByDescending(x => Math.Min(x.AToBLogOdds, x.BToALogOdds))
            .ThenBy(x => x.A).ThenBy(x => x.B))
        {
            if (!Compatible(edge.A, edge.B))
            {
                guardRejected++;
                continue;
            }
            if (!StrongEdge(edge.A, edge.B))
            {
                weakOrIncomplete++;
                continue;
            }
            var rootA = roots[edge.A];
            var rootB = roots[edge.B];
            if (rootA == rootB)
                continue;
            if (members[rootA].Count + members[rootB].Count
                > maximumObservationsPerIdentity)
            {
                capRejected++;
                continue;
            }

            // Complete linkage prevents a high-scoring A-B-C chain from
            // joining A and C without an observed, strong, guarded SQL edge.
            if (members[rootA].Any(a => members[rootB].Any(b =>
                    !Compatible(a, b) || !StrongEdge(a, b))))
            {
                weakOrIncomplete++;
                continue;
            }

            var keep = Math.Min(rootA, rootB);
            var remove = Math.Max(rootA, rootB);
            foreach (var id in members[remove])
            {
                roots[id] = keep;
                members[keep].Add(id);
            }
            members.Remove(remove);
            accepted++;
        }

        // Synthetic, deterministic diagnostic IDs. Never persist as Gold UUIDs.
        var mapped = roots.ToDictionary(
            x => x.Key,
            x => new Guid(x.Value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4));
        return new V9BlindIncrementalGroupingResult(
            new ReadOnlyDictionary<int, Guid>(mapped), accepted,
            weakOrIncomplete, guardRejected, capRejected,
            OperationallyCertified: false);
    }
}

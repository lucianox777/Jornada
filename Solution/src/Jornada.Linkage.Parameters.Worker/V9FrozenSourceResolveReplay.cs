using System.Collections.ObjectModel;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9FrozenSourceDecision(int ObservationId, ProbabilisticLinkageDecision Decision);
public sealed record V9FrozenSourceReplayResult(string SnapshotSha256,
    IReadOnlyList<V9FrozenSourceDecision> Decisions, IReadOnlyDictionary<int, Guid> IdentityByObservation);

/// <summary>
/// Executes the portable fit and its embedded grouping on a complete physical VALIDATION
/// graph. No fitting, selection, TEST, truth-based grouping, persistence or activation.
/// Reuses the same C# scorer, complete-link builder and Resolve decision kernel.
/// </summary>
public static class V9FrozenSourceResolveReplay
{
    private static Guid ObservationGuid(int id) => new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    public static V9FrozenSourceReplayResult Replay(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> validation,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(completePhysicalSqlEdges);
        var grouping = snapshot.Payload.CandidateGrouping
            ?? throw new InvalidDataException("Replay de coorte exige agrupamento fixado no snapshot.");
        if (validation.Count < 2 || validation.Any(x => x is null || x.ObservationId <= 0 || x.Partition != "VALIDATION")
            || validation.Select(x => x.ObservationId).Distinct().Count() != validation.Count)
            throw new InvalidDataException("Replay aceita exclusivamente coorte VALIDATION única, sem TEST.");
        var rows = validation.OrderBy(x => x.ObservationId).ToArray();
        var byId = rows.ToDictionary(x => x.ObservationId);
        var byGuid = rows.ToDictionary(x => ObservationGuid(x.ObservationId));
        var neighbors = rows.ToDictionary(x => x.ObservationId, _ => new HashSet<int>());
        foreach (var (a, b) in completePhysicalSqlEdges)
        {
            if (a >= b || !byId.TryGetValue(a, out var left) || !byId.TryGetValue(b, out var right)
                || left.Gestor == right.Gestor || !neighbors[a].Add(b) || !neighbors[b].Add(a))
                throw new InvalidDataException("Replay recebeu aresta SQL inválida, duplicada ou intra-gestor.");
        }
        var ranked = rows.ToDictionary(x => x.ObservationId, source => snapshot.RankPreview(
            new IdentityObservation(null, "SEM_CPF", source.PersonName, source.BirthDate, source.MotherName),
            neighbors[source.ObservationId].OrderBy(id => id).Select(id => byId[id])
                .Select(x => new V9SnapshotCandidate(ObservationGuid(x.ObservationId), x.PersonName, x.BirthDate, x.MotherName)).ToArray()));
        var scores = ranked.ToDictionary(x => x.Key, x => x.Value.ToDictionary(y => y.PessoaUuid));
        var edges = completePhysicalSqlEdges.Select(edge => new V9BlindFsSqlEdge(edge.A, edge.B,
            scores[edge.A][ObservationGuid(edge.B)].LogOdds, scores[edge.B][ObservationGuid(edge.A)].LogOdds)).ToArray();
        var inferred = V9BlindIncrementalIdentityGrouping.Build(rows, edges,
            grouping.MinimumPairPosterior, grouping.MaximumObservationsPerIdentity);
        var decisions = rows.Select(source =>
        {
            var collapsed = ranked[source.ObservationId]
                .GroupBy(x => inferred.IdentityByObservation[byGuid[x.PessoaUuid].ObservationId])
                .Select(g => g.First() with { PessoaUuid = g.Key })
                .OrderByDescending(x => x.LogOdds).ThenBy(x => x.PessoaUuid).ToArray();
            return new V9FrozenSourceDecision(source.ObservationId,
                snapshot.ResolveRankedPreview(ObservationGuid(source.ObservationId), collapsed));
        }).ToArray();
        return new(snapshot.ContentSha256, Array.AsReadOnly(decisions),
            new ReadOnlyDictionary<int, Guid>(inferred.IdentityByObservation.ToDictionary(x => x.Key, x => x.Value)));
    }
}

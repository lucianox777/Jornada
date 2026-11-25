// Shared V9 frozen, nonpublishing execution contract: compiled once in Jornada.Linkage.Core.
using System.Collections.ObjectModel;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9DevDirectionalScore(int Source, int Target, decimal? Posterior, decimal? LogOdds);

public sealed record V9FrozenSourceDecision(int ObservationId, ProbabilisticLinkageDecision Decision);
public sealed record V9FrozenSourceReplayResult(string SnapshotSha256,
    IReadOnlyList<V9FrozenSourceDecision> Decisions, IReadOnlyDictionary<int, Guid> IdentityByObservation);

/// <summary>
/// Executes an immutable fit and grouping on an independent physical evaluation graph.
/// Replay is VALIDATION-only; ReplayTest is TEST-only. Neither fits, selects,
/// consults truth for grouping, persists a model, or activates it.
/// Reuses the same C# scorer, complete-link builder and Resolve decision kernel.
/// </summary>
public static class V9FrozenSourceResolveReplay
{
    // Single identity mapping shared with operational SQL candidate verification.
    public static Guid ObservationGuid(int id) => new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    public static V9FrozenSourceReplayResult Replay(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> validation,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges)
        => ReplayForPartition(snapshot, validation, completePhysicalSqlEdges, "VALIDATION");

    /// <summary>Independent TEST uses the same immutable scorer/Resolve, never model selection.</summary>
    public static V9FrozenSourceReplayResult ReplayTest(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> test,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges)
        => ReplayForPartition(snapshot, test, completePhysicalSqlEdges, "TEST");

    /// <summary>
    /// Controlled DEV run only. Uses the original pinned FS, complete-link and
    /// Resolve kernel. The caller must supply all operational SQL edges for
    /// the full frozen DEV run; no fixture TEST replay or model refit occurs.
    /// This method alone cannot certify SQL/Gold publication or E2E parity.
    /// </summary>
    public static V9FrozenSourceReplayResult ReplayDev(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> dev,
        IReadOnlyList<(int A, int B)> completeOperationalSqlEdges)
        => ReplayForPartition(snapshot, dev, completeOperationalSqlEdges, "DEV");

    // Diagnostic-only overload: immutable scalar values after ranking, no input or decision mutation.
    public static V9FrozenSourceReplayResult ReplayDev(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> dev,
        IReadOnlyList<(int A, int B)> completeOperationalSqlEdges,
        Action<IReadOnlyList<V9DevDirectionalScore>> observe)
        => ReplayForPartition(snapshot, dev, completeOperationalSqlEdges, "DEV", observe);

    private static V9FrozenSourceReplayResult ReplayForPartition(V9FittedSnapshot snapshot,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> validation,
        IReadOnlyList<(int A, int B)> completePhysicalSqlEdges, string partition,
        Action<IReadOnlyList<V9DevDirectionalScore>>? observe = null)
    {
        if (partition is not ("VALIDATION" or "TEST" or "DEV"))
            throw new InvalidDataException("Apenas coortes de avaliação externas são admitidas.");
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(completePhysicalSqlEdges);
        var grouping = snapshot.Payload.CandidateGrouping
            ?? throw new InvalidDataException("Replay de coorte exige agrupamento fixado no snapshot.");
        if (grouping.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES")
            throw new InvalidDataException("Replay BLIND não mede Gold; variante Gold exige entidades do Runner real.");
        // DEV may legitimately have 0/1 pending rows; scientific replay
        // retains the original >=2-observation admission contract.
        if ((partition != "DEV" && validation.Count < 2)
            || validation.Any(x => x is null || x.ObservationId <= 0 || x.Partition != partition)
            || validation.Select(x => x.ObservationId).Distinct().Count() != validation.Count)
            throw new InvalidDataException("Replay exige coorte única correspondente à finalidade e IDs distintos.");
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
        var edges = completePhysicalSqlEdges.Select(edge =>
        {
            // Retain the COMPLETE SQL candidate graph for blocking accounting,
            // but pass to blind grouping only FS-scored edges. Both directions
            // must abstain together; never fill a missing rank with TF0.
            scores[edge.A].TryGetValue(ObservationGuid(edge.B), out var forward);
            scores[edge.B].TryGetValue(ObservationGuid(edge.A), out var backward);
            if (forward is null || backward is null)
            {
                if (snapshot.Payload.UnpublishedTermPolicy != V9TfUnpublishedTermPolicyV9.AbstainCandidate
                    || (forward is null) != (backward is null))
                    throw new InvalidDataException(
                        "Replay TF não reconciliou as duas direções da aresta SQL.");
                return null;
            }
            return new V9BlindFsSqlEdge(edge.A, edge.B, forward.LogOdds, backward.LogOdds);
        }).Where(edge => edge is not null).Cast<V9BlindFsSqlEdge>().ToArray();
        var inferred = partition switch
        {
            "TEST" => V9BlindIncrementalIdentityGrouping.BuildTest(rows, edges,
                grouping.MinimumPairPosterior, grouping.MaximumObservationsPerIdentity),
            "DEV" => V9BlindIncrementalIdentityGrouping.BuildDev(rows, edges,
                grouping.MinimumPairPosterior, grouping.MaximumObservationsPerIdentity),
            _ => V9BlindIncrementalIdentityGrouping.Build(rows, edges,
                grouping.MinimumPairPosterior, grouping.MaximumObservationsPerIdentity)
        };
        var decisions = rows.Select(source =>
        {
            var collapsed = ranked[source.ObservationId]
                .GroupBy(x => inferred.IdentityByObservation[byGuid[x.PessoaUuid].ObservationId])
                .Select(g => g.First() with { PessoaUuid = g.Key })
                .OrderByDescending(x => x.LogOdds).ThenBy(x => x.PessoaUuid).ToArray();
            return new V9FrozenSourceDecision(source.ObservationId,
                snapshot.ResolveRankedPreview(ObservationGuid(source.ObservationId), collapsed));
        }).ToArray();
        if (observe is not null)
        {
            var diagnostic = new List<V9DevDirectionalScore>();
            foreach (var source in neighbors.OrderBy(x => x.Key))
            foreach (var target in source.Value.OrderBy(x => x))
            {
                scores[source.Key].TryGetValue(ObservationGuid(target), out var score);
                diagnostic.Add(new(source.Key, target, score?.Score, score?.LogOdds));
            }
            observe(diagnostic.AsReadOnly());
        }
        return new(snapshot.ContentSha256, Array.AsReadOnly(decisions),
            new ReadOnlyDictionary<int, Guid>(inferred.IdentityByObservation.ToDictionary(x => x.Key, x => x.Value)));
    }
}

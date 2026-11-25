using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Independent complete-link and r1 oracle for the clean first-FULL fixture.
/// It does not call the production grouping or translation in the expected
/// path. The separate actual replay uses the immutable kernel only after the
/// expected partition exists. No labels, model fitting or publication.
/// </summary>
public static class V9DevE2EGroupOracle
{
    public sealed record Result(IReadOnlyList<V9DevE2EScalarOracle.Rank> Decisions,
        IReadOnlyList<int> RootByIndex, int GroupCount, string PartitionSha256);
    private sealed record Guard(string? First, string? Last, string? Mother, DateOnly? Birth);
    private sealed record Edge(int A, int B, decimal Strength);

    public static Result Compute(V9FittedSnapshotPayload winner,
        IReadOnlyList<V9DevE2EFirstFullMembershipOracle.Source> rows,
        IReadOnlyList<(int A,int B)> candidates, IReadOnlyList<V9DevE2EScalarOracle.Rank> ranks)
    {
        if (rows.Count != ranks.Count || !rows.Select(x => x.Id).SequenceEqual(ranks.Select(x => x.Id)))
            throw new InvalidDataException("RUNNER_V9_E2E_GROUP_CENSUS_MISMATCH");
        var grouping = winner.CandidateGrouping
            ?? throw new InvalidDataException("RUNNER_V9_E2E_GROUP_MISSING_CONTRACT");
        if (grouping.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES")
            throw new InvalidDataException("RUNNER_V9_E2E_GROUP_UNSUPPORTED_CONTRACT");
        var guards = rows.Select(x =>
        {
            var tokens = IdentityComparison.NormalizeText(x.Observation.NomeCompleto)?
                .Split(' ',StringSplitOptions.RemoveEmptyEntries);
            return new Guard(tokens is { Length: >= 2 } ? tokens[0] : null,
                tokens is { Length: >= 2 } ? tokens[^1] : null,
                IdentityComparison.NormalizeText(x.Observation.NomeMae), x.Observation.DataNascimento);
        }).ToArray();
        var threshold = (decimal)Math.Log((double)grouping.MinimumPairPosterior /
            (1d - (double)grouping.MinimumPairPosterior));
        // Strong links only: compute the two directional FS results
        // independently. Full blocking adjacency still controls abstention.
        var strong = new List<Edge>();
        var incident = new HashSet<int>();
        foreach (var (a,b) in candidates)
        {
            var i = a-1;
            var j = b-1;
            if (i < 0 || j <= i || j >= rows.Count || rows[i].Manager == rows[j].Manager)
                throw new InvalidDataException("RUNNER_V9_E2E_GROUP_INVALID_EDGE");
            incident.Add(i);
            incident.Add(j);
            var g = guards[i];
            if (g != guards[j] || g.First is null || g.Last is null ||
                string.IsNullOrWhiteSpace(g.Mother) || g.Birth is null)
                continue;
            var forward = V9DevE2EScalarOracle.Score(winner,rows[i].Observation,rows[j].Observation);
            var reverse = V9DevE2EScalarOracle.Score(winner,rows[j].Observation,rows[i].Observation);
            if ((forward is null) != (reverse is null))
                throw new InvalidDataException("RUNNER_V9_E2E_GROUP_ASYMMETRIC_ABSTENTION");
            if (forward is null || reverse is null) continue;
            var strength = Math.Min(forward.LogOdds, reverse.LogOdds);
            if (strength >= threshold) strong.Add(new(i,j,strength));
        }
        var allowed = strong.Select(x => (x.A,x.B)).ToHashSet();
        if (allowed.Count != strong.Count)
            throw new InvalidDataException("RUNNER_V9_E2E_GROUP_DUPLICATE_EDGE");
        var groups = Enumerable.Range(0,rows.Count).Select(i => new HashSet<int> { i }).ToList();
        foreach (var edge in strong.OrderByDescending(x => x.Strength).ThenBy(x => x.A).ThenBy(x => x.B))
        {
            var left = groups.Single(x => x.Contains(edge.A));
            var right = groups.Single(x => x.Contains(edge.B));
            if (ReferenceEquals(left,right) || left.Count + right.Count > grouping.MaximumObservationsPerIdentity)
                continue;
            if (left.All(i => right.All(j => allowed.Contains((Math.Min(i,j),Math.Max(i,j))))))
            {
                left.UnionWith(right);
                groups.Remove(right);
            }
        }
        var roots = new int[rows.Count];
        var decisions = ranks.ToArray();
        foreach (var group in groups)
        {
            var root = group.Min();
            var destinations = new HashSet<Guid>();
            foreach (var i in group)
            {
                roots[i] = root;
                if (ranks[i].Resolved is Guid target) destinations.Add(target);
                if (rows[i].Status == "RESOLVIDO" && rows[i].Current is Guid current) destinations.Add(current);
                if (rows[i].OriginState == "REFERENCIA" && rows[i].Canonical is Guid canonical) destinations.Add(canonical);
            }
            foreach (var i in group)
            {
                var rank = ranks[i];
                var hasPrior = (rows[i].Status == "RESOLVIDO" && rows[i].Current is not null)
                    || (rows[i].OriginState == "REFERENCIA" && rows[i].Canonical is not null);
                if (destinations.Count > 1)
                    decisions[i] = rank with { Status="CONFLITO", Resolved=null, Reason="V9_R1_GRUPO_GOLD_CONFLITANTE" };
                else if (rank.Status == "NAO_RESOLVIDO")
                {
                    if (group.Count > 1 && destinations.Count == 1 && rank.HasPass && !hasPrior
                        && rank.Reason != "V9_R1_INDEFINIDO_SEM_EVIDENCIA_SEGURA"
                        && rank.Reason != "V9_R1_INDEFINIDO_LIMITE_EVIDENCIAS")
                        decisions[i] = rank with { Reason="V9_R1_INDEFINIDO_COM_INDICIO" };
                    else if (rank.Reason == "SEM_CANDIDATO_NO_RULESET_BLOCKING")
                    {
                        if (!rank.HasPass)
                            decisions[i] = rank with { Reason="EVIDENCIA_INSUFICIENTE_PARA_BLOCKING" };
                        else if (group.Count > 1)
                            decisions[i] = rank with { Reason=destinations.Count == 0 && group.All(j => ranks[j].CandidateCount == 0)
                                ? "V9_R1_INDEFINIDO_SEM_INDICIO" : "V9_R1_INDEFINIDO_CANDIDATO_FRACO" };
                        else if (incident.Contains(i))
                            decisions[i] = rank with { Reason="V9_R1_SILVER_CANDIDATO_NAO_RESOLVIDO" };
                    }
                }
            }
        }
        var canonicalPartition = string.Concat(roots.Select((root,i) =>
            FormattableString.Invariant($"{i+1}:{root+1}\n")));
        return new(decisions,roots,groups.Count,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPartition))));
    }

    public sealed record EdgeScoreReceipt(int DirectionsCompared, int ScoredDirections, int AbstainedDirections,
        string DirectionSha256);

    public static string DirectionDigest(IEnumerable<V9DevDirectionalScore> rows)
    {
        var canonical = string.Concat(rows.OrderBy(x => x.Source).ThenBy(x => x.Target).Select(x =>
            FormattableString.Invariant($"{x.Source}:{x.Target}:{x.Posterior?.ToString("F8",System.Globalization.CultureInfo.InvariantCulture) ?? "-"}:{x.LogOdds?.ToString("F8",System.Globalization.CultureInfo.InvariantCulture) ?? "-"}\n")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static void VerifyDirection(V9DevE2EScalarOracle.Pair? expected,
        V9DevE2EScalarOracle.Pair? actual)
    {
        if (expected != actual)
            throw new InvalidDataException("RUNNER_V9_E2E_SILVER_DIRECTION_SCORE_DIVERGENCE");
    }

    // All SQL edges, including those rejected by the grouping guard.
    // The observed side uses the same frozen ranking entry as ReplayDev,
    // with the complete neighbor list, not singleton calls or top-k.
    public static EdgeScoreReceipt VerifyDirectionalReplay(V9FittedSnapshot winner,
        IReadOnlyList<V9DevE2EFirstFullMembershipOracle.Source> pending,
        IReadOnlyList<(int A,int B)> edges)
    {
        var neighbors = Enumerable.Range(1, pending.Count).ToDictionary(x => x, _ => new HashSet<int>());
        foreach (var (a,b) in edges)
            if (a < 1 || b > pending.Count || a >= b ||
                pending[a-1].Manager == pending[b-1].Manager ||
                !neighbors[a].Add(b) || !neighbors[b].Add(a))
                throw new InvalidDataException("RUNNER_V9_E2E_SILVER_DIRECTION_EDGE_INVALID");
        int scored = 0, abstained = 0;
        var expectedDirections = new List<V9DevDirectionalScore>();
        foreach (var (source, targets) in neighbors.OrderBy(x => x.Key))
        {
            var row = pending[source-1].Observation;
            var ids = targets.OrderBy(x => x).ToArray();
            var expected = ids.ToDictionary(id => V9FrozenSourceResolveReplay.ObservationGuid(id),
                id => V9DevE2EScalarOracle.Score(winner.Payload, row, pending[id-1].Observation));
            var ranked = winner.RankPreview(new IdentityObservation(null, "SEM_CPF",
                row.NomeCompleto, row.DataNascimento, row.NomeMae),
                ids.Select(id =>
                {
                    var target = pending[id-1].Observation;
                    return new V9SnapshotCandidate(V9FrozenSourceResolveReplay.ObservationGuid(id),
                        target.NomeCompleto, target.DataNascimento, target.NomeMae);
                }).ToArray());
            if (ranked.Select(x => x.PessoaUuid).Distinct().Count() != ranked.Count ||
                ranked.Any(x => !expected.ContainsKey(x.PessoaUuid)))
                throw new InvalidDataException("RUNNER_V9_E2E_SILVER_DIRECTION_RANK_CENSUS");
            var observed = ranked.ToDictionary(x => x.PessoaUuid,
                x => new V9DevE2EScalarOracle.Pair(x.Score, x.LogOdds));
            foreach (var (id, score) in expected)
            {
                var actual = observed.GetValueOrDefault(id);
                VerifyDirection(score, actual);
                var target = ids.Single(x => V9FrozenSourceResolveReplay.ObservationGuid(x) == id);
                expectedDirections.Add(new(source,target,score?.Posterior,score?.LogOdds));
                if (actual is null) abstained++;
                else scored++;
            }
        }
        if (scored + abstained != checked(2 * edges.Count))
            throw new InvalidDataException("RUNNER_V9_E2E_SILVER_DIRECTION_CENSUS");
        return new(scored + abstained, scored, abstained, DirectionDigest(expectedDirections));
    }

    // A separate replay, not the actual live scorer's ephemeral groups.
    // Compare partitions by member sets, independent of transient GUID labels.
    public static void VerifyFrozenReplay(V9FittedSnapshot winner,
        IReadOnlyList<V9DevE2EFirstFullMembershipOracle.Source> pending,
        IReadOnlyList<(int A,int B)> edges, IReadOnlyList<int> expectedRoots)
    {
        var cohort = pending.Select((x,i) => new V9PhysicalCalibrationObservation(
            i+1,"DEV_UNLABELED_" + (i+1),"DEV",x.Manager,x.Observation.NomeCompleto,
            x.Observation.NomeMae,x.Observation.DataNascimento)).ToArray();
        var actual = V9FrozenSourceResolveReplay.ReplayDev(winner,cohort,edges).IdentityByObservation;
        if (actual.Count != pending.Count)
            throw new InvalidDataException("RUNNER_V9_E2E_GROUP_REPLAY_CENSUS");
        var roots = actual.GroupBy(x => x.Value).SelectMany(group =>
        {
            var root = group.Min(x => x.Key)-1;
            return group.Select(x => (Index:x.Key-1,Root:root));
        }).OrderBy(x => x.Index).Select(x => x.Root);
        if (!roots.SequenceEqual(expectedRoots))
            throw new InvalidDataException("RUNNER_V9_E2E_GROUP_REPLAY_DIVERGENCE");
    }
}

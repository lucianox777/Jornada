using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Nonpublishing stage-3 DEV preview of the EXACT frozen V9 FS/Resolve scorer
/// over UUIDs physically authenticated by the operational Gold candidate
/// reader. It is not complete-link Silver grouping and MUST NOT be used as
/// IV9ControlledDevCohortScorer or to publish linkage_resultado/Gold.
/// </summary>
public static class V9ControlledDevGoldScorePreview
{
    public sealed record Receipt(string WinnerSha256, Guid ModelId,
        IReadOnlyDictionary<long, ProbabilisticLinkageDecision> Decisions);

    public static Receipt Score(
        V9OperationalPinnedCohortRuntime pinnedWinner,
        IReadOnlyList<V9FrozenRunObservation> frozenRows,
        V9ControlledDevOperationalCandidateSource.VerifiedRunCandidates verifiedSource)
    {
        ArgumentNullException.ThrowIfNull(pinnedWinner);
        ArgumentNullException.ThrowIfNull(frozenRows);
        ArgumentNullException.ThrowIfNull(verifiedSource);

        // All three source maps must cover the exact, globally ordered,
        // frozen run universe; do not silently score an incomplete page.
        if (frozenRows.Count == 0
            || frozenRows.Any(r => r is null || r.PessoaObservacaoId <= 0
                || r.Observation is null || r.Observation.Cpf is not null
                || r.Observation.ResolutionAttributes is { Count: > 0 }))
            throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH");
        var ids = frozenRows.Select(r => r.PessoaObservacaoId).ToArray();
        var expected = ids.ToHashSet();
        if (expected.Count != frozenRows.Count
            || !ids.SequenceEqual(ids.OrderBy(x => x))
            || !expected.SetEquals(verifiedSource.ManagerByObservation.Keys)
            || !expected.SetEquals(verifiedSource.CandidatesByObservation.Keys)
            || !expected.SetEquals(verifiedSource.EvidenceByObservation.Keys)
            || !expected.SetEquals(verifiedSource.AuthenticatedSilverByObservation.Keys))
            throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH");

        var winner = pinnedWinner.FrozenWinner;
        if (pinnedWinner.ModelId == Guid.Empty
            || !string.Equals(winner.ContentSha256, pinnedWinner.WinnerSha256,
                StringComparison.Ordinal)
            || pinnedWinner.ModelId !=
                V9OperationalSqlModelConverter.ModelIdFromWinnerSha(pinnedWinner.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_WINNER_MISMATCH");

        var decisions = new Dictionary<long, ProbabilisticLinkageDecision>(frozenRows.Count);
        foreach (var frozen in frozenRows)
        {
            var id = frozen.PessoaObservacaoId;
            if (!verifiedSource.ManagerByObservation.TryGetValue(id, out var manager)
                || string.IsNullOrWhiteSpace(manager)
                || !verifiedSource.CandidatesByObservation.TryGetValue(id, out var candidates)
                || candidates is null
                || !verifiedSource.EvidenceByObservation.TryGetValue(id, out var evidence)
                || evidence is null
                || !verifiedSource.AuthenticatedSilverByObservation.TryGetValue(
                    id, out var authenticatedSilver)
                || authenticatedSilver is null
                || !Equals(authenticatedSilver, frozen.Observation))
                throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH");

            if (evidence.CandidateCount != candidates.Count
                || evidence.CandidateUuids.Count != candidates.Count
                || !string.Equals(evidence.CanonicalUuidsSha256,
                    CanonicalUuidSha256(evidence.CandidateUuids), StringComparison.Ordinal))
                throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_CANDIDATE_RECEIPT_MISMATCH");

            V9ControlledDevOperationalCandidateSource.VerifyCanonicalCandidateMembership(
                evidence.CandidateUuids, candidates);
            // Null Gold core fields remain missing evidence for the original
            // frozen FS kernel; they are valid for Gold PARCIAL references.
            if (candidates.Any(c => c is null || c.PessoaUuid == Guid.Empty)
                || candidates.Select(c => c.PessoaUuid).Distinct().Count() != candidates.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_GOLD_REFERENCE_INVALID");

            // This is the winner's original FS, threshold, conflict margin
            // and abstention policy. Person UUIDs are actual Gold candidates.
            var original = winner.ResolvePreview(pinnedWinner.ModelId,
                frozen.Observation,
                candidates.Select(c => new V9SnapshotCandidate(
                    c.PessoaUuid, c.NomeCompleto, c.DataNascimento, c.NomeMae)).ToArray());
            var allowed = candidates.Select(c => c.PessoaUuid).ToHashSet();
            if (original.ModeloId != pinnedWinner.ModelId
                || original.PessoaUuidResolvido is Guid resolved && !allowed.Contains(resolved)
                || original.MelhorCandidatoUuid is Guid best && !allowed.Contains(best)
                || original.SegundoCandidatoUuid is Guid second && !allowed.Contains(second)
                || !decisions.TryAdd(id, original))
                throw new InvalidDataException("RUNNER_V9_DEV_PREVIEW_DECISION_INVALID");
        }
        return new Receipt(pinnedWinner.WinnerSha256, pinnedWinner.ModelId,
            new ReadOnlyDictionary<long, ProbabilisticLinkageDecision>(decisions));
    }

    private static string CanonicalUuidSha256(IReadOnlyList<Guid> uuids)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var uuid in uuids)
            hash.AppendData(Encoding.UTF8.GetBytes(uuid.ToString("D") + "\n"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

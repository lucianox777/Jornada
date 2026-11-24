using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Controlled DEV b1 scoring primitive. The caller must authenticate the
/// distinct candidate-person census and all eligible persistent-origin
/// observations in the same SQL SNAPSHOT before calling this primitive.
/// Not a second FS engine, a SQL admission proof, or a publication API.
/// </summary>
internal static class V9ControlledDevSecureEvidenceScore
{
    internal const string PolicyVersion = "V9_DEV_B1_SECURE_PERSISTENT_ORIGIN_V1";
    internal const string NoSecureEvidence = "V9_R1_INDEFINIDO_SEM_EVIDENCIA_SEGURA";
    internal const string EvidenceLimit = "V9_R1_INDEFINIDO_LIMITE_EVIDENCIAS";

    internal sealed record Evidence(long ObservationId, long OriginId,
        V9SnapshotCandidate Candidate);
    internal sealed record Receipt(string Policy, int CandidatePeople, long SecureRecords,
        long ComparedRecords, long ScoreableRecords, int PeopleWithoutScoreableEvidence,
        string EvidenceMembershipSha256);
    internal sealed record Result(ProbabilisticLinkageDecision Decision, Receipt EvidenceReceipt);

    internal static Result Score(V9FittedSnapshot winner, Guid modelId,
        IdentityObservation source, IReadOnlyList<Guid> candidatePeople,
        IReadOnlyList<Evidence> secureEvidence, long? maximumComparisons = null)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidatePeople);
        ArgumentNullException.ThrowIfNull(secureEvidence);
        if (modelId == Guid.Empty ||
            modelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256)
            || source.Cpf is not null || source.ResolutionAttributes is { Count: > 0 }
            || maximumComparisons is < 0)
            throw new InvalidDataException("RUNNER_V9_DEV_B1_INPUT_INVALID");
        var people = candidatePeople.ToHashSet();
        if (people.Count != candidatePeople.Count || people.Contains(Guid.Empty)
            || secureEvidence.Any(e => e is null || e.ObservationId <= 0 || e.OriginId <= 0
                || e.Candidate is null || !people.Contains(e.Candidate.PersonUuid))
            || secureEvidence.Select(e => e.ObservationId).Distinct().Count() != secureEvidence.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_B1_EVIDENCE_MEMBERSHIP_INVALID");
        // A distinct digest for b1 membership, not any scientific corpus,
        // blocking, fitted snapshot or candidate UUID-union fingerprint.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));
        Append(PolicyVersion + "\n");
        foreach (var id in people.OrderBy(x => x)) Append($"P:{id:D}\n");
        foreach (var item in secureEvidence.OrderBy(e => e.Candidate.PersonUuid)
                     .ThenBy(e => e.OriginId).ThenBy(e => e.ObservationId))
            Append(FormattableString.Invariant(
                $"E:{item.Candidate.PersonUuid:D}:{item.OriginId}:{item.ObservationId}\n"));
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        Receipt Census(long compared, long scoreable, int without) =>
            new(PolicyVersion, people.Count, secureEvidence.Count, compared, scoreable, without, digest);
        ProbabilisticLinkageDecision Empty(string reason) =>
            winner.ResolveRankedPreview(modelId, Array.Empty<CandidateScore>(), reason);

        if (maximumComparisons.HasValue && secureEvidence.Count > maximumComparisons.Value)
            return new(Empty(EvidenceLimit), Census(0, 0,
                people.Count - secureEvidence.Select(e => e.Candidate.PersonUuid).Distinct().Count()));

        var maxima = new Dictionary<Guid, CandidateScore>();
        long compared = 0, scoreable = 0;
        // Stable tie-break among records of a single person. Across persons,
        // use the same log-odds and UUID ordering as the frozen resolver.
        foreach (var item in secureEvidence.OrderBy(e => e.ObservationId))
        {
            var ranked = winner.RankPreview(source, new[] { item.Candidate },
                distinctCandidatePersonCount: people.Count);
            compared++;
            if (ranked.Count == 0) continue; // frozen TF abstention
            if (ranked.Count != 1 || ranked[0].PessoaUuid != item.Candidate.PersonUuid)
                throw new InvalidDataException("RUNNER_V9_DEV_B1_PAIR_RANK_INVALID");
            var pair = ranked[0];
            scoreable++;
            if (!maxima.TryGetValue(pair.PessoaUuid, out var current)
                || pair.LogOdds > current.LogOdds)
                maxima[pair.PessoaUuid] = pair;
        }
        var ranking = maxima.Values.OrderByDescending(x => x.LogOdds)
            .ThenBy(x => x.PessoaUuid).ToArray();
        var without = people.Count - ranking.Length;
        var decision = winner.ResolveRankedPreview(modelId, ranking,
            people.Count == 0 ? "SEM_CANDIDATO_NO_RULESET_BLOCKING" : NoSecureEvidence);
        // No incomplete competitive universe may authorize an association.
        // A conflict actually established by the frozen resolver is preserved.
        if (without > 0 && decision.Status != ResolutionStatus.CONFLITO)
            decision = decision with {
                Status = ResolutionStatus.NAO_RESOLVIDO,
                PessoaUuidResolvido = null,
                Motivo = NoSecureEvidence
            };
        return new(decision, Census(compared, scoreable, without));
    }
}

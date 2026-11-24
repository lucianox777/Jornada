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
    internal const string PolicyVersion = "V9_DEV_B1_SECURE_PERSISTENT_ORIGIN_RECORD_RANK_V1";

    // Full positive SQL bigint encoded injectively in a transient Guid.
    // Never a Gold UUID or a persisted identity. Matches the historical
    // observation-Guid ordering for IDs in the original int domain.
    private static Guid ObservationGuid(long id) =>
        new(unchecked((int)id), unchecked((short)(id >> 32)),
            unchecked((short)(id >> 48)), 0, 0, 0, 0, 0, 0, 0, 1);
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

        // Same sequence as the #38 calibration: one distinct candidate per
        // observation, rank the whole record population once, collapse the
        // best log-odds per person, then resolve between persons. The frozen
        // kernel alone decides how its count argument affects the prior.
        var records = secureEvidence.OrderBy(e => e.ObservationId)
            .Select(e => e.Candidate with { PersonUuid = ObservationGuid(e.ObservationId) })
            .ToArray();
        var personByRecord = secureEvidence.ToDictionary(
            e => ObservationGuid(e.ObservationId), e => e.Candidate.PersonUuid);
        var rankedRecords = winner.RankPreview(source, records);
        if (rankedRecords.Count > records.Length
            || rankedRecords.Select(x => x.PessoaUuid).Distinct().Count() != rankedRecords.Count
            || rankedRecords.Any(x => !personByRecord.ContainsKey(x.PessoaUuid)))
            throw new InvalidDataException("RUNNER_V9_DEV_B1_PAIR_RANK_INVALID");
        var ranking = rankedRecords
            .GroupBy(x => personByRecord[x.PessoaUuid])
            .Select(g => g.First() with { PessoaUuid = g.Key })
            .OrderByDescending(x => x.LogOdds)
            .ThenBy(x => x.PessoaUuid).ToArray();
        long compared = records.Length, scoreable = rankedRecords.Count;
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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Controlled, disposable DEV implementation of the real RunAsync V9 contract.
/// One physically verified Silver-to-Silver complete-link grouping per frozen
/// FULL run, followed by independent Gold-reference FS decisions and the
/// PO-approved r1 consistency veto. Produces RAW results only; RunAsync owns
/// the unchanged transactional Gold publication.
/// </summary>
public sealed class V9ControlledDevCohortScorer : IV9ControlledDevCohortScorer
{
    private readonly IOperationalSqlAdapter sql;
    private readonly V9OperationalPinnedCohortRuntime winner;
    private readonly Guid attemptId;
    private readonly int maximumGoldCandidates;
    private readonly ConcurrentDictionary<long, byte> consumed = new();
    private IReadOnlyDictionary<long, (IdentityObservation Observation,
        ProbabilisticLinkageDecision Decision)>? prepared;
    private Guid preparedRunId;

    // Explicitly installed only by the disposable 1k command, after C07 verification.
    internal sealed record GoldScore(long Source, long Record, Guid Person, decimal? Posterior, decimal? LogOdds);
    internal Action<Guid, IReadOnlyList<long>, string, IReadOnlyDictionary<int, Guid>,
        IReadOnlyList<V9DevDirectionalScore>, IReadOnlyList<GoldScore>,
        IReadOnlyDictionary<long, IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>>? DiagnosticObserver { get; set; }

    // Timing-only diagnostic; not consulted by the scorer or publication.
    internal Action<double,double,double,double,double>? DiagnosticTimingObserver { get; set; }
    public string WinnerSha256 => winner.WinnerSha256;
    internal const string DiagnosticFixtureSetting = "Linkage:V9BlockingMaintenance:DiagnosticLegacyFixture";
    internal V9ControlledDevBlockingMaintenance? Maintenance { get; private init; }

    public V9ControlledDevCohortScorer(IOperationalSqlAdapter sql,
        V9OperationalPinnedCohortRuntime verifiedWinner,
        Guid disposableAttemptId, int maximumGoldCandidates = 100_000)
    {
        this.sql = sql ?? throw new ArgumentNullException(nameof(sql));
        winner = verifiedWinner ?? throw new ArgumentNullException(nameof(verifiedWinner));
        if (disposableAttemptId == Guid.Empty || maximumGoldCandidates < 1
            || winner.ModelId == Guid.Empty
            || winner.ModelId !=
                V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_SCORER_PIN_INVALID");
        attemptId = disposableAttemptId;
        this.maximumGoldCandidates = maximumGoldCandidates;
    }

    /// <summary>
    /// Explicit DEV composition boundary; cannot silently choose a SQL-active
    /// model or a productive database. RunV9ControlledDevAsync repeats preflight.
    /// </summary>
    public static async Task<V9ControlledDevCohortScorer> CreateAsync(
        IConfiguration configuration, IOperationalSqlAdapter sql,
        Guid disposableAttemptId, CancellationToken ct, string? effectiveEnvironment = null)
    {
        var verified = await V9ControlledDevSelection.VerifyAsync(
            configuration, sql, disposableAttemptId, ct);
        var enabled = configuration.GetValue<bool>(V9BlockingMaintenanceGate.EnabledSetting);
        var diagnostic = configuration.GetValue<bool>(DiagnosticFixtureSetting);
        if (enabled == diagnostic)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_EXPLICIT_PATH_REQUIRED");
        if (diagnostic)
            return new(sql, verified.Runtime, disposableAttemptId);

        if (!Guid.TryParseExact(configuration[V9BlockingMaintenanceGate.AttemptSetting], "D", out var attempt)
            || attempt != disposableAttemptId
            || !V9BlockingMaintenanceGate.IsExplicitDevOptIn(effectiveEnvironment, enabled, attempt))
            throw new InvalidOperationException(V9BlockingMaintenanceGate.ProductiveNotCertified);
        var payload = verified.Runtime.FrozenWinner.Payload;
        var prefix = V9CalibrationBlockingProjection.ResolveFrozenPrefixLength(
            payload.ProjectionSchemaVersion, payload.ProjectionFingerprintSha256);
        return new(sql, verified.Runtime, disposableAttemptId)
        {
            Maintenance = new(effectiveEnvironment!, attempt, prefix, payload.ProjectionFingerprintSha256)
        };
    }

    public Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct) =>
        throw new InvalidOperationException("RUNNER_V9_DEV_EXPLICIT_WINNER_REQUIRED");

    public Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(
        int version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (version != 9)
            throw new InvalidDataException("RUNNER_V9_DEV_MODEL_VERSION_MISMATCH");
        var frozen = winner.FrozenWinner.Payload;
        var model = new ProbabilisticLinkageModelRef(
            winner.ModelId, 9,
            LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
            frozen.Threshold, frozen.ConflictMargin)
        {
            BlockingContract = new(
                "V9_DEV_R1_" + winner.WinnerSha256,
                winner.WinnerSha256,
                frozen.ProjectionSchemaVersion,
                frozen.ProjectionFingerprintSha256)
        };
        V9ControlledDevSelection.VerifyRunModelPin(model, winner.WinnerSha256);
        return Task.FromResult(model);
    }

    public Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
        IdentityObservation observation, Guid modeloId, CancellationToken ct) =>
        throw new InvalidOperationException("RUNNER_V9_DEV_REQUIRES_FROZEN_COHORT");

    public async Task PrepareAsync(Guid runId, ProbabilisticLinkageModelRef model,
        IReadOnlyList<V9FrozenRunObservation> frozenRows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(frozenRows);
        ct.ThrowIfCancellationRequested();
        if (preparedRunId != Guid.Empty || prepared is not null || runId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_SCORER_SINGLE_USE");
        V9ControlledDevSelection.VerifyRunModelPin(model, winner.WinnerSha256);
        if (frozenRows.Any(x => x is null || x.PessoaObservacaoId <= 0
                || x.Observation is null || x.Observation.Cpf is not null
                || x.Observation.ResolutionAttributes is { Count: > 0 })
            || !frozenRows.Select(x => x.PessoaObservacaoId)
                .SequenceEqual(frozenRows.Select(x => x.PessoaObservacaoId).OrderBy(x => x))
            || frozenRows.Select(x => x.PessoaObservacaoId).Distinct().Count() != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_COHORT_INVALID");

        // A failed preparation is never retried under the same mutable run.
        preparedRunId = runId;
        await using var connection = await sql.OpenAsync(ct);
        var phaseClock = System.Diagnostics.Stopwatch.StartNew();
        var verified = await V9ControlledDevOperationalCandidateSource.ReadAsync(
            connection, winner, attemptId, runId, frozenRows,
            maximumGoldCandidates, ct, Maintenance is not null, Maintenance?.EffectiveEnvironment);
        var readMs = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        if (Maintenance is not null && verified.IsolatedBlockingManifest is null)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_MISSING");
        var physical = verified.SilverGraph
            ?? throw new InvalidDataException("RUNNER_V9_DEV_SILVER_GRAPH_MISSING");
        if (physical.CanonicalEdgesSha256.Length != 64)
            throw new InvalidDataException("RUNNER_V9_DEV_SILVER_GRAPH_RECEIPT_INVALID");

        // All source rows are synthetic LABEL-FREE wrappers for the immutable
        // DEV kernel; BasePersonId never informs the blind grouping.
        var cohort = frozenRows.Select((x, index) =>
            new V9PhysicalCalibrationObservation(
                index + 1, "DEV_UNLABELED_" + (index + 1), "DEV",
                verified.ManagerByObservation[x.PessoaObservacaoId],
                x.Observation.NomeCompleto, x.Observation.NomeMae,
                x.Observation.DataNascimento)).ToArray();
        IReadOnlyList<V9DevDirectionalScore>? directions = null;
        var grouped = DiagnosticObserver is null
            ? winner.ScoreControlledDevCohort(cohort, physical.Edges)
            : winner.ScoreControlledDevCohort(cohort, physical.Edges, values => directions = values);
        if (grouped.SnapshotSha256 != winner.WinnerSha256
            || grouped.IdentityByObservation.Count != frozenRows.Count
            || grouped.Decisions.Count != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_GROUP_INCOMPLETE");

        var groups = new Dictionary<long, Guid>(frozenRows.Count);
        for (var index = 0; index < frozenRows.Count; index++)
        {
            if (!grouped.IdentityByObservation.TryGetValue(index + 1, out var group)
                || group == Guid.Empty
                || !groups.TryAdd(frozenRows[index].PessoaObservacaoId, group))
                throw new InvalidDataException("RUNNER_V9_DEV_GROUP_INVALID");
        }

        var groupMs = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        // b1: the Gold UUID universe is unchanged. The score is based only
        // on the physically authenticated persistent-origin records, never
        // the recomposed Gold summary or a synthetic group UUID.
        var originFacts = verified.SecureOriginFactsByPerson
            ?? throw new InvalidDataException("RUNNER_V9_DEV_B1_ORIGIN_CENSUS_MISSING");
        var expectedPeople = verified.CandidatesByObservation.Values
            .SelectMany(x => x).Select(x => x.PessoaUuid).ToHashSet();
        if (!expectedPeople.SetEquals(originFacts.Keys))
            throw new InvalidDataException("RUNNER_V9_DEV_B1_ORIGIN_CENSUS_INVALID");
        var goldScores = DiagnosticObserver is null ? null : new List<GoldScore>();
        var raw = new Dictionary<long, ProbabilisticLinkageDecision>(frozenRows.Count);
        var receipts = new Dictionary<long, V9ControlledDevSecureEvidenceScore.Receipt>(frozenRows.Count);
        foreach (var row in frozenRows)
        {
            ct.ThrowIfCancellationRequested();
            if (!verified.AuthenticatedSilverByObservation.TryGetValue(row.PessoaObservacaoId, out var source)
                || !Equals(source, row.Observation)
                || !verified.EvidenceByObservation.TryGetValue(row.PessoaObservacaoId, out var candidateReceipt)
                || !verified.CandidatesByObservation.TryGetValue(row.PessoaObservacaoId, out var candidates)
                || candidateReceipt.CandidateCount != candidates.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_B1_INPUT_INVALID");
            V9ControlledDevOperationalCandidateSource.VerifyCanonicalCandidateMembership(
                candidateReceipt.CandidateUuids, candidates);
            var scored = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
                winner.FrozenWinner, winner.ModelId, source, candidateReceipt.CandidateUuids, originFacts,
                observe: goldScores is null ? null : values => goldScores.AddRange(values.Select(v =>
                    new GoldScore(row.PessoaObservacaoId,v.ObservationId,v.Person,v.Posterior,v.LogOdds))));
            if (!raw.TryAdd(row.PessoaObservacaoId, scored.Decision)
                || !receipts.TryAdd(row.PessoaObservacaoId, scored.EvidenceReceipt))
                throw new InvalidDataException("RUNNER_V9_DEV_B1_DUPLICATE_SOURCE");
        }
        // Distinct b1 receipts, never a replacement for scientific hashes.
        // Aggregate counts and a deterministic digest of per-observation
        // evidence membership; do not emit person attributes in logs.
        using (var digest = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256))
        {
            foreach (var item in receipts.OrderBy(x => x.Key))
                digest.AppendData(System.Text.Encoding.UTF8.GetBytes(FormattableString.Invariant(
                    $"{item.Key}:{item.Value.EvidenceMembershipSha256}\n")));
            Console.WriteLine("V9_DEV_B1_EVIDENCE " + JsonSerializer.Serialize(new {
                runId, policy = V9ControlledDevSecureEvidenceScore.PolicyVersion,
                observations = receipts.Count,
                candidatePeople = receipts.Values.Sum(r => (long)r.CandidatePeople),
                secureRecords = receipts.Values.Sum(r => r.SecureRecords),
                comparedRecords = receipts.Values.Sum(r => r.ComparedRecords),
                scoreableRecords = receipts.Values.Sum(r => r.ScoreableRecords),
                peopleWithoutScoreableEvidence = receipts.Values.Sum(r => (long)r.PeopleWithoutScoreableEvidence),
                evidenceMembershipSha256 = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()
            }));
        }
        var scoreMs = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        // A singleton in the FINAL complete-link partition can still have a
        // weak/incompatible SILVER blocking candidate. It is never an empty
        // candidate search for NEW identity publication.
        var silverWithCandidates = new HashSet<long>();
        foreach (var (a, b) in physical.Edges)
        {
            if (a < 1 || a >= b || b > frozenRows.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_EDGE_INVALID");
            silverWithCandidates.Add(frozenRows[a - 1].PessoaObservacaoId);
            silverWithCandidates.Add(frozenRows[b - 1].PessoaObservacaoId);
        }
        var translation = V9ControlledDevGroupGoldR1.Translate(
            groups, raw, verified.CandidatesByObservation,
            verified.ExistingResolvedGoldLinksByObservation
                ?? throw new InvalidDataException("RUNNER_V9_DEV_GOLD_LINKS_NOT_READ"),
            silverWithCandidates,
            verified.NoEligibleBlockingPassObservationIds
                ?? throw new InvalidDataException("RUNNER_V9_DEV_BLOCKING_PASS_CENSUS_MISSING"));
        var ready = new Dictionary<long,
            (IdentityObservation Observation, ProbabilisticLinkageDecision Decision)>();
        foreach (var row in frozenRows)
        {
            if (!translation.TryGetValue(row.PessoaObservacaoId, out var decision)
                || decision.ModeloId != winner.ModelId
                || !ready.TryAdd(row.PessoaObservacaoId, (row.Observation, decision)))
                throw new InvalidDataException("RUNNER_V9_DEV_RAW_DECISIONS_INCOMPLETE");
        }
        if (ready.Count != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_RAW_DECISIONS_INCOMPLETE");
        DiagnosticTimingObserver?.Invoke(readMs,verified.SnapshotElapsedMilliseconds,groupMs,scoreMs,phaseClock.Elapsed.TotalMilliseconds);
        if (DiagnosticObserver is { } observer)
            observer(runId, Array.AsReadOnly(frozenRows.Select(x => x.PessoaObservacaoId).ToArray()),
                physical.CanonicalEdgesSha256, grouped.IdentityByObservation,
                directions ?? throw new InvalidDataException("RUNNER_V9_E2E_DIAGNOSTIC_MISSING"),
                goldScores!.AsReadOnly(), verified.CandidatesByObservation);
        prepared = new ReadOnlyDictionary<long,
            (IdentityObservation Observation, ProbabilisticLinkageDecision Decision)>(ready);
    }

    public Task<ProbabilisticLinkageDecision> ResolvePreparedAsync(
        Guid runId, long pessoaObservacaoId, IdentityObservation observation,
        Guid modelId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = prepared;
        if (current is null || runId == Guid.Empty || runId != preparedRunId
            || modelId != winner.ModelId
            || !current.TryGetValue(pessoaObservacaoId, out var item)
            || !Equals(item.Observation, observation)
            || !consumed.TryAdd(pessoaObservacaoId, 0))
            throw new InvalidOperationException("RUNNER_V9_DEV_PREPARED_RESULT_MISMATCH");
        return Task.FromResult(item.Decision);
    }
}

/// <summary>
/// PO r1: independent complete FS approval per observation + global V9 group
/// consistency veto. The grouping never grants an otherwise absent Gold link.
/// Does not publish, choose a group representative, or modify scientific scores.
/// </summary>
public static class V9ControlledDevGroupGoldR1
{
    public static IReadOnlyDictionary<long, ProbabilisticLinkageDecision> Translate(
        IReadOnlyDictionary<long, Guid> groupByObservation,
        IReadOnlyDictionary<long, ProbabilisticLinkageDecision> individualFs,
        IReadOnlyDictionary<long,
            IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>> candidates,
        IReadOnlyDictionary<long, Guid> alreadyEstablishedGold,
        IReadOnlySet<long> silverWithCandidates,
        IReadOnlySet<long> noEligiblePass)
    {
        ArgumentNullException.ThrowIfNull(groupByObservation);
        ArgumentNullException.ThrowIfNull(individualFs);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadyEstablishedGold);
        ArgumentNullException.ThrowIfNull(silverWithCandidates);
        ArgumentNullException.ThrowIfNull(noEligiblePass);
        if (noEligiblePass.Except(groupByObservation.Keys).Any())
            throw new InvalidDataException("RUNNER_V9_R1_BLOCKING_PASS_OUTSIDE_UNIVERSE");
        if (silverWithCandidates.Except(groupByObservation.Keys).Any())
            throw new InvalidDataException("RUNNER_V9_R1_SILVER_GRAPH_OUTSIDE_UNIVERSE");
        if (groupByObservation.Count != individualFs.Count
            || groupByObservation.Count != candidates.Count
            || groupByObservation.Keys.Except(individualFs.Keys).Any()
            || groupByObservation.Keys.Except(candidates.Keys).Any()
            || alreadyEstablishedGold.Keys.Except(groupByObservation.Keys).Any())
            throw new InvalidDataException("RUNNER_V9_R1_UNIVERSE_MISMATCH");

        var output = new Dictionary<long, ProbabilisticLinkageDecision>();
        foreach (var group in groupByObservation.GroupBy(x => x.Value))
        {
            if (group.Key == Guid.Empty)
                throw new InvalidDataException("RUNNER_V9_R1_GROUP_INVALID");
            var members = group.Select(x => x.Key).OrderBy(x => x).ToArray();
            var targets = new HashSet<Guid>();
            foreach (var id in members)
            {
                var decision = individualFs[id];
                var gold = candidates[id];
                if (decision is null || decision.ModeloId == Guid.Empty
                    || gold is null || gold.Any(c => c is null || c.PessoaUuid == Guid.Empty)
                    || gold.Select(c => c.PessoaUuid).Distinct().Count() != gold.Count)
                    throw new InvalidDataException("RUNNER_V9_R1_GOLD_CANDIDATE_INVALID");
                if (noEligiblePass.Contains(id) && gold.Count > 0)
                    throw new InvalidDataException("RUNNER_V9_R1_CANDIDATE_WITHOUT_PASS");
                if (decision.Status == ResolutionStatus.RESOLVIDO)
                {
                    if (noEligiblePass.Contains(id))
                        throw new InvalidDataException("RUNNER_V9_R1_RESOLVED_WITHOUT_PASS");
                    if (decision.PessoaUuidResolvido is not Guid resolved
                        || resolved == Guid.Empty
                        || gold.All(x => x.PessoaUuid != resolved))
                        throw new InvalidDataException("RUNNER_V9_R1_RESOLVED_GOLD_UNAUTHENTICATED");
                    targets.Add(resolved);
                }
                else if (decision.PessoaUuidResolvido is not null)
                    throw new InvalidDataException("RUNNER_V9_R1_UNRESOLVED_HAS_DESTINATION");
                if (alreadyEstablishedGold.TryGetValue(id, out var prior))
                {
                    if (prior == Guid.Empty)
                        throw new InvalidDataException("RUNNER_V9_R1_GOLD_LINK_INVALID");
                    targets.Add(prior);
                }
                if (decision.MelhorCandidatoUuid is Guid best
                    && gold.All(x => x.PessoaUuid != best)
                    || decision.SegundoCandidatoUuid is Guid second
                    && gold.All(x => x.PessoaUuid != second))
                    throw new InvalidDataException("RUNNER_V9_R1_CANDIDATE_NOT_AUTHENTICATED");
            }

            var conflicting = targets.Count > 1;
            var groupWithoutGoldCandidates = targets.Count == 0
                && members.All(id => candidates[id].Count == 0);
            foreach (var id in members)
            {
                var decision = individualFs[id];
                // Existing established Gold links are never automatically
                // revised by a new scorer. SQL publication applies precedence.
                if (conflicting)
                {
                    output.Add(id, decision with {
                        Status = ResolutionStatus.CONFLITO,
                        PessoaUuidResolvido = null,
                        Motivo = "V9_R1_GRUPO_GOLD_CONFLITANTE"
                    });
                    continue;
                }
                if (decision.Status == ResolutionStatus.RESOLVIDO)
                {
                    output.Add(id, decision);
                    continue;
                }

                // No group inheritance: a member that individually abstains
                // must NOT become its own new reference simply because its
                // neighbor (or previous round's reference) has no candidate.
                var noCandidate = decision.Motivo?.StartsWith(
                    "SEM_CANDIDATO_", StringComparison.Ordinal) == true;
                if (noCandidate && candidates[id].Count > 0)
                    throw new InvalidDataException("RUNNER_V9_R1_FALSE_NO_CANDIDATE");

                // A unique established/individually approved target is only
                // a group indication. Never copy its UUID or change the FS
                // ranking/scores. Existing protected links, FS conflicts and
                // insufficient blocking evidence retain their precedence.
                if (members.Length > 1 && targets.Count == 1
                    && decision.Status == ResolutionStatus.NAO_RESOLVIDO
                    && !noEligiblePass.Contains(id)
                    && decision.Motivo is not (V9ControlledDevSecureEvidenceScore.NoSecureEvidence
                        or V9ControlledDevSecureEvidenceScore.EvidenceLimit)
                    && !alreadyEstablishedGold.ContainsKey(id))
                {
                    output.Add(id, decision with {
                        Motivo = "V9_R1_INDEFINIDO_COM_INDICIO"
                    });
                    continue;
                }

                if (noCandidate)
                {
                    if (noEligiblePass.Contains(id)
                        || members.Length > 1 || silverWithCandidates.Contains(id))
                        output.Add(id, decision with {
                            Status = ResolutionStatus.NAO_RESOLVIDO,
                            Motivo = noEligiblePass.Contains(id)
                                ? "EVIDENCIA_INSUFICIENTE_PARA_BLOCKING"
                                : members.Length > 1
                                    ? groupWithoutGoldCandidates
                                        ? "V9_R1_INDEFINIDO_SEM_INDICIO"
                                        : "V9_R1_INDEFINIDO_CANDIDATO_FRACO"
                                    : "V9_R1_SILVER_CANDIDATO_NAO_RESOLVIDO"
                        });
                    else
                        output.Add(id, decision);
                }
                else
                    output.Add(id, decision);
            }
        }
        if (output.Count != groupByObservation.Count)
            throw new InvalidDataException("RUNNER_V9_R1_DECISIONS_INCOMPLETE");
        return new ReadOnlyDictionary<long, ProbabilisticLinkageDecision>(output);
    }
}

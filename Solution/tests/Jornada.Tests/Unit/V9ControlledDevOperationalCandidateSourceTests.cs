using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

/// <summary>
/// Input/connection regression for the real operational SQL candidate reader.
/// No fake Gold candidates and no scientific TEST execution are introduced.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9ControlledDevOperationalCandidateSourceTests
{
    private static readonly V9OperationalPinnedCohortRuntime Pinned =
        V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(
            V9FittedSnapshotTests.CompleteTf4OperationalWinner());

    private static V9FrozenRunObservation Row(long id, string? cpf = null) =>
        new(id, new IdentityObservation(cpf,
            cpf is null ? "SEM_CPF" : null,
            "ANA PAULA SILVA", new DateOnly(1990, 1, 1), "MARIA SILVA"));

    [Test]
    public void Gold_REFERENCIA_with_PARCIAL_core_preserves_null_evidence_without_repair()
    {
        var id = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var partial =
            V9ControlledDevOperationalCandidateSource.PreserveReferenceEvidence(
                id, "REFERENCIA", null, null, null);
        Assert.That(partial.PessoaUuid, Is.EqualTo(id));
        Assert.That(partial.NomeCompleto, Is.Null);
        Assert.That(partial.DataNascimento, Is.Null);
        Assert.That(partial.NomeMae, Is.Null);

        Assert.That((Action)(() =>
            V9ControlledDevOperationalCandidateSource.PreserveReferenceEvidence(
                id, "PROVISORIA", null, null, null)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_GOLD_REFERENCE_STALE"));
        Assert.That((Action)(() =>
            V9ControlledDevOperationalCandidateSource.PreserveReferenceEvidence(
                id, "INDEFINIDA", null, null, null)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_GOLD_REFERENCE_STALE"));
        Assert.That((Action)(() =>
            V9ControlledDevOperationalCandidateSource.PreserveReferenceEvidence(
                id, null, null, null, null)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_GOLD_REFERENCE_MISSING"));
        Assert.That((Action)(() =>
            V9ControlledDevOperationalCandidateSource.PreserveReferenceEvidence(
                Guid.Empty, "REFERENCIA", null, null, null)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_GOLD_REFERENCE_MISSING"));
    }

    [Test]
    public void Canonical_blocking_union_must_equal_all_Gold_joined_UUIDs()
    {
        var a = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var b = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var joined = new[]
        {
            new V9ControlledDevOperationalCandidateSource.GoldCandidate(
                a, "ANA", new DateOnly(1990, 1, 1), "MARIA"),
            new V9ControlledDevOperationalCandidateSource.GoldCandidate(
                b, "JOAO", new DateOnly(1980, 1, 1), "TERESA")
        };

        Assert.DoesNotThrow((Action)(() =>
            V9ControlledDevOperationalCandidateSource.VerifyCanonicalCandidateMembership(
                [a, b], joined)));

        void Reject(IReadOnlyList<Guid> verified,
            IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate> actual) =>
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource.VerifyCanonicalCandidateMembership(
                    verified, actual)), Throws.TypeOf<InvalidDataException>()
                    .With.Message.EqualTo("RUNNER_V9_DEV_CANDIDATE_SET_MISMATCH"));

        Reject([a], joined);
        Reject([a, b], [joined[0]]);
        Reject([b, a], joined);
        Reject([Guid.Empty, b], joined);
        Reject([a, b], [joined[0], joined[0]]);
        // Matching lists that each repeat a UUID must not be accepted as a set.
        Reject([a, a], [joined[0], joined[0]]);
    }

    [Test]
    public void Malformed_frozen_universe_fails_before_any_SQL_candidate_query()
    {
        using var closed = new SqlConnection();
        async Task ExecuteAsync(IReadOnlyList<V9FrozenRunObservation> rows) =>
            await V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), Guid.NewGuid(), rows, 10, CancellationToken.None);

        foreach (var bad in new IReadOnlyList<V9FrozenRunObservation>[]
        {
            [Row(2), Row(1)],
            [Row(1), Row(1)],
            [Row(0)],
            [Row(1, "12345678901")],
            [Row(1) with
            {
                Observation = Row(1).Observation with
                {
                    ResolutionAttributes =
                        [new IdentityResolutionAttributeValue("RG", "DOC_NAO_AUTENTICADO")]
                }
            }]
        })
            Assert.That(Assert.ThrowsAsync<InvalidDataException>(
                (Func<Task>)(() => ExecuteAsync(bad)))!.Message,
                Is.EqualTo("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID"));

        Assert.That(closed.State, Is.EqualTo(System.Data.ConnectionState.Closed));
    }

    [Test]
    public void Frozen_Silver_and_manager_must_match_its_real_run_item_identity()
    {
        var frozen = Row(1);
        var original = frozen.Observation;
        Assert.That(V9ControlledDevOperationalCandidateSource.VerifyFrozenSilverObservation(
                frozen, 1, "GESTOR_A", original), Is.EqualTo("GESTOR_A"));

        void Reject(long id, string? manager, IdentityObservation observed) =>
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource.VerifyFrozenSilverObservation(
                    frozen, id, manager, observed)),
                Throws.TypeOf<InvalidDataException>()
                    .With.Message.EqualTo("RUNNER_V9_DEV_SILVER_OR_MANAGER_DRIFT"));

        Reject(2, "GESTOR_A", original); // identity mismatch
        Reject(1, null, original);       // absent manager
        Reject(1, " ", original);        // blank manager
        Reject(1, "GESTOR_A", original with { NomeCompleto = "DIVERGENTE" });
        Reject(1, "GESTOR_A", original with { NomeMae = "DIVERGENTE" });
        Reject(1, "GESTOR_A", original with { DataNascimento = new DateOnly(2001, 1, 1) });
        Reject(1, "GESTOR_A", original with { Cpf = "12345678901" });
        Reject(1, "GESTOR_A", original with { CpfAusenteMotivo = "DIVERGENTE" });
    }


    [Test]
    public void Only_physically_existing_active_RESOLVIDO_links_can_bind_a_Gold_uuid()
    {
        var realGold = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var frozen = new HashSet<long> { 1, 2 };
        var verified = new Dictionary<long, Guid>();
        var seenActive = new HashSet<long>();

        // Legitimate pending observations remain unbound; a hit in blocking
        // would not change this result or permit a fabricated Gold destination.
        V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
            verified, seenActive, frozen, 2, "NAO_RESOLVIDO", null, null);
        V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
            verified, seenActive, frozen, 1, "RESOLVIDO", realGold, "REFERENCIA");
        Assert.That(verified.Count, Is.EqualTo(1));
        Assert.That(verified[1], Is.EqualTo(realGold));
        Assert.That(verified.ContainsKey(2), Is.False);

        void Reject(long observationId, string? status, Guid? uuid, string? goldState,
            bool repeated = false)
        {
            var state = repeated
                ? new Dictionary<long, Guid> { [1] = realGold }
                : new Dictionary<long, Guid>();
            var seen = repeated ? new HashSet<long> { 1 } : new HashSet<long>();
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
                    state, seen, frozen, observationId, status, uuid, goldState)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(
                    "RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED"));
        }

        Reject(3, "RESOLVIDO", realGold, "REFERENCIA"); // outside the run
        Reject(1, null, realGold, "REFERENCIA");
        Reject(1, "UNKNOWN", realGold, "REFERENCIA");
        Reject(1, "RESOLVIDO", null, "REFERENCIA");
        Reject(1, "RESOLVIDO", Guid.Empty, "REFERENCIA");
        Reject(1, "RESOLVIDO", realGold, null);
        Reject(1, "RESOLVIDO", realGold, "PROVISORIA");
        Reject(1, "RESOLVIDO", realGold, "INDEFINIDA");
        Reject(1, "NAO_RESOLVIDO", realGold, null);
        Reject(1, "CONFLITO", null, "REFERENCIA");
        Reject(1, "RESOLVIDO", realGold, "REFERENCIA", repeated: true);
    }


    [Test]
    public void Two_active_source_links_for_one_observation_are_ambiguous_even_when_only_one_is_resolved()
    {
        // Real schema indexes vinculo_fonte( ... ativo=1 ) non-uniquely.
        // Before the fix, NAO_RESOLVIDO followed by RESOLVIDO could
        // silently become an authenticated Gold link.
        var gold = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var frozen = new HashSet<long> { 1L };
        void RejectSecond(
            string firstStatus, Guid? firstUuid, string? firstGold,
            string secondStatus, Guid? secondUuid, string? secondGold)
        {
            var links = new Dictionary<long, Guid>();
            var seen = new HashSet<long>();
            V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
                links, seen, frozen, 1, firstStatus, firstUuid, firstGold);
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
                    links, seen, frozen, 1, secondStatus, secondUuid, secondGold)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(
                    "RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED"));
        }

        RejectSecond("NAO_RESOLVIDO", null, null,
            "RESOLVIDO", gold, "REFERENCIA");
        RejectSecond("RESOLVIDO", gold, "REFERENCIA",
            "NAO_RESOLVIDO", null, null);
        RejectSecond("NAO_RESOLVIDO", null, null,
            "NAO_RESOLVIDO", null, null);
        RejectSecond("CONFLITO", null, null,
            "CONFLITO", null, null);
    }


    [Test]
    public void Fully_resolved_graph_bridge_uses_only_preexisting_Gold_links_and_original_Silver()
    {
        var left = Row(1);
        var right = Row(2) with
        {
            Observation = Row(2).Observation with { NomeCompleto = "JOAO COSTA" }
        };
        var goldA = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var goldB = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var managers = new Dictionary<long, string>
            { [1] = "GESTOR_A", [2] = "GESTOR_B" };
        var originalSilver = new Dictionary<long, IdentityObservation>
            { [1] = left.Observation, [2] = right.Observation };
        var candidates = new Dictionary<long,
            IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>
            { [1] = [], [2] = [] };
        var evidence = new Dictionary<long, V9OperationalCandidateRead.Receipt>
            {
                [1] = new(0, new string('0', 64), []),
                [2] = new(0, new string('0', 64), [])
            };

        V9ControlledDevOperationalCandidateSource.VerifiedRunCandidates Receipt(
            IReadOnlyDictionary<long, Guid>? links,
            IReadOnlyDictionary<long, string>? managersOverride = null,
            IReadOnlyDictionary<long, IdentityObservation>? silverOverride = null) =>
            new(managersOverride ?? managers, candidates, evidence,
                silverOverride ?? originalSilver, links);

        var bound = V9ControlledDevOperationalCandidateSource
            .BuildFullyResolvedGraphSourcesForPhysicalReverification(
                [left, right], Receipt(new Dictionary<long, Guid>
                    { [1] = goldA, [2] = goldB }));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(bound.Select(x => x.ObservationId), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(bound.Select(x => x.GoldUuid), Is.EqualTo(new[] { goldA, goldB }));
            Assert.That(bound.Select(x => x.Manager),
                Is.EqualTo(new[] { "GESTOR_A", "GESTOR_B" }));
            Assert.That(bound[0].Observation, Is.EqualTo(left.Observation));
            Assert.That(bound[1].Observation, Is.EqualTo(right.Observation));
        }));

        void Reject(IReadOnlyList<V9FrozenRunObservation> rows,
            V9ControlledDevOperationalCandidateSource.VerifiedRunCandidates source) =>
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource
                    .BuildFullyResolvedGraphSourcesForPhysicalReverification(rows, source)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(
                    "RUNNER_V9_DEV_GRAPH_BINDING_NOT_AUTHENTICATED"));

        // A missing/null link cannot be replaced by any Gold candidate UUID
        // or by the synthetic complete-link grouping diagnostic identifier.
        Reject([left, right], Receipt(null));
        Reject([left, right], Receipt(new Dictionary<long, Guid> { [1] = goldA }));
        Reject([left, right], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [2] = Guid.Empty }));
        Reject([left, right], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [99] = goldB }));
        Reject([left, right], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [2] = goldB }, managersOverride:
            new Dictionary<long, string> { [1] = "GESTOR_A", [2] = " " }));
        Reject([left, right], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [2] = goldB }, silverOverride:
            new Dictionary<long, IdentityObservation>
            {
                [1] = left.Observation,
                [2] = right.Observation with { NomeCompleto = "NOME FALSO" }
            }));
        Reject([right, left], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [2] = goldB }));
        Reject([left, left], Receipt(new Dictionary<long, Guid>
            { [1] = goldA, [2] = goldB }));
        Reject([], Receipt(new Dictionary<long, Guid>()));
        Reject([left, right with
            { Observation = right.Observation with { Cpf = "12345678901" } }],
            Receipt(new Dictionary<long, Guid> { [1] = goldA, [2] = goldB }));
    }

    [Test]
    public void Zero_pending_DEV_run_requires_real_SQL_admission_not_a_fake_malformed_universe()
    {
        // A DEV FULL/INCREMENTAL run may legitimately have zero eligible rows.
        // Closed SQL fails at the original disposable-attempt guard, not at
        // frozen-universe validation; a positive SQL run still needs a fixture.
        using var closed = new SqlConnection();
        var rejected = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), Guid.NewGuid(),
                Array.Empty<V9FrozenRunObservation>(), 10, CancellationToken.None)));
        Assert.That(rejected!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
    }

    [Test]
    public void Missing_attempt_and_nonpositive_candidate_capacity_fail_before_SQL()
    {
        using var closed = new SqlConnection();
        var rows = new[] { Row(1) };
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.Empty, Guid.NewGuid(), rows, 10, CancellationToken.None)))!.Message,
            Is.EqualTo("RUNNER_V9_DEV_ATTEMPT_MISSING"));
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), Guid.Empty, rows, 10, CancellationToken.None)))!.Message,
            Is.EqualTo("RUNNER_V9_DEV_RUN_MISSING"));
        Assert.That(Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), Guid.NewGuid(), rows, 0, CancellationToken.None)))!.ParamName,
            Is.EqualTo("maximumCandidatesPerObservation"));
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), Guid.NewGuid(), rows, 10, CancellationToken.None)))!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
        Assert.That(closed.State, Is.EqualTo(System.Data.ConnectionState.Closed));
    }
}

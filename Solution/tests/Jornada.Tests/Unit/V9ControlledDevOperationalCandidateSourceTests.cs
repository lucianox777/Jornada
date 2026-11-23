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

        // Legitimate pending observations remain unbound; a hit in blocking
        // would not change this result or permit a fabricated Gold destination.
        V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
            verified, frozen, 2, "NAO_RESOLVIDO", null, null);
        V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
            verified, frozen, 1, "RESOLVIDO", realGold, "REFERENCIA");
        Assert.That(verified.Count, Is.EqualTo(1));
        Assert.That(verified[1], Is.EqualTo(realGold));
        Assert.That(verified.ContainsKey(2), Is.False);

        void Reject(long observationId, string? status, Guid? uuid, string? goldState,
            bool repeated = false)
        {
            var state = repeated
                ? new Dictionary<long, Guid> { [1] = realGold }
                : new Dictionary<long, Guid>();
            Assert.That((Action)(() =>
                V9ControlledDevOperationalCandidateSource.RecordExistingResolvedGoldLink(
                    state, frozen, observationId, status, uuid, goldState)),
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

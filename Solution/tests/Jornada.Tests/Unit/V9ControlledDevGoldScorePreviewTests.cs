using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

/// <summary>Only a read-only DEV scoring preview; never runner publication.</summary>
[TestFixture, Category("Unit")]
public sealed class V9ControlledDevGoldScorePreviewTests
{
    private static readonly V9OperationalPinnedCohortRuntime Winner =
        V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(
            V9FittedSnapshotTests.CompleteTf4OperationalWinner());

    private static readonly Guid GoldId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static V9FrozenRunObservation SourceRow(long id = 1) =>
        new(id, new IdentityObservation(null, "SEM_CPF", "MARIA SILVA",
            new DateOnly(1980, 5, 6), "ANA COSTA"));

    private static V9ControlledDevOperationalCandidateSource.VerifiedRunCandidates Source(
        IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate> gold,
        IReadOnlyList<Guid>? receiptIds = null, long? count = null,
        string? manager = "GESTOR_A", bool includeEvidence = true)
    {
        receiptIds ??= gold.Select(g => g.PessoaUuid).ToArray();
        var bytes = Encoding.UTF8.GetBytes(string.Concat(
            receiptIds.Select(id => id.ToString("D") + "\n")));
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(
            new Dictionary<long, string> { [1] = manager! },
            new Dictionary<long, IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>
                { [1] = gold },
            includeEvidence
                ? new Dictionary<long, V9OperationalCandidateRead.Receipt>
                    { [1] = new(count ?? receiptIds.Count, digest, receiptIds) }
                : new Dictionary<long, V9OperationalCandidateRead.Receipt>());
    }

    private static V9ControlledDevOperationalCandidateSource.GoldCandidate Candidate() =>
        new(GoldId, "MARIA SILVA", new DateOnly(1980, 5, 6), "ANA COSTA");

    [Test]
    public void Real_Gold_UUID_preview_reuses_exact_unchanged_frozen_FS_and_Resolve()
    {
        var row = SourceRow();
        var snapshotCandidate = new V9SnapshotCandidate(
            GoldId, "MARIA SILVA", new DateOnly(1980, 5, 6), "ANA COSTA");
        var expected = Winner.FrozenWinner.ResolvePreview(
            Winner.ModelId, row.Observation, [snapshotCandidate]);
        var scored = V9ControlledDevGoldScorePreview.Score(
            Winner, [row], Source([Candidate()]));

        Assert.That(scored.WinnerSha256, Is.EqualTo(Winner.WinnerSha256));
        Assert.That(scored.ModelId, Is.EqualTo(Winner.ModelId));
        Assert.That(scored.Decisions.Keys, Is.EquivalentTo(new long[] { 1 }));
        Assert.That(scored.Decisions[1], Is.EqualTo(expected));
        Assert.That(scored.Decisions[1].ModeloId, Is.EqualTo(Winner.ModelId));
        Assert.That(scored.Decisions[1].MelhorCandidatoUuid,
            Is.EqualTo(expected.MelhorCandidatoUuid));
    }

    [Test]
    public void Empty_Gold_candidate_union_preserves_original_abstention()
    {
        var row = SourceRow();
        var expected = Winner.FrozenWinner.ResolvePreview(
            Winner.ModelId, row.Observation, Array.Empty<V9SnapshotCandidate>());
        var scored = V9ControlledDevGoldScorePreview.Score(
            Winner, [row], Source(Array.Empty<V9ControlledDevOperationalCandidateSource.GoldCandidate>()));
        Assert.That(scored.Decisions[1], Is.EqualTo(expected));
        Assert.That(scored.Decisions[1].Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
    }

    [Test]
    public void Tampered_candidate_receipts_and_incomplete_universe_are_rejected()
    {
        var row = SourceRow();
        void Reject(V9ControlledDevOperationalCandidateSource.VerifiedRunCandidates source,
            string reason, IReadOnlyList<V9FrozenRunObservation>? frozen = null) =>
            Assert.That((Action)(() => V9ControlledDevGoldScorePreview.Score(
                    Winner, frozen ?? [row], source)),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(reason));

        Reject(Source([Candidate()], count: 2),
            "RUNNER_V9_DEV_PREVIEW_CANDIDATE_RECEIPT_MISMATCH");
        Reject(Source([Candidate()], receiptIds: [Guid.NewGuid()]),
            "RUNNER_V9_DEV_CANDIDATE_SET_MISMATCH");
        Reject(Source([Candidate(), Candidate()]),
            "RUNNER_V9_DEV_PREVIEW_GOLD_REFERENCE_INVALID");
        Reject(Source([Candidate()], manager: " "),
            "RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH");
        Reject(Source([Candidate()], includeEvidence: false),
            "RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH");
        Reject(Source([Candidate()]),
            "RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH", [SourceRow(2)]);
        Reject(Source([Candidate()]),
            "RUNNER_V9_DEV_PREVIEW_UNIVERSE_MISMATCH", [SourceRow(), SourceRow()]);
    }
}

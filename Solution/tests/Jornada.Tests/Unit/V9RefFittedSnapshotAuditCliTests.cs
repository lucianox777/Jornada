using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9RefFittedSnapshotAuditCliTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string RefResultsSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Algorithm = LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;

    private static V9RefFittedSnapshotAuditCli.ReferenceRow Row(
        string state = "PUBLICADA", Guid? modelId = null, string? referenceAlgorithm = null,
        string? storedResultsSha = null, string? computedResultsSha = null,
        string? modelAlgorithm = null, string? modelSnapshotReference = null) =>
        new(state, modelId ?? Guid.Parse("44444444-4444-4444-8444-444444444444"),
            referenceAlgorithm ?? Algorithm, storedResultsSha ?? RefResultsSha,
            computedResultsSha ?? RefResultsSha, modelAlgorithm ?? Algorithm,
            "VALIDADO", modelSnapshotReference ?? Sha);

    [Test]
    public void Published_ref_and_model_can_match_metadata_without_certification_or_activation()
    {
        var verdict = V9RefFittedSnapshotAuditCli.Assess(Row(), Sha);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(verdict.Status, Is.EqualTo("REFERENCE_AND_MODEL_METADATA_MATCH_NOT_CERTIFIED"));
            Assert.That(verdict.PublishedReference, Is.True);
            Assert.That(verdict.SqlResultsHashVerified, Is.True);
            Assert.That(verdict.LinkedModelAlgorithmVerified, Is.True);
            Assert.That(verdict.ModelSnapshotShaMatches, Is.True);
            Assert.That(verdict.OperationallyCertified, Is.False);
            Assert.That(verdict.ActivationAllowed, Is.False);
        }));
    }

    [Test]
    public void Missing_ref_publication_and_unbound_model_fail_closed()
    {
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(Row(state: "CARREGANDO"), Sha).Status,
            Is.EqualTo("REF_NOT_PUBLISHED"));
        var unbound = Row() with { ModelId = null, ModelAlgorithm = null, ModelStatus = null, ModelSnapshotReference = null };
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(unbound, Sha).Status,
            Is.EqualTo("REF_NOT_LINKED_TO_MODEL"));
    }

    [Test]
    public void Changed_result_json_sql_hash_or_model_identity_is_not_silently_treated_as_pinned_fit()
    {
        var tamperedJson = Row(computedResultsSha: new string('f', 64));
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(tamperedJson, Sha).Status,
            Is.EqualTo("REF_RESULTS_DIGEST_MISMATCH"));
        var mismatchedAlgorithm = Row(modelAlgorithm: "V8");
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(mismatchedAlgorithm, Sha).Status,
            Is.EqualTo("MODEL_ALGORITHM_MISMATCH"));
        var mismatchedPortableFit = Row(modelSnapshotReference: new string('e', 64));
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(mismatchedPortableFit, Sha).Status,
            Is.EqualTo("PINNED_SNAPSHOT_NOT_BOUND_TO_MODEL"));
        // SQL REF hashes its UTF-16 JSON, not the portable canonical UTF-8 bytes.
        // A distinct, valid REF hash must be accepted if it is independently verified.
        Assert.That(V9RefFittedSnapshotAuditCli.Assess(Row(), Sha).SqlResultsHashVerified, Is.True);
        Assert.That(RefResultsSha, Is.Not.EqualTo(Sha));
        Assert.Throws<ArgumentException>((Action)(() => V9RefFittedSnapshotAuditCli.Assess(Row(), "bad-hash")));
    }
}

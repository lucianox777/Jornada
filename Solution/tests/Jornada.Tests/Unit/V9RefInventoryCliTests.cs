using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9RefInventoryCliTests
{
    private const string S = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string F = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string A = LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
    private static V9RefInventoryCli.Metadata Ready() => new("PUBLICADA", "SINTETICA", A,
        Guid.Parse("22222222-2222-4222-8222-222222222222"), S, S, A, F);

    [Test]
    public void Metadata_is_not_model_certification_even_when_all_declared_shas_match()
    {
        Assert.That(V9RefInventoryCli.Assess(Ready()),
            Is.EqualTo("V9_METADATA_PRESENT_FIT_SHA_NOT_YET_VERIFIED"));
        Assert.That(S, Is.Not.EqualTo(F), "SQL REF JSON digest is separate from the portable fit digest.");
    }

    [Test]
    public void Inventory_differentiates_missing_or_unpublished_ref_from_a_pinned_model()
    {
        Assert.That(V9RefInventoryCli.Assess(Ready() with { State = "CARREGANDO" }),
            Is.EqualTo("REF_NOT_PUBLISHED"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { Origin = "IBGE" }),
            Is.EqualTo("UNEXPECTED_REFERENCE_ORIGIN"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { RecomputedSqlResultsSha256 = F }),
            Is.EqualTo("REF_RESULTS_DIGEST_MISMATCH"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { StoredSqlResultsSha256 = "not-a-digest" }),
            Is.EqualTo("REF_RESULTS_DIGEST_MISMATCH"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { ReferenceAlgorithm = "V8" }),
            Is.EqualTo("NOT_V9_ALGORITHM"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { ModelId = null }),
            Is.EqualTo("REF_NOT_LINKED_TO_MODEL"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { ModelAlgorithm = "V8" }),
            Is.EqualTo("MODEL_ALGORITHM_MISMATCH"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { ModelSnapshotReference = null }),
            Is.EqualTo("NO_DIRECT_PINNED_FIT_SHA_IN_MODEL"));
        Assert.That(V9RefInventoryCli.Assess(Ready() with { ModelSnapshotReference = "some-file-path" }),
            Is.EqualTo("NO_DIRECT_PINNED_FIT_SHA_IN_MODEL"));
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class CalibrationReplayManifestTests
{
    private static readonly ResolutionProjectionPlan Projection = ResolutionProjectionPlanner.Build(
        new[] { new ResolutionSourceField("nome", ResolutionAttributeSemantic.PersonName) },
        "REPLAY_TEST_PROJECTION_V1");

    [Test]
    public void Fingerprint_IsStableWhenExternalSnapshotOrderChanges()
    {
        var person = Snapshot(CalibrationSourceKind.PersonData, "silver.pessoa", "snapshot-42", "aaa");
        var corpus = Snapshot(CalibrationSourceKind.TrainingCorpus, "linkage-m-u", "corpus-7", "bbb");
        var ibge = Snapshot(CalibrationSourceKind.ExternalReference, "ibge-nomes", "2022", "ccc", "IBGE_NAMES_PARSER_V1");
        var municipal = Snapshot(CalibrationSourceKind.ExternalReference, "referencia-municipal", "2026-09", "ddd");

        var first = CalibrationReplayManifest.Create(
            "CALIBRATOR_V1", Projection, "PLAN_V1", "plan-hash", person, corpus, new[] { ibge, municipal });
        var reordered = CalibrationReplayManifest.Create(
            "CALIBRATOR_V1", Projection, "PLAN_V1", "plan-hash", person, corpus, new[] { municipal, ibge });

        Assert.That(first.Fingerprint, Is.EqualTo(reordered.Fingerprint));
    }

    [Test]
    public void Fingerprint_ChangesWhenAnyConsumedSourceChanges()
    {
        var person = Snapshot(CalibrationSourceKind.PersonData, "silver.pessoa", "snapshot-42", "aaa");
        var corpus = Snapshot(CalibrationSourceKind.TrainingCorpus, "linkage-m-u", "corpus-7", "bbb");
        var ibgeV1 = Snapshot(CalibrationSourceKind.ExternalReference, "ibge-nomes", "2022", "ccc", "IBGE_NAMES_PARSER_V1");
        var ibgeV2 = Snapshot(CalibrationSourceKind.ExternalReference, "ibge-nomes", "2022", "changed-content", "IBGE_NAMES_PARSER_V1");

        var first = CalibrationReplayManifest.Create(
            "CALIBRATOR_V1", Projection, "PLAN_V1", "plan-hash", person, corpus, new[] { ibgeV1 });
        var changedSource = CalibrationReplayManifest.Create(
            "CALIBRATOR_V1", Projection, "PLAN_V1", "plan-hash", person, corpus, new[] { ibgeV2 });

        Assert.That(first.Fingerprint, Is.Not.EqualTo(changedSource.Fingerprint));
    }

    [Test]
    public void Manifest_CarriesAllFiveReplayDimensions()
    {
        var person = Snapshot(CalibrationSourceKind.PersonData, "silver.pessoa", "snapshot-42", "aaa");
        var corpus = Snapshot(CalibrationSourceKind.TrainingCorpus, "linkage-m-u", "corpus-7", "bbb");
        var ibge = Snapshot(CalibrationSourceKind.ExternalReference, "ibge-nomes", "2022", "ccc", "IBGE_NAMES_PARSER_V1");

        var manifest = CalibrationReplayManifest.Create(
            "CALIBRATOR_V1", Projection, "PLAN_V9", "plan-fingerprint", person, corpus, new[] { ibge });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.PersonSnapshot, Is.EqualTo(person));
            Assert.That(manifest.TrainingCorpusSnapshot, Is.EqualTo(corpus));
            Assert.That(manifest.ExternalSnapshots, Has.Count.EqualTo(1));
            Assert.That(manifest.AlgorithmCatalogVersion, Is.EqualTo(HomologatedResolutionAlgorithmCatalog.CatalogVersion));
            Assert.That(manifest.ComparatorCatalogVersion, Is.EqualTo(HomologatedResolutionComparatorCatalog.CatalogVersion));
            Assert.That(manifest.ProjectionFingerprint, Is.EqualTo(Projection.Fingerprint));
            Assert.That(manifest.BlockingPlanVersion, Is.EqualTo("PLAN_V9"));
            Assert.That(manifest.Fingerprint, Has.Length.EqualTo(64));
        });
    }

    private static CalibrationSourceSnapshot Snapshot(
        CalibrationSourceKind kind,
        string source,
        string version,
        string fingerprint,
        string? parser = null) =>
        new(kind, source, version, fingerprint, parser);
}

using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationSnapshotManifestGuardTests
{
    private static V9CalibrationSnapshotManifestGuard.Manifest Valid() => new(
        V9CalibrationSnapshotManifestGuard.Version, "FELLEGI_SUNTER_V9", "V1",
        new string('a', 64), new string('b', 64), new string('c', 64),
        new string('d', 64), new string('e', 64), true, false, false);

    [Test]
    public void Manually_Approved_Disjoint_Snapshot_Passes_Preflight()
    {
        Assert.DoesNotThrow((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(Valid())));
    }

    [Test]
    public void Rejects_Automatic_Recalibration_Test_And_Unapproved_Fit()
    {
        Assert.That((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(
            Valid() with { AutomaticRecalibrationRequested = true })), Throws.TypeOf<InvalidDataException>());
        Assert.That((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(
            Valid() with { UsesTestLabels = true })), Throws.TypeOf<InvalidDataException>());
        Assert.That((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(
            Valid() with { ManuallyApproved = false })), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Rejects_Identity_Leakage_And_Invalid_Fingerprints()
    {
        Assert.That((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(
            Valid() with { IndependentValidationGroupsSha256 = new string('d', 64) })),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((TestDelegate)(() => V9CalibrationSnapshotManifestGuard.ValidateForBootstrap(
            Valid() with { FitSha256 = "invalid" })), Throws.TypeOf<InvalidDataException>());
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9OfflineCalibratorCapacityTests
{
    private static V9PhysicalCalibrationObservation[] SharedSignatureFrame()
    {
        return Enumerable.Range(1, 6)
            .Select(i => new V9PhysicalCalibrationObservation(
                i, "BASE-" + i, i <= 3 ? "TRAIN" : "VALIDATION",
                "GESTOR-" + i, "ANA SOUZA", "MARIA SILVA", new DateOnly(1990, 1, 1)))
            .ToArray();
    }

    private static LinkageBlockingPass[] Passes() =>
    [
        LinkageBlockingPass.Create("P_FIRST_BIRTH_YEAR",
            [BlockingCandidateFeatureCatalog.FirstName,
             BlockingCandidateFeatureCatalog.BirthYear])
    ];

    private static V9PhysicalAdaptiveExecution Execution(int pairCap, int signatureCap = 10000) =>
        new(MaximumPrefixLength: 4, EndToEndRepetitions: 20,
            FsBlindGroupingOnly: true, MaximumBlindClusterSize: 16,
            MaximumCandidatePairIncidences: pairCap, MaximumProjectedSignatures: signatureCap);

    [Test]
    public void Offline_uses_real_projection_and_reports_declared_pair_incidence_cap_without_SQL()
    {
        var report = V9PhysicalSqlAdaptiveCalibrationProbe.InspectOfflinePairCapacity(
            SharedSignatureFrame(), Passes(), Execution(1));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.WithinDeclaredProjectedSignatureCap, Is.True);
            Assert.That(report.WithinDeclaredPairIncidenceCap, Is.False);
            Assert.That(report.PreflightPairIncidencesUpperBound, Is.EqualTo(2L));
            Assert.That(report.BlockingPasses, Is.EqualTo(1));
            Assert.That(report.EvidenceScope, Does.Contain("NO_SQL_NO_TEST"));
        }));
    }

    [Test]
    public void Offline_can_admit_same_frame_with_larger_predeclared_cap()
    {
        var report = V9PhysicalSqlAdaptiveCalibrationProbe.InspectOfflinePairCapacity(
            SharedSignatureFrame(), Passes(), Execution(1000));
        Assert.That(report.WithinDeclaredProjectedSignatureCap, Is.True);
        Assert.That(report.WithinDeclaredPairIncidenceCap, Is.True);
        Assert.That(report.PreflightPairIncidencesUpperBound, Is.GreaterThan(1));
    }

    [Test]
    public void Offline_reports_signature_projection_cap_without_relaxing_execution_plan()
    {
        var report = V9PhysicalSqlAdaptiveCalibrationProbe.InspectOfflinePairCapacity(
            SharedSignatureFrame(), Passes(), Execution(1000, signatureCap: 1));
        Assert.That(report.WithinDeclaredProjectedSignatureCap, Is.False);
        Assert.That(report.WithinDeclaredPairIncidenceCap, Is.False);
        Assert.That(report.SignatureRows, Is.EqualTo(2));
        Assert.That(report.DeclaredMaximumProjectedSignatures, Is.EqualTo(1));
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt15BlockingPairDiagnosticTests
{
    private static readonly string Name = BlockingFeatureNames.FullName;
    private static readonly string Mother = BlockingFeatureNames.MotherFullName;

    [Test]
    public void Compare_UsesExactlyTheSameDenominatorsAndShowsRecallCostTradeoff()
    {
        var sample = new[]
        {
            Row(true, true, false), Row(true, true, true), Row(true, false, false),
            Row(false, true, false), Row(false, true, false),
            Row(false, false, true), Row(false, false, false)
        };
        var result = Dt15BlockingPairDiagnostic.Compare(
            sample, [Pass("D-name", Name)], Active(Pass("D-mother", Mother)),
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Dt15BlockingPairDiagnostic.Comparable));
            Assert.That(result.MatchedPairWeight, Is.EqualTo(3m));
            Assert.That(result.NonMatchedPairWeight, Is.EqualTo(4m));
            Assert.That(result.Active!.TrueMatchRecall, Is.EqualTo(1d / 3d).Within(0.000001));
            Assert.That(result.Draft.TrueMatchRecall, Is.EqualTo(2d / 3d).Within(0.000001));
            Assert.That(result.Active.ReductionRatio, Is.EqualTo(0.75d));
            Assert.That(result.Draft.ReductionRatio, Is.EqualTo(0.5d));
            Assert.That(result.RecallDelta, Is.EqualTo(1d / 3d).Within(0.000001));
            Assert.That(result.ReductionDelta, Is.EqualTo(-0.25d));
        });
    }

    [Test]
    public void Compare_MissingMotherRemainsInGlobalTruthDenominator()
    {
        var sample = new[]
        {
            Row(true, true, null), Row(true, false, true),
            Row(false, false, false), Row(false, true, null)
        };
        var result = Dt15BlockingPairDiagnostic.Compare(
            sample, [Pass("name", Name)], Active(Pass("mother", Mother)),
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion);
        Assert.Multiple(() =>
        {
            Assert.That(result.MatchedPairWeight, Is.EqualTo(2m));
            Assert.That(result.Active!.TrueMatchRecall, Is.EqualTo(0.5d));
            Assert.That(result.Draft.TrueMatchRecall, Is.EqualTo(0.5d));
            Assert.That(result.Active.CompleteMatchCoverage, Is.EqualTo(0.5d));
        });
    }

    [Test]
    public void Compare_NoActiveDoesNotInventPreviousRecallOrDelta()
    {
        var result = Dt15BlockingPairDiagnostic.Compare(
            Minimal(), [Pass("name", Name)], null,
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Dt15BlockingPairDiagnostic.NoActiveModel));
            Assert.That(result.IsComparable, Is.False);
            Assert.That(result.Active, Is.Null);
            Assert.That(result.RecallDelta, Is.Null);
            Assert.That(result.ReductionDelta, Is.Null);
            Assert.That(result.Draft.TrueMatchRecall, Is.EqualTo(1d));
        });
    }

    [Test]
    public void Compare_IncompatibleVersionProjectionOrFeatureNeverProducesDelta()
    {
        var baseline = Active(Pass("mother", Mother));
        foreach (var changed in new[]
        {
            baseline with { AlgorithmVersion = "OLD_FS" },
            baseline with { NormalizationVersion = "LEGACY" },
            baseline with { ProjectionFingerprintSha256 = new string('0', 64) },
            baseline with { Passes = [Pass("external", "FIELD_NOT_IN_CALIBRATOR")] },
            baseline with { Passes = null }
        })
        {
            var result = Dt15BlockingPairDiagnostic.Compare(
                Minimal(), [Pass("name", Name)], changed,
                "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion);
            Assert.Multiple(() =>
            {
                Assert.That(result.IsComparable, Is.False);
                Assert.That(result.Active, Is.Null);
                Assert.That(result.RecallDelta, Is.Null);
            });
        }
    }

    [Test]
    public void Compare_RejectsEmptyObservationsOrMissingDraftPasses()
    {
        Assert.That(() => Dt15BlockingPairDiagnostic.Compare(
            Array.Empty<BlockingFeatureObservation>(), [Pass("name", Name)], null,
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion),
            Throws.ArgumentException);
        Assert.That(() => Dt15BlockingPairDiagnostic.Compare(
            Minimal(), Array.Empty<LinkageBlockingPass>(), null,
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion),
            Throws.ArgumentException);
    }

    private static Dt15ActiveBlockingSnapshot Active(params LinkageBlockingPass[] passes) =>
        new(Guid.Parse("d1111111-1111-4111-8111-111111111111"), 3,
            "FS_LINKAGE_V6", IdentityComparison.NormalizationVersion,
            new string('a', 64), PersonResolutionProjectionContract.SchemaVersion,
            PersonResolutionProjectionContract.FingerprintSha256, passes);

    private static BlockingFeatureObservation[] Minimal() =>
        [Row(true, true, true), Row(false, false, false)];

    private static LinkageBlockingPass Pass(string id, string field) =>
        LinkageBlockingPass.Create(id, [field]);

    private static BlockingFeatureObservation Row(bool truth, bool? name, bool? mother) =>
        new(truth, new Dictionary<string, bool?>(StringComparer.Ordinal)
        {
            [Name] = name,
            [Mother] = mother
        }, 1m);
}

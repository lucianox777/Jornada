using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IndependentEvaluationManifestTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Test]
    public void Create_IsDeterministicAndNormalizesHexCase()
    {
        var at = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var first = IndependentEvaluationManifestCatalog.Create("method-v1", "model-v1", A.ToUpperInvariant(), B, C, 100, 12, at);
        var second = IndependentEvaluationManifestCatalog.Create("method-v1", "model-v1", A, B, C, 100, 12, at);

        Assert.That(first.FingerprintSha256, Is.EqualTo(second.FingerprintSha256));
        Assert.That(first.CalibrationCorpusFingerprintSha256, Is.EqualTo(A));
        Assert.That(first.FingerprintSha256, Has.Length.EqualTo(64));
    }

    [Test]
    public void Create_RejectsSameCalibrationAndEvaluationCorpus()
    {
        Assert.That(
            () => IndependentEvaluationManifestCatalog.Create("m", "v", A, A, C, 10, 1, DateTimeOffset.UnixEpoch),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsInvalidFingerprints()
    {
        Assert.That(
            () => IndependentEvaluationManifestCatalog.Create("m", "v", "deadbeef", B, C, 10, 1, DateTimeOffset.UnixEpoch),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Create_RejectsInvalidDenominators()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => IndependentEvaluationManifestCatalog.Create("m", "v", A, B, C, 0, 0, DateTimeOffset.UnixEpoch),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => IndependentEvaluationManifestCatalog.Create("m", "v", A, B, C, 10, 11, DateTimeOffset.UnixEpoch),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Create_RequiresUtcTimestamp()
    {
        var nonUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(-3));
        Assert.That(
            () => IndependentEvaluationManifestCatalog.Create("m", "v", A, B, C, 10, 1, nonUtc),
            Throws.TypeOf<ArgumentException>());
    }
}

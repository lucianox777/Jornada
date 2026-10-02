using System.Security.Cryptography;
using System.Text;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

public sealed class Dt05ReplayManifestPublisherTests
{
    [Test]
    public void Input_snapshot_id_is_versioned_order_sensitive_and_repeatable()
    {
        var a = Dt05ReplayManifestPublisher.ComputeInputSnapshotId(42, new long[] { 1, 2, 9 });
        var b = Dt05ReplayManifestPublisher.ComputeInputSnapshotId(42, new long[] { 1, 2, 9 });
        var reordered = Dt05ReplayManifestPublisher.ComputeInputSnapshotId(42, new long[] { 2, 1, 9 });
        var otherWatermark = Dt05ReplayManifestPublisher.ComputeInputSnapshotId(43, new long[] { 1, 2, 9 });

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Does.StartWith("dt05-input-v1:sha256:"));
        Assert.That(a, Has.Length.EqualTo("dt05-input-v1:sha256:".Length + 64));
        Assert.That(reordered, Is.Not.EqualTo(a));
        Assert.That(otherWatermark, Is.Not.EqualTo(a));
    }

    [Test]
    public void Input_snapshot_id_matches_documented_v1_bytes()
    {
        var bytes = Encoding.UTF8.GetBytes("dt05-input-v1\\nhigh_watermark=7\\n10\\n20\\n");
        var expected = "dt05-input-v1:sha256:" +
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.That(Dt05ReplayManifestPublisher.ComputeInputSnapshotId(7, new long[] { 10, 20 }), Is.EqualTo(expected));
    }
    [Test]
    public void Canonical_json_sorts_object_keys_recursively_like_python_utility()
    {
        var bytes = Dt05ReplayManifestPublisher.Canonicalize(new {
            z = 1,
            a = new { y = "ç", b = true },
            list = new[] { new { d = 4, c = 3 } }
        });

        Assert.That(Encoding.UTF8.GetString(bytes),
            Is.EqualTo("{\"a\":{\"b\":true,\"y\":\"ç\"},\"list\":[{\"c\":3,\"d\":4}],\"z\":1}"));
    }
}

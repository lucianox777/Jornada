using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IbgeSnapshotChangeDetectorTests
{
    [Test]
    public void SameETag_ReusesSnapshot()
    {
        var previous = new IbgeSourceMetadata("\"abc\"", null, 100);
        var current = new IbgeSourceMetadata("\"abc\"", null, 100);

        Assert.That(IbgeSnapshotChangeDetector.Compare(previous, current),
            Is.EqualTo(IbgeSnapshotChangeStatus.Unchanged));
        Assert.That(IbgeSnapshotChangeDetector.RequiresDownload(previous, current), Is.False);
    }

    [Test]
    public void DifferentETag_RequiresDownload_EvenWhenLengthIsEqual()
    {
        var previous = new IbgeSourceMetadata("\"abc\"", null, 100);
        var current = new IbgeSourceMetadata("\"def\"", null, 100);

        Assert.That(IbgeSnapshotChangeDetector.Compare(previous, current),
            Is.EqualTo(IbgeSnapshotChangeStatus.Changed));
        Assert.That(IbgeSnapshotChangeDetector.RequiresDownload(previous, current), Is.True);
    }

    [Test]
    public void SameLengthAlone_IsIndeterminate_NotProofOfIdentity()
    {
        var previous = new IbgeSourceMetadata(null, null, 100);
        var current = new IbgeSourceMetadata(null, null, 100);

        Assert.That(IbgeSnapshotChangeDetector.Compare(previous, current),
            Is.EqualTo(IbgeSnapshotChangeStatus.Indeterminate));
        Assert.That(IbgeSnapshotChangeDetector.RequiresDownload(previous, current), Is.True);
    }

    [Test]
    public void DifferentLength_RequiresDownload()
    {
        var previous = new IbgeSourceMetadata(null, null, 100);
        var current = new IbgeSourceMetadata(null, null, 101);

        Assert.That(IbgeSnapshotChangeDetector.Compare(previous, current),
            Is.EqualTo(IbgeSnapshotChangeStatus.Changed));
    }
}

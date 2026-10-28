using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Integration;

[TestFixture, Category("LinkageV9")]
public sealed class V9JointThresholdGridTests
{
    private static readonly string Snapshot = new('a', 64);
    private static readonly string Train = new('b', 64);

    [Test]
    public void Preregistration_is_order_independent_and_contains_every_pair()
    {
        var first = V9JointThresholdGrid.Preregister(Snapshot, Train,
            new[] { .05m, .10m }, new[] { .80m, .90m });
        var second = V9JointThresholdGrid.Preregister(Snapshot, Train,
            new[] { .10m, .05m }, new[] { .90m, .80m });
        Assert.Multiple((Action)(() =>
        {
            Assert.That(first.Pairs, Has.Count.EqualTo(4));
            Assert.That(first.GridSha256, Is.EqualTo(second.GridSha256));
            Assert.That(first.DeclarationSha256, Is.EqualTo(second.DeclarationSha256));
            Assert.That(first.UsesTestForSelection, Is.False);
            Assert.That(first.PermitsAutomaticActivation, Is.False);
        }));
    }

    [Test]
    public void Invalid_or_ambiguous_grids_are_rejected()
    {
        Assert.Throws<InvalidDataException>((TestDelegate)(() =>
            V9JointThresholdGrid.Preregister(Snapshot, Train,
                new[] { .85m }, new[] { .80m, .90m })));
        Assert.Throws<InvalidDataException>((TestDelegate)(() =>
            V9JointThresholdGrid.Preregister(Snapshot, Train,
                new[] { .05m, .05m }, new[] { .90m })));
        Assert.Throws<InvalidDataException>((TestDelegate)(() =>
            V9JointThresholdGrid.Preregister(Snapshot, Train,
                new[] { 0m }, new[] { .90m })));
        Assert.Throws<InvalidDataException>((TestDelegate)(() =>
            V9JointThresholdGrid.Preregister("invalid", Train,
                new[] { .05m }, new[] { .90m })));
    }

    [Test]
    public void Frozen_evidence_is_bound_to_declaration()
    {
        var first = V9JointThresholdGrid.Preregister(Snapshot, Train,
            new[] { .05m }, new[] { .90m });
        var changed = V9JointThresholdGrid.Preregister(Snapshot, new string('c', 64),
            new[] { .05m }, new[] { .90m });
        Assert.That(first.GridSha256, Is.EqualTo(changed.GridSha256));
        Assert.That(first.DeclarationSha256, Is.Not.EqualTo(changed.DeclarationSha256));
    }
}

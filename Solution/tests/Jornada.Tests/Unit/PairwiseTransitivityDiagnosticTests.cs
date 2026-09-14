using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PairwiseTransitivityDiagnosticTests
{
    [Test]
    public void Reports_rejected_closure_without_applying_transitive_merge()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var report = PairwiseTransitivityDiagnostic.Analyze(new[]
        {
            new PairwiseLinkageDecision(a, b, PairwiseLinkageDecisionState.Accepted, 0.99m),
            new PairwiseLinkageDecision(b, c, PairwiseLinkageDecisionState.Accepted, 0.98m),
            new PairwiseLinkageDecision(a, c, PairwiseLinkageDecisionState.Rejected, 0.20m)
        });

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(PairwiseTransitivityDiagnostic.Version));
            Assert.That(report.NodeCount, Is.EqualTo(3));
            Assert.That(report.AcceptedEdges, Is.EqualTo(2));
            Assert.That(report.MultiMemberComponents, Is.EqualTo(1));
            Assert.That(report.OpenWedges, Has.Count.EqualTo(1));
            Assert.That(report.RejectedClosures, Is.EqualTo(1));
            Assert.That(report.MissingClosures, Is.Zero);
            Assert.That(report.OpenWedges[0].BridgeId, Is.EqualTo(b));
        });
    }

    [Test]
    public void Reports_missing_closure_as_unobserved_not_rejected()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000013");

        var report = PairwiseTransitivityDiagnostic.Analyze(new[]
        {
            new PairwiseLinkageDecision(a, b, PairwiseLinkageDecisionState.Accepted),
            new PairwiseLinkageDecision(b, c, PairwiseLinkageDecisionState.Accepted)
        });

        Assert.Multiple(() =>
        {
            Assert.That(report.OpenWedges, Has.Count.EqualTo(1));
            Assert.That(report.MissingClosures, Is.EqualTo(1));
            Assert.That(report.RejectedClosures, Is.Zero);
            Assert.That(report.OpenWedges[0].ClosureDecision, Is.Null);
        });
    }

    [Test]
    public void Closed_triangle_has_no_transitivity_gap()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000021");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000022");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000023");

        var report = PairwiseTransitivityDiagnostic.Analyze(new[]
        {
            new PairwiseLinkageDecision(a, b, PairwiseLinkageDecisionState.Accepted),
            new PairwiseLinkageDecision(b, c, PairwiseLinkageDecisionState.Accepted),
            new PairwiseLinkageDecision(a, c, PairwiseLinkageDecisionState.Accepted)
        });

        Assert.Multiple(() =>
        {
            Assert.That(report.AcceptedEdges, Is.EqualTo(3));
            Assert.That(report.Components.Single().Members, Has.Count.EqualTo(3));
            Assert.That(report.OpenWedges, Is.Empty);
        });
    }

    [Test]
    public void Duplicate_or_self_pairs_are_rejected()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000031");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000032");

        Assert.Multiple(() =>
        {
            Assert.That(() => PairwiseTransitivityDiagnostic.Analyze(new[]
            {
                new PairwiseLinkageDecision(a, a, PairwiseLinkageDecisionState.Accepted)
            }), Throws.ArgumentException);

            Assert.That(() => PairwiseTransitivityDiagnostic.Analyze(new[]
            {
                new PairwiseLinkageDecision(a, b, PairwiseLinkageDecisionState.Accepted),
                new PairwiseLinkageDecision(b, a, PairwiseLinkageDecisionState.Rejected)
            }), Throws.InvalidOperationException);
        });
    }
}

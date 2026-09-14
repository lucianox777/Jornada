using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ConservativePairwiseGroupingPolicyTests
{
    [Test]
    public void Closed_triangle_is_eligible_without_transitive_inference()
    {
        var a = Id(1);
        var b = Id(2);
        var c = Id(3);

        var report = ConservativePairwiseGroupingPolicy.Evaluate(new[]
        {
            Accepted(a, b, 0.99m),
            Accepted(b, c, 0.98m),
            Accepted(a, c, 0.97m)
        });

        var component = report.Components.Single(x => x.Members.Count == 3);
        Assert.Multiple(() =>
        {
            Assert.That(report.PolicyVersion, Is.EqualTo(ConservativePairwiseGroupingPolicy.Version));
            Assert.That(component.State, Is.EqualTo(PairwiseGroupingState.Eligible));
            Assert.That(component.RequiredPairs, Is.EqualTo(3));
            Assert.That(component.AcceptedPairs, Is.EqualTo(3));
            Assert.That(component.RejectedPairs, Is.Zero);
            Assert.That(component.InconclusivePairs, Is.Zero);
            Assert.That(component.MissingPairs, Is.Zero);
            Assert.That(component.Reason, Is.EqualTo("COMPLETE_LINK_TODOS_PARES_ACEITOS"));
        });
    }

    [Test]
    public void Accepted_chain_with_rejected_closure_is_ambiguous()
    {
        var a = Id(11);
        var b = Id(12);
        var c = Id(13);

        var report = ConservativePairwiseGroupingPolicy.Evaluate(new[]
        {
            Accepted(a, b),
            Accepted(b, c),
            new PairwiseLinkageDecision(a, c, PairwiseLinkageDecisionState.Rejected, 0.10m)
        });

        var component = report.Components.Single(x => x.Members.Count == 3);
        Assert.Multiple(() =>
        {
            Assert.That(component.State, Is.EqualTo(PairwiseGroupingState.Ambiguous));
            Assert.That(component.AcceptedPairs, Is.EqualTo(2));
            Assert.That(component.RejectedPairs, Is.EqualTo(1));
            Assert.That(component.MissingPairs, Is.Zero);
            Assert.That(component.Reason, Is.EqualTo("AMBIGUO_FECHAMENTO_REJEITADO"));
        });
    }

    [Test]
    public void Accepted_chain_with_missing_closure_is_ambiguous()
    {
        var a = Id(21);
        var b = Id(22);
        var c = Id(23);

        var report = ConservativePairwiseGroupingPolicy.Evaluate(new[]
        {
            Accepted(a, b),
            Accepted(b, c)
        });

        var component = report.Components.Single(x => x.Members.Count == 3);
        Assert.Multiple(() =>
        {
            Assert.That(component.State, Is.EqualTo(PairwiseGroupingState.Ambiguous));
            Assert.That(component.MissingPairs, Is.EqualTo(1));
            Assert.That(component.Reason, Is.EqualTo("AMBIGUO_FECHAMENTO_NAO_OBSERVADO"));
        });
    }

    [Test]
    public void Accepted_chain_with_inconclusive_closure_is_ambiguous()
    {
        var a = Id(31);
        var b = Id(32);
        var c = Id(33);

        var report = ConservativePairwiseGroupingPolicy.Evaluate(new[]
        {
            Accepted(a, b),
            Accepted(b, c),
            new PairwiseLinkageDecision(a, c, PairwiseLinkageDecisionState.Inconclusive, 0.70m)
        });

        var component = report.Components.Single(x => x.Members.Count == 3);
        Assert.Multiple(() =>
        {
            Assert.That(component.State, Is.EqualTo(PairwiseGroupingState.Ambiguous));
            Assert.That(component.InconclusivePairs, Is.EqualTo(1));
            Assert.That(component.Reason, Is.EqualTo("AMBIGUO_FECHAMENTO_INCONCLUSIVO"));
        });
    }

    [Test]
    public void Result_and_evidence_fingerprint_are_invariant_to_input_order_and_pair_orientation()
    {
        var a = Id(41);
        var b = Id(42);
        var c = Id(43);
        var first = new[]
        {
            Accepted(a, b, 0.990m),
            Accepted(b, c, 0.980m),
            Accepted(a, c, 0.970m)
        };
        var second = new[]
        {
            Accepted(c, a, 0.97m),
            Accepted(c, b, 0.98m),
            Accepted(b, a, 0.99m)
        };

        var left = ConservativePairwiseGroupingPolicy.Evaluate(first);
        var right = ConservativePairwiseGroupingPolicy.Evaluate(second);

        Assert.Multiple(() =>
        {
            Assert.That(left.EvidenceFingerprintSha256, Is.EqualTo(right.EvidenceFingerprintSha256));
            Assert.That(left.Components.Select(ComponentShape), Is.EqualTo(right.Components.Select(ComponentShape)));
        });
    }

    [Test]
    public void Pair_is_eligible_but_rejected_only_nodes_do_not_form_composition()
    {
        var a = Id(51);
        var b = Id(52);
        var c = Id(53);
        var d = Id(54);

        var report = ConservativePairwiseGroupingPolicy.Evaluate(new[]
        {
            Accepted(a, b),
            new PairwiseLinkageDecision(c, d, PairwiseLinkageDecisionState.Rejected, 0.02m)
        });

        Assert.Multiple(() =>
        {
            Assert.That(report.EligibleComponents, Is.EqualTo(1));
            Assert.That(report.Components.Single(x => x.Members.Contains(a)).State, Is.EqualTo(PairwiseGroupingState.Eligible));
            Assert.That(report.Components.Where(x => x.Members.Contains(c) || x.Members.Contains(d))
                .All(x => x.State == PairwiseGroupingState.NoComposition), Is.True);
        });
    }

    private static PairwiseLinkageDecision Accepted(Guid left, Guid right, decimal score = 0.99m) =>
        new(left, right, PairwiseLinkageDecisionState.Accepted, score);

    private static string ComponentShape(PairwiseGroupingComponentAssessment component) =>
        $"{component.State}:{string.Join(",", component.Members.Order())}:{component.AcceptedPairs}:{component.RejectedPairs}:{component.InconclusivePairs}:{component.MissingPairs}";

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}

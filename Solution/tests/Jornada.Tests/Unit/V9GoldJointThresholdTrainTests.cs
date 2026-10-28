using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldJointThresholdTrainTests
{
    private static readonly string Fit = new('a', 64);
    private static readonly string Snapshot = new('b', 64);
    private static readonly string Plan = new('c', 64);
    private static readonly string Run = new('d', 64);

    private static V9LowerNonMatchTrainCalibrator.JointMeasurement[] Measurements()
    {
        var result = new List<V9LowerNonMatchTrainCalibrator.JointMeasurement>();
        foreach (var upper in new[] { .8m, .9m })
        foreach (var lower in new[] { .1m, .2m })
        foreach (var order in V9LowerNonMatchTrainCalibrator.JointOrders)
            result.Add(new(order, "TRAIN_SELECTION", Fit, Snapshot, Run,
                upper, lower, 30, 100, upper == .8m ? 1 : 0,
                lower == .2m ? 2 : 1, lower == .2m ? 5 : 10, .8m,
                true, true, true, true));
        return result.ToArray();
    }

    private static V9LowerNonMatchTrainCalibrator.JointPolicy Policy(
        IReadOnlyList<V9LowerNonMatchTrainCalibrator.JointMeasurement> rows)
        => new(Fit, Snapshot,
            V9LowerNonMatchTrainCalibrator.JointEvidenceFingerprint(rows),
            V9LowerNonMatchTrainCalibrator.JointGridFingerprint(
                [.8m, .9m], [.1m, .2m]), Plan,
            [.8m, .9m], [.1m, .2m], 0m, 10, true, false);

    [Test]
    public void Produces_both_thresholds_from_full_three_order_train_grid_only()
    {
        var rows = Measurements();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(rows), rows);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(study.Candidates, Has.Count.EqualTo(4));
            Assert.That(study.SuggestedUpperThreshold, Is.EqualTo(.9m));
            Assert.That(study.SuggestedLowerThreshold, Is.EqualTo(.1m));
            Assert.That(study.Candidates.Count(x => x.IsParetoFrontier), Is.GreaterThan(0));
            Assert.That(study.TestUsed, Is.False);
            Assert.That(study.OperationalActivationAllowed, Is.False);
            Assert.That(study.StatisticallyCertified, Is.False);
        }));
    }

    [Test]
    public void Rejects_test_leakage_missing_order_and_stale_evidence()
    {
        var rows = Measurements();
        var policy = Policy(rows);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(policy, rows[..^1])));
        var testRows = rows.ToArray();
        testRows[0] = testRows[0] with { Partition = "TEST" };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(
                policy with { TrainEvidenceSha256 =
                    V9LowerNonMatchTrainCalibrator.JointEvidenceFingerprint(testRows) }, testRows)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(
                policy with { DeclaredGridSha256 = new string('f', 64) }, rows)));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(policy with { TestUsed = true }, rows)));
    }

    [Test]
    public void Rejects_unrecomputed_upper_incomplete_search_and_invalid_pairs()
    {
        var rows = Measurements();
        var changed = rows.ToArray();
        changed[0] = changed[0] with { UpperRecomputedByPhysicalRankResolve = false };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
        changed = rows.ToArray();
        changed[0] = changed[0] with { CompletePhysicalCandidateSearch = false };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(
                Policy(rows) with { CandidateLowerThresholds = [.1m, .85m] }, rows)));
    }

    [Test]
    public void Predeclared_observed_risk_bound_is_not_an_activation_gate()
    {
        var rows = Measurements();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(
            Policy(rows) with { MaximumObservedFalseLinkFraction = .02m }, rows);
        Assert.That(study.Candidates.Count(x => x.MeetsPredeclaredObservedRiskConstraint),
            Is.EqualTo(4));
        Assert.That(study.StatisticallyCertified, Is.False);
        Assert.That(study.OperationalActivationAllowed, Is.False);
    }
}

using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldJointThresholdTrainTests
{
    private static readonly string Fit = new('a', 64);
    private static readonly string Snapshot = new('b', 64);
    private static readonly string Plan = new('c', 64);
    private static string RunHash(int index) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"JOINT-TRAIN-RUN-{index}")))
            .ToLowerInvariant();

    private static V9LowerNonMatchTrainCalibrator.JointMeasurement[] Measurements()
    {
        var result = new List<V9LowerNonMatchTrainCalibrator.JointMeasurement>();
        var runIndex = 0;
        foreach (var upper in new[] { .8m, .9m })
        foreach (var lower in new[] { .1m, .2m })
        foreach (var order in V9LowerNonMatchTrainCalibrator.JointOrders)
            result.Add(new(order, "TRAIN_SELECTION", Fit, Snapshot, RunHash(++runIndex),
                upper, lower, 30, 100, upper == .8m ? 1 : 0, 100,
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
            [.8m, .9m], [.1m, .2m], 10, true, false);

    [Test]
    public void Joint_orders_match_actual_four_wave_gold_runner_protocol()
    {
        Assert.That(V9LowerNonMatchTrainCalibrator.JointOrders,
            Is.EqualTo(GoldTrainThreeOrderProtocol.OrderIds),
            "Joint calibration and Gold Runner must use the same canonical order IDs.");
        foreach (var order in V9LowerNonMatchTrainCalibrator.JointOrders)
        {
            var waveOrder = GoldTrainThreeOrderProtocol.ResolveOrder(
                order, ["G0", "G1", "G2", "G3"]);
            Assert.That(waveOrder, Has.Count.EqualTo(4));
        }
    }

    [Test]
    public void Rejects_abbreviated_order_alias_even_when_fingerprint_is_recomputed()
    {
        var rows = Measurements();
        var changed = rows.ToArray();
        changed[0] = changed[0] with { OrderId = "G0_G3" };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
    }

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
            Assert.That(study.UpperCandidates, Has.Count.EqualTo(2));
            Assert.That(study.UpperCandidates.Single(x => x.IsParetoFrontier).UpperThreshold, Is.EqualTo(.9m));
            Assert.That(study.LowerCandidates, Has.Count.EqualTo(2));
            Assert.That(study.DescriptiveLowerFinalists, Does.Contain(.1m));
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
    public void Rejects_reused_physical_run_even_when_evidence_fingerprint_is_recomputed()
    {
        var rows = Measurements();
        var duplicated = rows.ToArray();
        duplicated[1] = duplicated[1] with { PhysicalRunSha256 = duplicated[0].PhysicalRunSha256 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(duplicated), duplicated)));
    }

    [Test]
    public void Preserves_nonzero_observed_false_links_without_preset_risk_veto()
    {
        var rows = Measurements();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(rows), rows);
        Assert.That(study.Candidates, Has.Count.EqualTo(4));
        Assert.That(study.Candidates.Where(x => x.UpperThreshold == .8m)
            .All(x => x.WorstObservedFalseLinkFraction == .01m), Is.True);
        Assert.That(study.Candidates.Any(x => x.FalseLinksAcrossOrders > 0), Is.True);
        Assert.That(study.StatisticallyCertified, Is.False);
        Assert.That(study.OperationalActivationAllowed, Is.False);
    }

    [Test]
    public void Upper_selection_ignores_four_objective_creation_tradeoffs()
    {
        var rows = Measurements().Select(x => x.UpperThreshold == .8m
            ? x with { FalseNewReferences = 0, PendingAfter = 0, BCubedF1 = 1m }
            : x with { FalseNewReferences = 100, PendingAfter = 100, BCubedF1 = .2m })
            .ToArray();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(rows), rows);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(study.SuggestedUpperThreshold, Is.EqualTo(.9m),
                "Upper is selected from recall and FDR, never from Gold creation/pending.");
            Assert.That(study.LowerCandidates.All(x => x.FalseNewReferencesAcrossOrders == 300), Is.True);
            Assert.That(study.UpperCandidates.Single(x => x.UpperThreshold == .9m).WorstObservedRecall,
                Is.EqualTo(1m));
        }));
    }

    [Test]
    public void Rejects_missing_or_inconsistent_recall_denominator()
    {
        var rows = Measurements();
        var changed = rows.ToArray();
        changed[0] = changed[0] with { TrueLinkOpportunities = 0 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
        changed = rows.ToArray();
        changed[0] = changed[0] with { TrueLinkOpportunities = 1 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
    }

    [Test]
    public void Fdr_undefined_for_all_cells_of_upper_cannot_be_treated_as_zero()
    {
        var rows = Measurements().Select(x => x.UpperThreshold == .9m
            ? x with { AcceptedLinks = 0, FalseLinks = 0 } : x).ToArray();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(rows), rows);
        Assert.That(study.UpperCandidates.Single(x => x.UpperThreshold == .9m).WorstObservedFdr, Is.Null);
        Assert.That(study.SuggestedUpperThreshold, Is.EqualTo(.8m));
    }

    [Test]
    public void Rejects_threshold_dependent_truth_denominator_with_valid_recomputed_evidence_sha()
    {
        var rows = Measurements();
        var changed = rows.ToArray();
        changed[0] = changed[0] with { TrueLinkOpportunities = 101 };
        // The fingerprint is deliberately recomputed: provenance hashing alone
        // must not make incomparable recall denominators acceptable.
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
    }

    [Test]
    public void Rejects_threshold_dependent_independent_group_support_in_same_order()
    {
        var rows = Measurements();
        var changed = rows.ToArray();
        changed[0] = changed[0] with { IndependentIdentityGroups = 31 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(changed), changed)));
    }

    [Test]
    public void Undefined_observed_rate_is_not_invented_when_no_links_accepted()
    {
        var rows = Measurements().Select(x => x.UpperThreshold == .9m
            ? x with { AcceptedLinks = 0, FalseLinks = 0 } : x).ToArray();
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(Policy(rows), rows);
        Assert.That(study.Candidates.Where(x => x.UpperThreshold == .9m)
            .All(x => x.WorstObservedFalseLinkFraction is null
                && x.UndefinedObservedFalseLinkRateOrders == 3), Is.True);
        Assert.That(study.Candidates, Has.Count.EqualTo(4));
    }
}

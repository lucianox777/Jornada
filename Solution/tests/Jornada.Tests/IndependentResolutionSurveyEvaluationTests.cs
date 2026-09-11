using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IndependentResolutionSurveyEvaluationTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 11, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public void Evaluate_ComputesWeightedMetricsAndClusterJackknife()
    {
        var report = IndependentResolutionSurveyEvaluator.Evaluate(
            Manifest(candidatePairs: 7, referenceLinks: 3),
            SurveyRows(),
            threshold: 0.95m,
            conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(IndependentResolutionSurveyEvaluator.Version));
            Assert.That(report.SamplingDesignFingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(report.FingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(report.Overall.Observations, Is.EqualTo(4));
            Assert.That(report.Overall.IndependentGroups, Is.EqualTo(4));
            Assert.That(report.Overall.ObservationWeight, Is.EqualTo(10m));
            Assert.That(report.Overall.ReferenceWeight, Is.EqualTo(7m));
            Assert.That(report.Overall.ResolvedWeight, Is.EqualTo(3m));
            Assert.That(report.Overall.ConflictWeight, Is.EqualTo(3m));
            Assert.That(report.Overall.UnresolvedWeight, Is.EqualTo(4m));
            Assert.That(report.Overall.TrueLinkWeight, Is.EqualTo(2m));
            Assert.That(report.Overall.FalseLinkWeight, Is.EqualTo(1m));
            Assert.That(report.Overall.MissedLinkWeight, Is.EqualTo(5m));
            Assert.That(report.Overall.TrueNonLinkWeight, Is.EqualTo(3m));
            Assert.That(report.Overall.RecoveredReferenceWeight, Is.EqualTo(3m));
            Assert.That(report.Overall.Recall, Is.EqualTo(2m / 7m));
            Assert.That(report.Overall.Precision, Is.EqualTo(2m / 3m));
            Assert.That(report.Overall.FalseLinkRate, Is.EqualTo(1m / 3m));
            Assert.That(report.Overall.FalsePositiveRate, Is.EqualTo(0.25m));
            Assert.That(report.Overall.ResolutionRate, Is.EqualTo(0.3m));
            Assert.That(report.Overall.CandidateRecoveryRate, Is.EqualTo(3m / 7m));
            Assert.That(report.Overall.TopCandidateBrierScore, Is.EqualTo(0.62665m));
            Assert.That(report.OverallUncertainty, Has.Count.EqualTo(7));
        });

        var recall = report.OverallUncertainty.Single(interval => interval.Metric == "RECALL");
        Assert.Multiple(() =>
        {
            Assert.That(recall.Estimate, Is.EqualTo(report.Overall.Recall));
            Assert.That(recall.StandardError, Is.GreaterThan(0m));
            Assert.That(recall.Lower95, Is.InRange(0m, 1m));
            Assert.That(recall.Upper95, Is.InRange(0m, 1m));
            Assert.That(recall.IndependentGroups, Is.EqualTo(4));
            Assert.That(recall.Replicates, Is.EqualTo(4));
        });

        var highBin = report.Calibration.Single(bin => bin.LowerInclusivePercent == 90);
        Assert.Multiple(() =>
        {
            Assert.That(highBin.Observations, Is.EqualTo(3));
            Assert.That(highBin.DesignWeight, Is.EqualTo(6m));
            Assert.That(highBin.MeanPredictedProbability, Is.EqualTo(5.81m / 6m));
            Assert.That(highBin.ObservedMatchRate, Is.EqualTo(1m / 3m));
        });
    }

    [Test]
    public void Evaluate_IsDeterministicAcrossInputOrdering()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var firstRows = SurveyRows();
        var secondRows = firstRows
            .Reverse()
            .Select(row => row with
            {
                Observation = row.Observation with
                {
                    Candidates = row.Observation.Candidates.Reverse().ToArray()
                }
            })
            .ToArray();

        var first = IndependentResolutionSurveyEvaluator.Evaluate(manifest, firstRows, 0.95m, 0.03m);
        var second = IndependentResolutionSurveyEvaluator.Evaluate(manifest, secondRows, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(first.SamplingDesignFingerprintSha256, Is.EqualTo(second.SamplingDesignFingerprintSha256));
            Assert.That(first.FingerprintSha256, Is.EqualTo(second.FingerprintSha256));
        });
    }

    [Test]
    public void Evaluate_DesignFingerprintChangesWithWeightOrCluster()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var baseline = SurveyRows();
        var changedWeight = baseline.ToArray();
        changedWeight[0] = changedWeight[0] with { DesignWeight = 2.5m };
        var changedCluster = baseline.ToArray();
        changedCluster[0] = changedCluster[0] with { IndependenceGroupFingerprintSha256 = Hash("e") };

        var a = IndependentResolutionSurveyEvaluator.Evaluate(manifest, baseline, 0.95m, 0.03m);
        var b = IndependentResolutionSurveyEvaluator.Evaluate(manifest, changedWeight, 0.95m, 0.03m);
        var c = IndependentResolutionSurveyEvaluator.Evaluate(manifest, changedCluster, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(b.SamplingDesignFingerprintSha256, Is.Not.EqualTo(a.SamplingDesignFingerprintSha256));
            Assert.That(c.SamplingDesignFingerprintSha256, Is.Not.EqualTo(a.SamplingDesignFingerprintSha256));
            Assert.That(b.FingerprintSha256, Is.Not.EqualTo(a.FingerprintSha256));
            Assert.That(c.FingerprintSha256, Is.Not.EqualTo(a.FingerprintSha256));
        });
    }

    [Test]
    public void Evaluate_SubgroupUsesClusterAwareUncertaintyWhenEnoughGroupsExist()
    {
        var report = IndependentResolutionSurveyEvaluator.Evaluate(
            Manifest(candidatePairs: 7, referenceLinks: 3),
            SurveyRows(),
            0.95m,
            0.03m);

        var all = report.Subgroups.Single(group => group.Dimension == "FRAME" && group.Value == "ALL");
        Assert.Multiple(() =>
        {
            Assert.That(all.Metrics.IndependentGroups, Is.EqualTo(4));
            Assert.That(all.Uncertainty, Has.Count.EqualTo(7));
            Assert.That(all.Uncertainty.All(interval => interval.IndependentGroups == 4), Is.True);
        });
    }

    [Test]
    public void Evaluate_FailsClosedForInvalidWeightGroupOrInsufficientClusters()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var invalidWeight = SurveyRows();
        invalidWeight[0] = invalidWeight[0] with { DesignWeight = 0m };
        var invalidGroup = SurveyRows();
        invalidGroup[0] = invalidGroup[0] with { IndependenceGroupFingerprintSha256 = "deadbeef" };
        var twoGroups = SurveyRows();
        twoGroups[0] = twoGroups[0] with { IndependenceGroupFingerprintSha256 = Hash("a") };
        twoGroups[1] = twoGroups[1] with { IndependenceGroupFingerprintSha256 = Hash("a") };
        twoGroups[2] = twoGroups[2] with { IndependenceGroupFingerprintSha256 = Hash("b") };
        twoGroups[3] = twoGroups[3] with { IndependenceGroupFingerprintSha256 = Hash("b") };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => IndependentResolutionSurveyEvaluator.Evaluate(manifest, invalidWeight, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => IndependentResolutionSurveyEvaluator.Evaluate(manifest, invalidGroup, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => IndependentResolutionSurveyEvaluator.Evaluate(manifest, twoGroups, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    private static IndependentResolutionSurveyObservation[] SurveyRows() =>
    [
        Survey(Observation("1", "a", new[] { Candidate("a", 0.98m), Candidate("b", 0.10m) }), 2m, "a"),
        Survey(Observation("2", "c", new[] { Candidate("d", 0.97m), Candidate("c", 0.94m) }), 1m, "b"),
        Survey(Observation("3", null, new[] { Candidate("e", 0.96m), Candidate("f", 0.95m) }), 3m, "c"),
        Survey(Observation("4", "7", new[] { Candidate("8", 0.80m) }), 4m, "d")
    ];

    private static IndependentResolutionSurveyObservation Survey(
        IndependentResolutionObservation observation,
        decimal weight,
        string group) =>
        new(observation, weight, Hash(group));

    private static IndependentResolutionObservation Observation(
        string observation,
        string? reference,
        IReadOnlyList<IndependentResolutionCandidate> candidates) =>
        new(
            Hash(observation),
            reference is null ? null : Hash(reference),
            candidates,
            new Dictionary<string, string> { ["FRAME"] = "ALL" });

    private static IndependentResolutionCandidate Candidate(string candidate, decimal score) =>
        new(Hash(candidate), score);

    private static IndependentRuleSetEvaluationManifest Manifest(long candidatePairs, long referenceLinks)
    {
        var evaluation = IndependentEvaluationManifestCatalog.Create(
            "eval-v1",
            "model-v1",
            Hash("a"),
            Hash("b"),
            Hash("c"),
            candidatePairs,
            referenceLinks,
            CapturedAt);
        var rules = LinkageDynamicRuleSet.Create(
            "rules-v1",
            "calibrator-v1",
            new[] { BlockingCandidateFeatureCatalog.FirstName },
            new[] { new KeyValuePair<string, decimal>("threshold", 0.95m) });
        return IndependentRuleSetEvaluationManifestCatalog.Bind(evaluation, rules);
    }

    private static string Hash(string token) => new(token[0], 64);
}

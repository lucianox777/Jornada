using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IndependentResolutionEvaluationTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Evaluate_ComputesResolutionQualityCandidateRecoveryAndCalibration()
    {
        var report = IndependentResolutionEvaluator.Evaluate(
            Manifest(candidatePairs: 7, referenceLinks: 3),
            RepresentativeObservations(),
            threshold: 0.95m,
            conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(IndependentResolutionEvaluator.Version));
            Assert.That(report.RuleSetVersion, Is.EqualTo("rules-v1"));
            Assert.That(report.FingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(report.Overall.Observations, Is.EqualTo(4));
            Assert.That(report.Overall.ReferenceLinks, Is.EqualTo(3));
            Assert.That(report.Overall.Resolved, Is.EqualTo(2));
            Assert.That(report.Overall.Conflicts, Is.EqualTo(1));
            Assert.That(report.Overall.Unresolved, Is.EqualTo(1));
            Assert.That(report.Overall.TrueLinks, Is.EqualTo(1));
            Assert.That(report.Overall.FalseLinks, Is.EqualTo(1));
            Assert.That(report.Overall.MissedLinks, Is.EqualTo(2));
            Assert.That(report.Overall.TrueNonLinks, Is.EqualTo(1));
            Assert.That(report.Overall.RecoveredReferenceCandidates, Is.EqualTo(2));
            Assert.That(report.Overall.Recall, Is.EqualTo(1m / 3m));
            Assert.That(report.Overall.Precision, Is.EqualTo(0.5m));
            Assert.That(report.Overall.FalseLinkRate, Is.EqualTo(0.5m));
            Assert.That(report.Overall.FalsePositiveRate, Is.EqualTo(0.5m));
            Assert.That(report.Overall.ResolutionRate, Is.EqualTo(0.5m));
            Assert.That(report.Overall.CandidateRecoveryRate, Is.EqualTo(2m / 3m));
            Assert.That(report.Overall.TopCandidateBrierScore, Is.EqualTo(0.625725m));
        });

        var highBin = report.Calibration.Single(x => x.LowerInclusivePercent == 90);
        Assert.Multiple(() =>
        {
            Assert.That(highBin.Observations, Is.EqualTo(3));
            Assert.That(highBin.MeanPredictedProbability, Is.EqualTo(0.97m));
            Assert.That(highBin.ObservedMatchRate, Is.EqualTo(1m / 3m));
        });
    }

    [Test]
    public void Evaluate_UsesSameConflictBoundaryAsRuntimePolicy()
    {
        var rows = new[]
        {
            Observation("1", "a", new[] { Candidate("a", 0.98m), Candidate("b", 0.95m) })
        };

        var report = IndependentResolutionEvaluator.Evaluate(
            Manifest(candidatePairs: 2, referenceLinks: 1), rows, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(report.Overall.Resolved, Is.EqualTo(1));
            Assert.That(report.Overall.Conflicts, Is.Zero);
            Assert.That(report.Overall.TrueLinks, Is.EqualTo(1));
        });
    }

    [Test]
    public void Evaluate_IsDeterministicAcrossInputAndCandidateOrdering()
    {
        var first = RepresentativeObservations();
        var second = first
            .Reverse()
            .Select(row => row with { Candidates = row.Candidates.Reverse().ToArray() })
            .ToArray();
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);

        var a = IndependentResolutionEvaluator.Evaluate(manifest, first, 0.95m, 0.03m);
        var b = IndependentResolutionEvaluator.Evaluate(manifest, second, 0.95m, 0.03m);

        Assert.That(a.FingerprintSha256, Is.EqualTo(b.FingerprintSha256));
    }

    [Test]
    public void Evaluate_ProducesIndependentSubgroupSlices()
    {
        var report = IndependentResolutionEvaluator.Evaluate(
            Manifest(candidatePairs: 7, referenceLinks: 3),
            RepresentativeObservations(),
            0.95m,
            0.03m);

        var cohortA = report.Subgroups.Single(x => x.Dimension == "COHORT" && x.Value == "A");
        Assert.Multiple(() =>
        {
            Assert.That(cohortA.Metrics.Observations, Is.EqualTo(2));
            Assert.That(cohortA.Metrics.ReferenceLinks, Is.EqualTo(2));
            Assert.That(cohortA.Metrics.TrueLinks, Is.EqualTo(1));
            Assert.That(cohortA.Metrics.FalseLinks, Is.EqualTo(1));
            Assert.That(cohortA.Metrics.MissedLinks, Is.EqualTo(1));
            Assert.That(cohortA.Metrics.Recall, Is.EqualTo(0.5m));
            Assert.That(cohortA.Metrics.Precision, Is.EqualTo(0.5m));
        });
    }

    [Test]
    public void Evaluate_FailsClosedOnManifestDenominatorsDuplicatesAndInvalidScores()
    {
        var valid = RepresentativeObservations();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => IndependentResolutionEvaluator.Evaluate(
                    Manifest(candidatePairs: 8, referenceLinks: 3), valid, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(
                () => IndependentResolutionEvaluator.Evaluate(
                    Manifest(candidatePairs: 7, referenceLinks: 2), valid, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());

            var duplicate = new[]
            {
                Observation("1", null, new[] { Candidate("a", 0.1m), Candidate("a", 0.2m) })
            };
            Assert.That(
                () => IndependentResolutionEvaluator.Evaluate(
                    Manifest(candidatePairs: 2, referenceLinks: 0), duplicate, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());

            var invalidScore = new[]
            {
                Observation("1", null, new[] { Candidate("a", 1.01m) })
            };
            Assert.That(
                () => IndependentResolutionEvaluator.Evaluate(
                    Manifest(candidatePairs: 1, referenceLinks: 0), invalidScore, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    private static IndependentResolutionObservation[] RepresentativeObservations() =>
    [
        Observation("1", "a", new[] { Candidate("a", 0.98m), Candidate("b", 0.10m) }, "A"),
        Observation("2", "c", new[] { Candidate("d", 0.97m), Candidate("c", 0.94m) }, "A"),
        Observation("3", null, new[] { Candidate("e", 0.96m), Candidate("f", 0.95m) }, "B"),
        Observation("4", "7", new[] { Candidate("8", 0.80m) }, "B")
    ];

    private static IndependentResolutionObservation Observation(
        string observation,
        string? reference,
        IReadOnlyList<IndependentResolutionCandidate> candidates,
        string? cohort = null) =>
        new(
            Hash(observation),
            reference is null ? null : Hash(reference),
            candidates,
            cohort is null ? null : new Dictionary<string, string> { ["COHORT"] = cohort });

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

    private static string Hash(string token)
    {
        var c = token[0];
        return new string(c, 64);
    }
}

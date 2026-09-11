using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IndependentResolutionWeightProvenanceTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 11, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public void Evaluate_RequiresExplicitWeightProvenanceAndDelegatesSurveyMetrics()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var governed = GovernedRows();
        var report = IndependentResolutionGovernedSurveyEvaluator.Evaluate(
            manifest,
            governed,
            threshold: 0.95m,
            conflictMargin: 0.03m);
        var direct = IndependentResolutionSurveyEvaluator.Evaluate(
            manifest,
            governed.Select(static row => row.SurveyObservation).ToArray(),
            threshold: 0.95m,
            conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(IndependentResolutionGovernedSurveyEvaluator.Version));
            Assert.That(report.BaseSurveyFingerprintSha256, Is.EqualTo(direct.FingerprintSha256));
            Assert.That(report.Survey.FingerprintSha256, Is.EqualTo(direct.FingerprintSha256));
            Assert.That(report.Survey.Overall.Recall, Is.EqualTo(direct.Overall.Recall));
            Assert.That(report.Survey.Overall.Precision, Is.EqualTo(direct.Overall.Precision));
            Assert.That(report.WeightMethodVersion, Is.EqualTo("WEIGHT_METHOD_V1"));
            Assert.That(report.WeightProvenanceFingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(report.FingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(report.Observations, Is.EqualTo(4));
            Assert.That(report.SelectionAdjustmentsApplied, Is.EqualTo(1));
            Assert.That(report.NonResponseAdjustmentsApplied, Is.EqualTo(1));
            Assert.That(report.CalibrationAdjustmentsApplied, Is.EqualTo(1));
        });
    }

    [Test]
    public void Evaluate_IsDeterministicAcrossOrderingAndBindsProvenance()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var baseline = GovernedRows();
        var reversed = baseline.Reverse().ToArray();

        var first = IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, baseline, 0.95m, 0.03m);
        var second = IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, reversed, 0.95m, 0.03m);

        var changed = baseline.ToArray();
        var firstAdjustments = changed[0].WeightProvenance.Adjustments.ToArray();
        firstAdjustments[0] = firstAdjustments[0] with { FingerprintSha256 = Hash("f") };
        changed[0] = changed[0] with
        {
            WeightProvenance = changed[0].WeightProvenance with { Adjustments = firstAdjustments }
        };
        var third = IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, changed, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(second.WeightProvenanceFingerprintSha256, Is.EqualTo(first.WeightProvenanceFingerprintSha256));
            Assert.That(second.FingerprintSha256, Is.EqualTo(first.FingerprintSha256));
            Assert.That(third.BaseSurveyFingerprintSha256, Is.EqualTo(first.BaseSurveyFingerprintSha256));
            Assert.That(third.WeightProvenanceFingerprintSha256, Is.Not.EqualTo(first.WeightProvenanceFingerprintSha256));
            Assert.That(third.FingerprintSha256, Is.Not.EqualTo(first.FingerprintSha256));
        });
    }

    [Test]
    public void Evaluate_FailsClosedForWeightMismatchMissingAdjustmentOrHiddenEvidence()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);

        var mismatch = GovernedRows();
        mismatch[0] = mismatch[0] with
        {
            WeightProvenance = mismatch[0].WeightProvenance with { FinalWeight = 99m }
        };

        var missingAdjustment = GovernedRows();
        missingAdjustment[0] = missingAdjustment[0] with
        {
            WeightProvenance = missingAdjustment[0].WeightProvenance with
            {
                Adjustments = missingAdjustment[0].WeightProvenance.Adjustments.Take(2).ToArray()
            }
        };

        var appliedWithoutEvidence = GovernedRows();
        var applied = appliedWithoutEvidence[0].WeightProvenance.Adjustments.ToArray();
        applied[0] = applied[0] with { Reference = null, FingerprintSha256 = null };
        appliedWithoutEvidence[0] = appliedWithoutEvidence[0] with
        {
            WeightProvenance = appliedWithoutEvidence[0].WeightProvenance with { Adjustments = applied }
        };

        var notApplicableWithEvidence = GovernedRows();
        var hidden = notApplicableWithEvidence[3].WeightProvenance.Adjustments.ToArray();
        hidden[0] = hidden[0] with { Reference = "hidden", FingerprintSha256 = Hash("e") };
        notApplicableWithEvidence[3] = notApplicableWithEvidence[3] with
        {
            WeightProvenance = notApplicableWithEvidence[3].WeightProvenance with { Adjustments = hidden }
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, mismatch, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, missingAdjustment, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, appliedWithoutEvidence, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, notApplicableWithEvidence, 0.95m, 0.03m),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void Evaluate_FailsClosedWhenWeightMethodVersionsAreMixed()
    {
        var manifest = Manifest(candidatePairs: 7, referenceLinks: 3);
        var rows = GovernedRows();
        rows[3] = rows[3] with
        {
            WeightProvenance = rows[3].WeightProvenance with { MethodVersion = "WEIGHT_METHOD_V2" }
        };

        Assert.That(
            () => IndependentResolutionGovernedSurveyEvaluator.Evaluate(manifest, rows, 0.95m, 0.03m),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static GovernedIndependentResolutionSurveyObservation[] GovernedRows()
    {
        var survey = SurveyRows();
        return
        [
            Governed(survey[0], baseWeight: 1m, selection: IndependentResolutionWeightAdjustmentState.Applied),
            Governed(survey[1], baseWeight: 1m, nonResponse: IndependentResolutionWeightAdjustmentState.Applied),
            Governed(survey[2], baseWeight: 2m, calibration: IndependentResolutionWeightAdjustmentState.Applied),
            Governed(survey[3], baseWeight: 4m)
        ];
    }

    private static GovernedIndependentResolutionSurveyObservation Governed(
        IndependentResolutionSurveyObservation survey,
        decimal baseWeight,
        IndependentResolutionWeightAdjustmentState selection = IndependentResolutionWeightAdjustmentState.NotApplicable,
        IndependentResolutionWeightAdjustmentState nonResponse = IndependentResolutionWeightAdjustmentState.NotApplicable,
        IndependentResolutionWeightAdjustmentState calibration = IndependentResolutionWeightAdjustmentState.NotApplicable)
    {
        var provenance = new IndependentResolutionWeightProvenance(
            "WEIGHT_METHOD_V1",
            "governance://weighting/method-v1",
            baseWeight,
            survey.DesignWeight,
            CapturedAt.AddHours(1),
            new[]
            {
                Adjustment(IndependentResolutionWeightAdjustmentKind.Selection, selection, "s"),
                Adjustment(IndependentResolutionWeightAdjustmentKind.NonResponse, nonResponse, "n"),
                Adjustment(IndependentResolutionWeightAdjustmentKind.Calibration, calibration, "c")
            });
        return new GovernedIndependentResolutionSurveyObservation(survey, provenance);
    }

    private static IndependentResolutionWeightAdjustment Adjustment(
        IndependentResolutionWeightAdjustmentKind kind,
        IndependentResolutionWeightAdjustmentState state,
        string token) =>
        state == IndependentResolutionWeightAdjustmentState.Applied
            ? new IndependentResolutionWeightAdjustment(kind, state, $"evidence://{kind}", Hash(token))
            : new IndependentResolutionWeightAdjustment(kind, state, null, null);

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

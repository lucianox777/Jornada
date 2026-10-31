using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SplinkCompatibleTermFrequencyTests
{
    [Test]
    public void FuzzyAdjustment_UsesMoreFrequentSideConservatively()
    {
        var effective = SplinkCompatibleTermFrequency.EffectiveFrequency(0.0001m, 0.01m);
        var adjustment = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            0.0001m,
            0.01m,
            referenceUProbability: 0.02m);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(effective, Is.EqualTo(0.01m));
            Assert.That(adjustment, Is.EqualTo(Math.Log(2d)).Within(1e-12));
        }));
    }

    [Test]
    public void MinimumUValue_CapsEvidenceFromVeryRareTerms()
    {
        var uncapped = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            0.000001m,
            0.000001m,
            0.01m);
        var capped = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            0.000001m,
            0.000001m,
            0.01m,
            minimumUValue: 0.001m);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(capped, Is.LessThan(uncapped));
            Assert.That(capped, Is.EqualTo(Math.Log(10d)).Within(1e-12));
        }));
    }

    [Test]
    public void WeightZero_DisablesTermFrequencyAdjustment()
    {
        var adjustment = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            0.001m,
            0.01m,
            0.1m,
            weight: 0m);

        Assert.That(adjustment, Is.Zero);
    }

    [Test]
    public void AsymmetricTf_PenalizesCommonTermButDoesNotRewardRareTerm()
    {
        var common = SplinkCompatibleTermFrequency.LogBayesAdjustmentAsymmetric(
            0.2m, 0.1m, 0.01m);
        var rare = SplinkCompatibleTermFrequency.LogBayesAdjustmentAsymmetric(
            0.0001m, 0.0002m, 0.01m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(common, Is.EqualTo(Math.Log(0.05d)).Within(1e-12));
            Assert.That(rare, Is.Zero);
        }));
    }

    [Test]
    public void AsymmetricTf_ZeroWeightIsNeutralAndSymmetricRemainsUnchanged()
    {
        var zero = SplinkCompatibleTermFrequency.LogBayesAdjustmentAsymmetric(
            0.2m, 0.1m, 0.01m, weight: 0m);
        var symmetric = SplinkCompatibleTermFrequency.LogBayesAdjustment(
            0.0001m, 0.0002m, 0.01m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(zero, Is.Zero);
            Assert.That(symmetric, Is.EqualTo(Math.Log(50d)).Within(1e-12));
        }));
    }

    [Test]
    public void AsymmetricTf_NeverRewardsAcrossFrequencyAndWeightGrid()
    {
        var frequencies = new[] { 0.000001m, 0.0001m, 0.01m, 0.2m, 1m };
        var weights = new[] { 0m, 0.25m, 1m, 2m };
        foreach (var left in frequencies)
        foreach (var right in frequencies)
        foreach (var referenceU in frequencies)
        foreach (var weight in weights)
        {
            var symmetric = SplinkCompatibleTermFrequency.LogBayesAdjustment(
                left, right, referenceU, weight);
            var asymmetric = SplinkCompatibleTermFrequency.LogBayesAdjustmentAsymmetric(
                left, right, referenceU, weight);
            Assert.That(asymmetric, Is.EqualTo(Math.Min(0d, symmetric)).Within(1e-12),
                $"left={left}, right={right}, u={referenceU}, weight={weight}");
            Assert.That(asymmetric, Is.LessThanOrEqualTo(0d));
        }
    }

    [Test]
    public void ExplicitV9Modes_KeepZeroNeutralAndMatchExistingImplementations()
    {
        const decimal left = 0.2m, right = 0.1m, u = 0.01m;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                SplinkCompatibleTermFrequency.AdjustmentModeV9.Zero, left, right, u), Is.Zero);
            Assert.That(SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                SplinkCompatibleTermFrequency.AdjustmentModeV9.Symmetric, left, right, u),
                Is.EqualTo(SplinkCompatibleTermFrequency.LogBayesAdjustment(left, right, u)));
            Assert.That(SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                SplinkCompatibleTermFrequency.AdjustmentModeV9.Asymmetric, left, right, u),
                Is.EqualTo(SplinkCompatibleTermFrequency.LogBayesAdjustmentAsymmetric(left, right, u)));
            Assert.That((Action)(() => SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                (SplinkCompatibleTermFrequency.AdjustmentModeV9)99, left, right, u)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    [Test]
    public void ZeroMode_RejectsInvalidInputsInsteadOfSilentlyAcceptingThem()
    {
        var mode = SplinkCompatibleTermFrequency.AdjustmentModeV9.Zero;
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                mode, 0m, 0.1m, 0.01m)), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() => SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                mode, 0.1m, 1.1m, 0.01m)), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() => SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                mode, 0.1m, 0.1m, 0m)), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() => SplinkCompatibleTermFrequency.LogBayesAdjustmentV9(
                mode, 0.1m, 0.1m, 0.01m, weight: -1m)), Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    [Test]
    public void Pareto_DoesNotInventPreferenceBetweenFalsePositiveAndFalseNegative()
    {
        var a = new CalibrationEvaluation("A", 90, 90, 1, 8, 11, 200);
        var b = new CalibrationEvaluation("B", 90, 90, 4, 3, 13, 200);
        var dominated = new CalibrationEvaluation("C", 90, 90, 5, 9, 6, 200);
        var sameErrorsMoreInconclusive = new CalibrationEvaluation("D", 90, 90, 1, 8, 20, 200);

        var frontier = CalibrationCandidatePareto.NonDominated(new[] { a, b, dominated, sameErrorsMoreInconclusive });

        Assert.That(frontier.Select(x => x.CandidateId), Is.EquivalentTo(new[] { "A", "B" }));
    }
}

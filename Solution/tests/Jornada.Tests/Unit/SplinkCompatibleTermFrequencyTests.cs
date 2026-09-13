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

        Assert.Multiple(() =>
        {
            Assert.That(effective, Is.EqualTo(0.01m));
            Assert.That(adjustment, Is.EqualTo(Math.Log(2d)).Within(1e-12));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(capped, Is.LessThan(uncapped));
            Assert.That(capped, Is.EqualTo(Math.Log(10d)).Within(1e-12));
        });
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
    public void NominalDf_PreservesSimilarityAndFrequencyAsSeparateEvidence()
    {
        var evidence = NominalDfEvidenceCalculator.Evaluate(
            "SOUSA",
            "SOUZA",
            leftFrequency: 0.002m,
            rightFrequency: 0.003m,
            referenceUProbability: 0.01m);

        Assert.Multiple(() =>
        {
            Assert.That(evidence.Similarity, Is.GreaterThan(0.8d));
            Assert.That(evidence.EffectiveFrequency, Is.EqualTo(0.003m));
            Assert.That(evidence.TermFrequencyLogAdjustment, Is.Not.Null);
            Assert.That(evidence.SimilarityAlgorithmVersion, Is.EqualTo("JARO_WINKLER@V1"));
            Assert.That(evidence.TermFrequencyAlgorithmVersion, Is.EqualTo("SPLINK_TERM_FREQUENCY_V1"));
        });
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

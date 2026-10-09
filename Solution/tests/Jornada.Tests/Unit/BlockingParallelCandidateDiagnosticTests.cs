using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingParallelCandidateDiagnosticTests
{
    [Test]
    public void Analyze_ComparesComplementaryAndSharedPairsInExactlyTheSameUniverse()
    {
        // Six no-CPF inter-Gestor pairs; 1, 2 and 3 are reference true matches.
        // D recovers 1 and 2; C recovers 2 and 3.
        var result = BlockingParallelCandidateDiagnostic.Analyze(
            dynamicCandidatePairs: new ulong[] { 1, 2, 4 },
            combinedCandidatePairs: new ulong[] { 2, 3, 5 },
            eligibleTruePairs: new ulong[] { 1, 2, 3 },
            eligiblePairCount: 6,
            combinedEligibleTruePairs: 3,
            maxCandidatePairs: 6);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Universe, Is.EqualTo(
                BlockingParallelCandidateDiagnostic.Universe));
            Assert.That(result.EligiblePairCount, Is.EqualTo(6));
            Assert.That(result.EligibleTruePairs, Is.EqualTo(3));
            Assert.That(result.DynamicCandidatePairs, Is.EqualTo(3));
            Assert.That(result.CombinedCandidatePairs, Is.EqualTo(3));
            Assert.That(result.UnionCandidatePairs, Is.EqualTo(5));
            Assert.That(result.SharedCandidatePairs, Is.EqualTo(1));
            Assert.That(result.DynamicOnlyCandidatePairs, Is.EqualTo(2));
            Assert.That(result.CombinedOnlyCandidatePairs, Is.EqualTo(2));
            Assert.That(result.DynamicTruePairs, Is.EqualTo(2));
            Assert.That(result.CombinedTruePairs, Is.EqualTo(2));
            Assert.That(result.UnionTruePairs, Is.EqualTo(3));
            Assert.That(result.SharedTruePairs, Is.EqualTo(1));
            Assert.That(result.DynamicOnlyTruePairs, Is.EqualTo(1));
            Assert.That(result.CombinedOnlyTruePairs, Is.EqualTo(1));
            Assert.That(result.DynamicRecall, Is.EqualTo(2m / 3m));
            Assert.That(result.CombinedRecall, Is.EqualTo(2m / 3m));
            Assert.That(result.CombinedConditionalRecall, Is.EqualTo(2m / 3m));
            Assert.That(result.UnionRecall, Is.EqualTo(1m));
            Assert.That(result.DynamicReductionRatio, Is.EqualTo(2m / 3m));
            Assert.That(result.CombinedReductionRatio, Is.EqualTo(2m / 3m));
            Assert.That(result.UnionReductionRatio, Is.EqualTo(1m / 3m));
        }));
    }

    [Test]
    public void Analyze_CombinedMissingMotherIsVisibleInOverallAndConditionalRecall()
    {
        // The true pair excluded from C's eligibility still remains in D/C's denominator.
        var result = BlockingParallelCandidateDiagnostic.Analyze(
            dynamicCandidatePairs: new ulong[] { 1, 2 },
            combinedCandidatePairs: new ulong[] { 2 },
            eligibleTruePairs: new ulong[] { 1, 2 },
            eligiblePairCount: 4,
            combinedEligibleTruePairs: 1,
            maxCandidatePairs: 4);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.CombinedRecall, Is.EqualTo(0.5m));
            Assert.That(result.CombinedConditionalRecall, Is.EqualTo(1m));
            Assert.That(result.UnionRecall, Is.EqualTo(1m));
            Assert.That(result.CombinedOnlyTruePairs, Is.Zero);
        }));
    }

    [Test]
    public void Analyze_DeduplicatesRepeatedPairsAndShowsZeroIncrementWhenCIsRedundant()
    {
        var result = BlockingParallelCandidateDiagnostic.Analyze(
            dynamicCandidatePairs: new ulong[] { 1, 1, 2 },
            combinedCandidatePairs: new ulong[] { 1, 1 },
            eligibleTruePairs: new ulong[] { 1 },
            eligiblePairCount: 3,
            combinedEligibleTruePairs: 1,
            maxCandidatePairs: 2);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.DynamicCandidatePairs, Is.EqualTo(2));
            Assert.That(result.UnionCandidatePairs, Is.EqualTo(2));
            Assert.That(result.SharedCandidatePairs, Is.EqualTo(1));
            Assert.That(result.CombinedOnlyCandidatePairs, Is.Zero);
            Assert.That(result.CombinedOnlyTruePairs, Is.Zero);
            Assert.That(result.UnionTruePairs, Is.EqualTo(result.DynamicTruePairs));
        }));
    }

    [Test]
    public void Analyze_FailsClosedWhenCombinedUnionExceedsBudget()
    {
        Assert.That(
            () => BlockingParallelCandidateDiagnostic.Analyze(
                dynamicCandidatePairs: new ulong[] { 1, 2 },
                combinedCandidatePairs: new ulong[] { 3 },
                eligibleTruePairs: new ulong[] { 1 },
                eligiblePairCount: 4,
                combinedEligibleTruePairs: 1,
                maxCandidatePairs: 2),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("sem truncar"));
    }

    [Test]
    public void Analyze_RejectsInconsistentTruePairAndEligibilityDenominators()
    {
        Assert.That(
            () => BlockingParallelCandidateDiagnostic.Analyze(
                new ulong[] { 1 }, new ulong[] { 1 },
                new ulong[] { 1, 1 }, 2, 1, 3),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            () => BlockingParallelCandidateDiagnostic.Analyze(
                new ulong[] { 1 }, new ulong[] { 1 },
                new ulong[] { 1 }, 1, 0, 3),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            () => BlockingParallelCandidateDiagnostic.Analyze(
                new ulong[] { 1, 2 }, Array.Empty<ulong>(),
                new ulong[] { 1 }, 1, 0, 3),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Analyze_NoCpfFreeInterGestorTruthIsRepresentedAsNoEvidence()
    {
        var result = BlockingParallelCandidateDiagnostic.Analyze(
            Array.Empty<ulong>(), Array.Empty<ulong>(), Array.Empty<ulong>(),
            eligiblePairCount: 0, combinedEligibleTruePairs: 0, maxCandidatePairs: 5);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.UnionCandidatePairs, Is.Zero);
            Assert.That(result.UnionRecall, Is.Zero);
            Assert.That(result.UnionReductionRatio, Is.Zero);
            Assert.That(result.EligibleTruePairs, Is.Zero);
        }));
    }
}

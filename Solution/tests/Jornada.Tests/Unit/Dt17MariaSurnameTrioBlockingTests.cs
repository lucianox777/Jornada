using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

/// <summary>
/// DT-17: synthetic, pre-labelled D/C candidate memberships for the MARIA surname
/// trio. This tests the shared diagnostic and denominators; it does NOT prove
/// that either real blocking query actually retrieves these particular pairs.
/// No model, reference, corpus or database is modified.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class Dt17MariaSurnameTrioBlockingTests
{
    private sealed record Pair(
        ulong Id,
        string LeftName,
        string RightName,
        string? LeftMother,
        string? RightMother,
        bool IsTrueMatch,
        bool InDynamic,
        bool InCombined);

    // Invented names and pair labels, exclusively for a deterministic DEV fixture.
    // A pair can be a homonym despite exactly equal names. The last positive pair
    // lacks a mother on one side and therefore cannot enter C's eligible truth.
    private static readonly Pair[] Cases =
    [
        new(1, "MARIA SOUZA", "MARIA SOUZA LIMA", "ANA LIMA", "ANA LIMA",
            true, true, false), // D-only positive: extra surname
        new(2, "MARIA SOUZA LIMA", "MARIA LIMA", "ANA LIMA", "ANA LIMA",
            true, false, true), // C-only positive: omitted surname
        new(3, "MARIA SOUZA", "MARIA SOUZA", "ANA LIMA", "ANA LIMA",
            true, true, true), // shared positive
        new(4, "MARIA SOUZA", "MARIA SOUZA", "ANA LIMA", "BIA LIMA",
            false, true, false), // distinct people, identical names
        new(5, "MARIA LIMA", "MARIA SOUZA LIMA", "ANA LIMA", "BIA LIMA",
            false, false, true), // C-only negative candidate
        new(6, "MARIA LIMA", "MARIA LIMA", null, "ANA LIMA",
            true, true, false), // D-only positive: C ineligible without mother
        new(7, "MARIA SOUZA LIMA", "MARIA LIMA", "BIA LIMA", "BIA LIMA",
            false, false, false) // negative not retrieved by either pass
    ];

    private static SyntheticParallelBlockingEvaluation Evaluate(int maxCandidatePairs)
    {
        var truth = Cases.Where(x => x.IsTrueMatch).Select(x => x.Id).ToArray();
        var combinedEligibleTruth = Cases.Count(x =>
            x.IsTrueMatch && x.LeftMother is not null && x.RightMother is not null);
        return BlockingParallelCandidateDiagnostic.Analyze(
            dynamicCandidatePairs: Cases.Where(x => x.InDynamic).Select(x => x.Id).ToArray(),
            combinedCandidatePairs: Cases.Where(x => x.InCombined).Select(x => x.Id).ToArray(),
            eligibleTruePairs: truth,
            eligiblePairCount: Cases.Length,
            combinedEligibleTruePairs: combinedEligibleTruth,
            maxCandidatePairs: maxCandidatePairs);
    }

    [Test]
    public void MariaTrio_D_C_AndDeduplicatedUnionUseOneTruthDenominator()
    {
        var result = Evaluate(maxCandidatePairs: 6);

        Assert.Multiple(() =>
        {
            Assert.That(Cases.SelectMany(p => new[] { p.LeftName, p.RightName }).Distinct(),
                Is.SupersetOf(new[] { "MARIA SOUZA", "MARIA SOUZA LIMA", "MARIA LIMA" }));
            Assert.That(Cases.Single(p => p.Id == 6).LeftMother, Is.Null);
            Assert.That(Cases.Single(p => p.Id == 4).IsTrueMatch, Is.False);
            Assert.That(result.Universe, Is.EqualTo(BlockingParallelCandidateDiagnostic.Universe));
            Assert.That(result.EligiblePairCount, Is.EqualTo(7));
            Assert.That(result.EligibleTruePairs, Is.EqualTo(4));
            Assert.That(result.CombinedEligibleTruePairs, Is.EqualTo(3));
            Assert.That(result.DynamicCandidatePairs, Is.EqualTo(4));
            Assert.That(result.CombinedCandidatePairs, Is.EqualTo(3));
            Assert.That(result.UnionCandidatePairs, Is.EqualTo(6));
            Assert.That(result.SharedCandidatePairs, Is.EqualTo(1));
            Assert.That(result.DynamicOnlyCandidatePairs, Is.EqualTo(3));
            Assert.That(result.CombinedOnlyCandidatePairs, Is.EqualTo(2));
            Assert.That(result.DynamicTruePairs, Is.EqualTo(3));
            Assert.That(result.CombinedTruePairs, Is.EqualTo(2));
            Assert.That(result.SharedTruePairs, Is.EqualTo(1));
            Assert.That(result.DynamicOnlyTruePairs, Is.EqualTo(2));
            Assert.That(result.CombinedOnlyTruePairs, Is.EqualTo(1));
            Assert.That(result.UnionTruePairs, Is.EqualTo(4));
            Assert.That(result.DynamicRecall, Is.EqualTo(3m / 4m));
            Assert.That(result.CombinedRecall, Is.EqualTo(2m / 4m));
            Assert.That(result.CombinedConditionalRecall, Is.EqualTo(2m / 3m));
            Assert.That(result.UnionRecall, Is.EqualTo(1m));
            Assert.That(result.DynamicReductionRatio, Is.EqualTo(2m / 3m));
            Assert.That(result.CombinedReductionRatio, Is.EqualTo(2m / 3m));
            Assert.That(result.UnionReductionRatio, Is.EqualTo(1m / 3m));
        });
    }

    [Test]
    public void MariaTrio_UnionFailsClosedRatherThanTruncateARequiredCandidate()
    {
        Assert.That(() => Evaluate(maxCandidatePairs: 5),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("sem truncar"));
    }
}

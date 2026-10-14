using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlTrainValidationUnionSearchTests
{
    private static readonly V9SqlTrainValidationUnionSearch.Pair[] Baseline =
    [
        .. Enumerable.Range(0, 8).Select(i =>
            new V9SqlTrainValidationUnionSearch.Pair(
                i*2, i*2+1, "TRAIN", true)),
        .. Enumerable.Range(0, 8).Select(i =>
            new V9SqlTrainValidationUnionSearch.Pair(
                100+i*2, 101+i*2, "VALIDATION", true)),
        new(40, 41, "TRAIN", false),
        new(140, 141, "VALIDATION", false)
    ];
    private static readonly V9SqlTrainValidationUnionSearch.Pair TrainA =
        new(300, 301, "TRAIN", true);
    private static readonly V9SqlTrainValidationUnionSearch.Pair TrainB =
        new(302, 303, "TRAIN", true);
    private static readonly V9SqlTrainValidationUnionSearch.Pair ValidationA =
        new(400, 401, "VALIDATION", true);
    private static readonly V9SqlTrainValidationUnionSearch.Pair ValidationB =
        new(402, 403, "VALIDATION", true);

    private static readonly V9SqlTrainValidationUnionSearch.Extension[] Extensions =
    [
        new("P_A", [TrainA, ValidationA, new(900, 901, "TRAIN", false)]),
        new("P_B", [TrainB, ValidationB, new(902, 903, "TRAIN", false)]),
        new("P_C", [TrainA, ValidationA]), // strictly overlapping P_A
        new("P_D", []), new("P_E", []), new("P_F", [])
    ];

    [Test]
    public void Deduplicates_overlapping_passes_and_requires_recall_95_on_both_frames()
    {
        var result = V9SqlTrainValidationUnionSearch.Analyze(
            Baseline, Extensions, trainEligibleM: 10, validationEligibleM: 10,
            pairHardCap: 1000);
        var a = result.Combinations.Single(x => x.Signature == "P_A");
        var ab = result.Combinations.Single(x => x.Signature == "P_A||P_B");
        var ac = result.Combinations.Single(x => x.Signature == "P_A||P_C");
        var abc = result.Combinations.Single(x => x.Signature == "P_A||P_B||P_C");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Version, Is.EqualTo(V9SqlTrainValidationUnionSearch.Version));
            Assert.That(result.RegisteredPassCount, Is.EqualTo(6));
            Assert.That(result.EvaluatedUnionCount, Is.EqualTo(56));
            Assert.That(result.TrainEligibleM, Is.EqualTo(10));
            Assert.That(result.MinimumTrainMFor95Percent, Is.EqualTo(10));
            Assert.That(result.BaselineTrainM, Is.EqualTo(8));
            Assert.That(result.BaselineTrainU, Is.EqualTo(1));
            Assert.That(a.TrainAddedM, Is.EqualTo(1));
            Assert.That(a.ValidationAddedM, Is.EqualTo(1));
            Assert.That(a.TrainAddedU, Is.EqualTo(1));
            Assert.That(a.MeetsBoth95PercentRecallFloors, Is.False);
            Assert.That(ac.TrainAddedM, Is.EqualTo(1)); // NOT 2
            Assert.That(ac.ValidationAddedM, Is.EqualTo(1));
            Assert.That(ac.MeetsBoth95PercentRecallFloors, Is.False);
            Assert.That(ab.TrainAddedM, Is.EqualTo(2));
            Assert.That(ab.TrainUnionM, Is.EqualTo(10));
            Assert.That(ab.TrainUnionU, Is.EqualTo(3));
            Assert.That(ab.ValidationUnionM, Is.EqualTo(10));
            Assert.That(ab.MeetsBoth95PercentRecallFloors, Is.True);
            Assert.That(abc.TrainAddedM, Is.EqualTo(2)); // NOT 3
            Assert.That(abc.MeetsBoth95PercentRecallFloors, Is.True);
            Assert.That(result.AdvisoryBestSignatureNotPublished, Is.EqualTo("P_B||P_C"));
            Assert.That(result.MayPromoteBlockingPolicy, Is.False);
            Assert.That(result.PartitionScope, Does.Contain("TEST_FORBIDDEN"));
        }));
    }

    [Test]
    public void Reports_no_advisory_if_95_percent_cannot_be_met()
    {
        var r = V9SqlTrainValidationUnionSearch.Analyze(
            Baseline, Extensions, 50, 50, 1000);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(r.PassingUnionCount, Is.Zero);
            Assert.That(r.AdvisoryBestSignatureNotPublished, Is.Null);
            Assert.That(r.MinimumTrainMFor95Percent, Is.EqualTo(48));
            Assert.That(r.MayPromoteBlockingPolicy, Is.False);
        }));
    }

    [Test]
    public void Rejects_test_leakage_conflicting_overlap_and_cap_exceedance()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTrainValidationUnionSearch.Analyze(
                [.. Baseline, new(500, 501, "TEST", true)],
                Extensions, 10, 10, 1000)));
        var bad = Extensions.ToArray();
        bad[2] = bad[2] with { ExtraPairs = [
            TrainA with { IsReferenceMatch = false }] };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTrainValidationUnionSearch.Analyze(Baseline, bad, 10, 10, 1000)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTrainValidationUnionSearch.Analyze(
                Baseline, Extensions, 10, 10, 18)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTrainValidationUnionSearch.Analyze(
                [.. Baseline, Baseline[0]], Extensions, 10, 10, 1000)));
    }

    [Test]
    public void Rejects_any_pass_with_validation_or_test_instead_of_train()
    {
        var extra = Extensions.ToArray();
        extra[4] = extra[4] with { ExtraPairs = [
            new V9SqlTrainValidationUnionSearch.Pair(800, 801, "TEST", true)] };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTrainValidationUnionSearch.Analyze(
                Baseline, extra, 10, 10, 1000)));
    }
}

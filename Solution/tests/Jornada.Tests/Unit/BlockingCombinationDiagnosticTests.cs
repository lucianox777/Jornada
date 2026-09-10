using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[Category("Unit")]
public sealed class BlockingCombinationDiagnosticTests
{
    [Test]
    public void Analyze_UnionCanIncreaseRecallOverBestSingleField()
    {
        var observations = new[]
        {
            Obs(true,  true,  false),
            Obs(true,  false, true),
            Obs(false, true,  false),
            Obs(false, false, false)
        };

        var result = BlockingCombinationDiagnostic.Analyze(observations, 2, 2).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Fields, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(result.TrueMatchRecall, Is.EqualTo(1d));
            Assert.That(result.IncrementalRecallVsBestMember, Is.EqualTo(0.5d));
            Assert.That(result.NonMatchRetention, Is.EqualTo(0.5d));
            Assert.That(result.ReductionRatio, Is.EqualTo(0.5d));
        });
    }

    [Test]
    public void Analyze_RedundantFieldsHaveNoIncrementalRecall()
    {
        var observations = new[]
        {
            Obs(true, true, true),
            Obs(true, false, false),
            Obs(false, true, true),
            Obs(false, false, false)
        };

        var result = BlockingCombinationDiagnostic.Analyze(observations, 2, 2).Single();

        Assert.That(result.IncrementalRecallVsBestMember, Is.Zero);
    }

    [Test]
    public void Analyze_RanksHigherRecallBeforeReduction()
    {
        var observations = new[]
        {
            new BlockingFeatureObservation(true, new Dictionary<string, bool?> { ["A"] = true, ["B"] = false, ["C"] = false }),
            new BlockingFeatureObservation(true, new Dictionary<string, bool?> { ["A"] = false, ["B"] = true, ["C"] = false }),
            new BlockingFeatureObservation(false, new Dictionary<string, bool?> { ["A"] = false, ["B"] = false, ["C"] = true }),
            new BlockingFeatureObservation(false, new Dictionary<string, bool?> { ["A"] = false, ["B"] = false, ["C"] = false })
        };

        var results = BlockingCombinationDiagnostic.Analyze(observations, 2, 2);

        Assert.That(results.First().Fields, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(results.First().TrueMatchRecall, Is.EqualTo(1d));
    }

    [Test]
    public void Analyze_RejectsCorpusWithoutReferenceNonMatches()
    {
        var observations = new[] { Obs(true, true, false), Obs(true, false, true) };
        Assert.That(
            () => BlockingCombinationDiagnostic.Analyze(observations),
            Throws.TypeOf<ArgumentException>());
    }

    private static BlockingFeatureObservation Obs(bool match, bool? a, bool? b) =>
        new(match, new Dictionary<string, bool?> { ["A"] = a, ["B"] = b });
}

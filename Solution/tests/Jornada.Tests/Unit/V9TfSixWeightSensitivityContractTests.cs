using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

/// <summary>
/// Executable contract for the six-weight sensitivity grid. This tiny fixture is
/// intentionally NOT the historical 30k experiment and must never be reported as such.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9TfSixWeightSensitivityContractTests
{
    private static readonly decimal[] Weights = [0m, .025m, .05m, .10m, .20m, .40m];
    private static readonly string TrainCorpus = new('a', 64);
    private static readonly string SelectionCorpus = new('b', 64);
    private static readonly string TrainGroups = new('c', 64);
    private static readonly string SelectionGroups = new('d', 64);
    private static readonly DateOnly Birth = new(1980, 1, 2);

    [Test]
    public void Six_weights_share_the_same_candidate_frame_and_never_tune_on_selection()
    {
        var entries = new[]
        {
            new NominalTermFrequencyEntry("NOME_PRENOME", "MARIA", 60, 100, .60m),
            new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "ANA", 30, 100, .30m)
        };
        var reference = FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(entries), entries, 123),
            "IBGE_PINNED", new string('e', 64), new string('1', 64),
            new NominalSyntheticIbgeBootstrapV9(
                CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                30_000, "CORPUS_30K", "GENERATOR_V1", 42UL, "HYPOTHESES_V1"));

        static V9FsAdaptiveCandidatePair Pair(string split, string left, string right, bool same) =>
            new(split, left, right, same, "MARIA COSTA", "MARIA LIMA",
                null, null, Birth, Birth, 2);

        var frame = new V9FsAdaptiveCandidateFrame(
            TrainCorpus, SelectionCorpus, TrainGroups, SelectionGroups,
            [
                Pair("TRAIN", "A", "A", true),
                Pair("TRAIN", "B", "B", true),
                Pair("TRAIN", "C", "D", false),
                Pair("TRAIN", "D", "C", false),
                Pair("VALIDATION", "E", "E", true),
                Pair("VALIDATION", "F", "F", true),
                Pair("VALIDATION", "G", "H", false),
                Pair("VALIDATION", "H", "G", false)
            ],
            CandidateBlockingPasses: [new LinkageBlockingPass("NAME", ["NOME"])],
            ProjectionSchemaVersion: "PROJECTION_V1",
            ProjectionFingerprintSha256: new string('f', 64),
            BlockingFrameFingerprintSha256: new string('1', 64));

        var options = Weights.Select((weight, index) =>
            new V9FsAdaptiveOptionConfiguration(
                $"SENSITIVITY_TF_{index}", .5m, .55m, weight)).ToArray();
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(frame, options, reference);
        var train = new AdaptiveCalibrationSlice("TRAIN", TrainCorpus, TrainGroups, 4, true, false);
        var selection = new AdaptiveCalibrationSlice(
            "VALIDATION", SelectionCorpus, SelectionGroups, 4, true, false);

        var outcomes = options.Select(option =>
        {
            var model = new AdaptiveCalibrationModelOption(
                V9FellegiSunterAdaptiveCalibrationAdapter.Family,
                V9FellegiSunterAdaptiveCalibrationAdapter.Version,
                option.OptionId, option.FingerprintSha256);
            var fit = adapter.FitTrain(model, train);
            return (option.TfWeight, Metrics: adapter.EvaluateValidation(fit, selection));
        }).ToArray();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(outcomes.Select(x => x.TfWeight), Is.EqualTo(Weights));
            Assert.That(outcomes, Has.Length.EqualTo(6));
            Assert.That(outcomes.All(x => x.Metrics.TrueAcceptedLinks >= 0), Is.True);
            Assert.That(outcomes.All(x => x.Metrics.FalseAcceptedLinks >= 0), Is.True);
            Assert.That(outcomes.Select(x => x.TfWeight).Distinct().Count(), Is.EqualTo(6),
                "The sensitivity grid must not repeat a weight.");
            Assert.That(frame.Pairs.Count, Is.EqualTo(8),
                "Sensitivity must reuse candidate pairs; never materialize a Cartesian product.");
            Assert.That(frame.Pairs.Count(x => x.Partition == "TRAIN"), Is.EqualTo(4));
            Assert.That(frame.Pairs.Count(x => x.Partition == "VALIDATION"), Is.EqualTo(4));
        }));
    }
}

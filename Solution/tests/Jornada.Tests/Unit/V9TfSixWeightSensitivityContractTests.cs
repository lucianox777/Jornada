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

        // Six PREDECLARED numeric weights from the original #1224 contract,
        // distinct from the separately tested 0/.5/1 x symmetric/asymmetric
        // modes. Rank through the shared C# scorer and portable snapshot:
        // this is still a synthetic UNIT fixture, NOT a 30k result.
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA COSTA", Birth, null);
        var candidate = new V9SnapshotCandidate(Guid.Parse(
            "e98e2ad9-b48d-4e32-8952-2cafb8c15c41"), "MARIA LIMA", Birth, null);
        var outcomes = options.Select(option =>
        {
            var model = new AdaptiveCalibrationModelOption(
                V9FellegiSunterAdaptiveCalibrationAdapter.Family,
                V9FellegiSunterAdaptiveCalibrationAdapter.Version,
                option.OptionId, option.FingerprintSha256);
            var fit = adapter.FitTrain(model, train);
            var metrics = adapter.EvaluateValidation(fit, selection);
            var frozen = adapter.ExportFrozenSnapshot(fit,
                [new LinkageBlockingPass("NAME", ["NOME"])],
                "PROJECTION_V1", new string('f', 64), .2m);
            var replay = V9FittedSnapshot.Load(frozen.SerializeUtf8(), frozen.ContentSha256);
            var score = replay.RankPreview(observation, [candidate]).Single().LogOdds;
            Assert.That(replay.Payload.TrainCorpusSha256, Is.EqualTo(TrainCorpus));
            Assert.That(replay.Payload.ValidationCorpusSha256, Is.EqualTo(SelectionCorpus));
            var personFirst = replay.Payload.UnitTfCalibrations!.Single(x =>
                x.Side == CompositeNominalTfSideV9.Person
                && x.Unit == NominalFrequencyUnitV9.FirstName);
            if (option.TfWeight > 0m)
            {
                Assert.That(personFirst.State, Is.EqualTo(V9SnapshotTfFitState.Active));
                Assert.That(personFirst.Calibration!.NominalUReference!.Probability,
                    Is.EqualTo(2.5m / 3m));
            }
            else
                Assert.That(personFirst.State, Is.EqualTo(V9SnapshotTfFitState.BaselineTf0));
            return (option.TfWeight, Metrics: metrics, Score: score);
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
        // The U_ref is fitted exclusively from the two nonmatch TRAIN events,
        // independent of the six weights and the sealed selection partition.
        // For MARIA, the pinned IBGE frequency is 0.60.
        const double expectedUref = (2d + .5d) / (2d + 2d * .5d);
        var baseline = outcomes[0].Score;
        foreach (var x in outcomes)
            Assert.That(x.Score,
                Is.EqualTo(baseline + (decimal)((double)x.TfWeight
                    * Math.Log(expectedUref / .60d))).Within(.00000005m),
                "All six TF weights must share TRAIN U_ref and the same frozen candidate frame.");
    }
}

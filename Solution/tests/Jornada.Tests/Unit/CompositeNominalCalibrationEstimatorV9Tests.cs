using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalCalibrationEstimatorV9Tests
{
    private static readonly DateOnly Birth = new(1990, 1, 12);

    private static IdentityTrainingPair Pair(string left, string right,
        string? motherLeft = null, string? motherRight = null)
        => new(left, Birth, motherLeft, right, Birth, motherRight);

    private static CompositeNominalTrainingFrameV9 Frame() => new(
        new[]
        {
            Pair("Maria Aparecida Santos", "MARIA APARECIDA SANTOS",
                "Ana da Costa", "ANA DA COSTA"),
            Pair("Maria Aparecida Santos", "Maria Cecília Santos", null, "Ana da Costa"),
            Pair("João Silva", "João Silva", "Maria de Souza", "Maria de Souza")
        },
        new[]
        {
            Pair("Maria Silva", "Maria Silvaa", "Ana da Costa", "Ana Albuquerque"),
            Pair("Maria Silva", "Ana Costa", null, "Ana da Costa"),
            Pair("João Silva", "Pedro Almeida", "Maria de Souza", "Beatriz de Souza")
        },
        "SHA256:blocking-frame-fixture",
        "TRAIN:independent-label-fixture",
        "TRAIN:partition-fixture");

    [Test]
    public void ProducesExclusiveObservedMAndUForPersonAndMother()
    {
        var result = CompositeNominalCalibrationEstimatorV9.Estimate(Frame(), 1m);
        Assert.That(result.Attributes, Has.Count.EqualTo(2));
        Assert.That(result.NominalContractVersion,
            Is.EqualTo(CompositeNominalComparisonV9.ContractVersion));
        Assert.That(result.BlockingFrameFingerprint, Is.EqualTo("SHA256:blocking-frame-fixture"));

        var person = result.Attributes.Single(x => x.Attribute == "NOME_V9");
        var mother = result.Attributes.Single(x => x.Attribute == "NOME_MAE_V9");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(person.MatchedObserved, Is.EqualTo(3));
            Assert.That(person.UnmatchedObserved, Is.EqualTo(3));
            Assert.That(person.MatchedMissing, Is.Zero);
            Assert.That(person.MatchedSupport[CompositeNominalLevelV9.ExactFull], Is.EqualTo(2));
            Assert.That(person.MatchedSupport[CompositeNominalLevelV9.ExactFirstLast], Is.EqualTo(1));
            Assert.That(person.UnmatchedSupport[CompositeNominalLevelV9.HighFull], Is.EqualTo(1));

            Assert.That(mother.MatchedObserved, Is.EqualTo(2));
            Assert.That(mother.UnmatchedObserved, Is.EqualTo(2));
            Assert.That(mother.MatchedMissing, Is.EqualTo(1));
            Assert.That(mother.UnmatchedMissing, Is.EqualTo(1));
            Assert.That(mother.MatchedSupport[CompositeNominalLevelV9.Missing], Is.EqualTo(1));
            Assert.That(mother.UnmatchedSupport[CompositeNominalLevelV9.Missing], Is.EqualTo(1));

            Assert.That(person.M.Values.Sum(), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(person.U.Values.Sum(), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(mother.M.Values.Sum(), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(mother.U.Values.Sum(), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(person.M.ContainsKey(CompositeNominalLevelV9.Missing), Is.False);
            Assert.That(mother.U.ContainsKey(CompositeNominalLevelV9.Missing), Is.False);
        }));
    }

    [Test]
    public void MissingIsSupportOnlyAndNeverFakeNegativeEvidence()
    {
        var frame = new CompositeNominalTrainingFrameV9(
            new[] { Pair("Maria Silva", "Maria Silva") },
            new[] { Pair("Maria Silva", "Ana Costa") },
            "blocking", "independent-labels", "train");
        // Neither class supplies a mother's name. An apparent zero contribution
        // must not be fabricated by smoothing an entirely missing field.
        Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(frame, 0.5m)),
            Throws.InvalidOperationException.With.Message.Contains("suporte observado"));
    }

    [Test]
    public void FailsClosedOnMissingFrameEvidenceOrClass()
    {
        var frame = Frame();
        var noBlocking = frame with { BlockingFrameFingerprint = " " };
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(
                noBlocking, 1m)), Throws.InvalidOperationException);
            Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(
                frame with { LabelProvenance = "" }, 1m)), Throws.InvalidOperationException);
            Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(
                frame with { TrainingPartitionFingerprint = "" }, 1m)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(
                frame with { UnmatchedPairs = Array.Empty<IdentityTrainingPair>() }, 1m)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => CompositeNominalCalibrationEstimatorV9.Estimate(
                frame, 0m)), Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    [Test]
    public void OutputsOnlyAggregatesAndProvenanceNotNamesOrUnapprovedTf()
    {
        var r = CompositeNominalCalibrationEstimatorV9.Estimate(Frame(), 1m);
        var names = r.Attributes.SelectMany(a => a.M.Keys.Select(k => k.ToString())).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(names, Does.Not.Contain("MARIA"));
            Assert.That(typeof(CompositeNominalCalibrationEstimateV9).GetProperties()
                .Any(p => p.Name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Person", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("TfWeight", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(typeof(CompositeNominalAttributeEstimateV9).GetProperties()
                .Any(p => p.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Tf", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(r.Attributes.Sum(a => a.MatchedMissing), Is.EqualTo(1));
        }));
    }
}

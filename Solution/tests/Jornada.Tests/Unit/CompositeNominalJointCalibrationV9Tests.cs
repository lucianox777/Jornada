using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalJointCalibrationV9Tests
{
    private static readonly DateOnly Birth = new(1989, 4, 5);

    private static IdentityTrainingPair Pair(
        string a, string b, string? motherA, string? motherB)
        => new(a, Birth, motherA, b, Birth, motherB);

    private static CompositeNominalTrainingFrameV9 Frame() => new(
        new[]
        {
            Pair("Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA"),
            Pair("Maria Clara Silva", "Maria Sofia Silva", null, "Ana Costa"),
            Pair("Joao Santos", "JOAO SANTOS", "Maria Santos", "Maria Santos")
        },
        new[]
        {
            // Adversarial non-match homonym: both person and mother can
            // agree exactly even when independent labels deny a match.
            Pair("Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA"),
            Pair("Maria Silva", "Maria Silvaa", null, "Ana Costa"),
            Pair("Joao Santos", "Jose Moreira", "Maria Lima", "Beatriz Souza")
        },
        "blocking:independent-frame", "independent-labeled-fixture", "train:disjoint-fixture");

    [Test]
    public void JointCellsAreExclusiveAndNormalizeWithinEachClass()
    {
        var result = CompositeNominalJointCalibrationEstimatorV9.Estimate(Frame(), 1m);
        var exact = result.Cells.Single(c =>
            c.Person == CompositeNominalLevelV9.ExactFull
            && c.Mother == CompositeNominalLevelV9.ExactFull);
        var missing = result.Cells.Single(c =>
            c.Person == CompositeNominalLevelV9.ExactFirstLast
            && c.Mother == CompositeNominalLevelV9.Missing);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.ContractVersion, Is.EqualTo(
                CompositeNominalJointCalibrationEstimatorV9.Version));
            Assert.That(result.Status, Is.EqualTo(
                CompositeNominalJointCalibrationEstimatorV9.Status));
            Assert.That(result.Cells, Has.Count.EqualTo(81));
            Assert.That(result.MatchedPairCount, Is.EqualTo(3));
            Assert.That(result.UnmatchedPairCount, Is.EqualTo(3));
            Assert.That(result.Cells.Sum(c => c.MatchedSupport), Is.EqualTo(3));
            Assert.That(result.Cells.Sum(c => c.UnmatchedSupport), Is.EqualTo(3));
            Assert.That(result.Cells.Sum(c => c.M), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(result.Cells.Sum(c => c.U), Is.EqualTo(1m).Within(0.000000000001m));
            Assert.That(exact.MatchedSupport, Is.EqualTo(2));
            Assert.That(exact.UnmatchedSupport, Is.EqualTo(1));
            Assert.That(exact.M, Is.EqualTo(3m / 84m));
            Assert.That(exact.U, Is.EqualTo(2m / 84m));
            Assert.That(missing.MatchedSupport, Is.EqualTo(1));
            Assert.That(missing.UnmatchedSupport, Is.Zero);
            Assert.That(result.BothClassesObservedCells, Is.EqualTo(1));
            Assert.That(result.ZeroClassSupportCells, Is.EqualTo(80));
        }));
    }

    [Test]
    public void MissingMotherIsAnExplicitJointCellAndNotFabricatedDisagreement()
    {
        var result = CompositeNominalJointCalibrationEstimatorV9.Estimate(Frame(), 0.5m);
        var missingInMatched = result.Cells.Where(c => c.Mother == CompositeNominalLevelV9.Missing)
            .Sum(c => c.MatchedSupport);
        var missingInUnmatched = result.Cells.Where(c => c.Mother == CompositeNominalLevelV9.Missing)
            .Sum(c => c.UnmatchedSupport);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(missingInMatched, Is.EqualTo(1));
            Assert.That(missingInUnmatched, Is.EqualTo(1));
            Assert.That(result.Cells.Count(c => c.Mother == CompositeNominalLevelV9.Missing),
                Is.EqualTo(9));
        }));
    }

    [Test]
    public void SupportGateNeverPromotesPseudocountOnlyCells()
    {
        var estimate = CompositeNominalJointCalibrationEstimatorV9.Estimate(Frame(), 1m);
        var supported = CompositeNominalJointCalibrationEstimatorV9.RequireObservedInBothClasses(
            estimate, CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull, 1);
        Assert.That(supported.MatchedSupport, Is.EqualTo(2));
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.RequireObservedInBothClasses(
                    estimate, CompositeNominalLevelV9.ExactFirstLast,
                    CompositeNominalLevelV9.Missing, 1)), Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.RequireObservedInBothClasses(
                    estimate, CompositeNominalLevelV9.ExactFull,
                    CompositeNominalLevelV9.ExactFull, 2)), Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.RequireObservedInBothClasses(
                    estimate, CompositeNominalLevelV9.ExactFull,
                    CompositeNominalLevelV9.ExactFull, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    [Test]
    public void IndependentLabelsAndBlockingProvenanceAreMandatory()
    {
        var frame = Frame();
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(
                    frame with { LabelProvenance = "" }, 1m)), Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(
                    frame with { BlockingFrameFingerprint = "" }, 1m)), Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(
                    frame with { TrainingPartitionFingerprint = "" }, 1m)), Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(
                    frame with { MatchedPairs = Array.Empty<IdentityTrainingPair>() }, 1m)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(frame, 0m)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() =>
                CompositeNominalJointCalibrationEstimatorV9.Estimate(frame, 1m, 4)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void JointEvidenceIsAggregateOnlyAndDoesNotProduceOperationalWeights()
    {
        var result = CompositeNominalJointCalibrationEstimatorV9.Estimate(Frame(), 1m);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("MARIA SILVA"));
            Assert.That(json, Does.Not.Contain("ANA COSTA"));
            Assert.That(json, Does.Not.Contain("SANTOS"));
            Assert.That(json, Does.Not.Contain("MatchedPairs\":\""));
            Assert.That(typeof(CompositeNominalJointCalibrationV9).GetProperties()
                .Any(p => p.Name.Contains("Weight", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Posterior", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Score", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Tf", StringComparison.OrdinalIgnoreCase)), Is.False);
        }));
    }

    [Test]
    public void BlockingAuditExposesJointWithoutPromotingV9Scorer()
    {
        var u = new BlockingConditionedUnmatchedPairSample(
            Frame().UnmatchedPairs,
            new Dictionary<string, long>(),
            20,
            Array.Empty<BlockingPassNominalUSupport>());
        var audit = CompositeNominalBlockingAuditBuilderV9.Build(
            Frame().MatchedPairs, u,
            new[] { LinkageBlockingPass.Create("TEST",
                [BlockingCandidateFeatureCatalog.FirstName]) },
            IdentityComparison.NormalizationVersion,
            "PROJECTION_FIXTURE", new string('a', 64), 1m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Status, Does.StartWith("DIAGNOSTIC_ONLY"));
            Assert.That(audit.Joint, Is.Not.Null);
            Assert.That(audit.Joint!.Cells, Has.Count.EqualTo(81));
            Assert.That(audit.Joint.Status, Does.Contain("NOT_CERTIFIED"));
            Assert.That(audit.MatchedLogicallyRetained, Is.Zero);
        }));
    }
}

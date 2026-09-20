using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class FsDecisionThresholdCalibrationTests
{
    [Test]
    public void Calibrate_SelectsZeroFalseLinkParetoCandidateFromValidationOnly()
    {
        var scenarios = new[]
        {
            Positive("v-pos", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000001",
                Candidate("00000000-0000-0000-0000-000000000001", .98m, 3m),
                Candidate("00000000-0000-0000-0000-000000000009", .70m, 1m)),
            Negative("v-neg", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000002",
                Candidate("00000000-0000-0000-0000-000000000009", .94m, 2m)),
            Positive("t-pos", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000003",
                Candidate("00000000-0000-0000-0000-000000000003", .99m, 3.2m),
                Candidate("00000000-0000-0000-0000-000000000008", .50m, .5m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000004",
                Candidate("00000000-0000-0000-0000-000000000008", .90m, 1.5m))
        };

        var result = FsDecisionThresholdCalibrator.Calibrate(
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(),
            scenarios,
            seed: 20260919,
            validationBasisPoints: 2000,
            testBasisPoints: 2000);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.Not.Null);
            Assert.That(result.Selected!.Candidate.Threshold, Is.EqualTo(.98m));
            Assert.That(result.Selected.Candidate.DualThresholdConflictFloor, Is.EqualTo(.70000001m),
                "O piso deve descer até imediatamente acima do segundo candidato positivo observado, sem alterar VALIDATION.");
            Assert.That(result.Selected.Validation.FalsePositive, Is.Zero);
            Assert.That(result.Selected.Validation.FalseNegative, Is.Zero);
            Assert.That(result.Selected.Test.FalsePositive, Is.Zero);
            Assert.That(result.TestSafetyPassed, Is.True);
        });

        var promoted = FsDecisionThresholdCalibrator.ApplySelected(Parameters(), result);
        Assert.Multiple(() =>
        {
            Assert.That(promoted[LinkageParameterCatalog.Threshold], Is.EqualTo(.98m));
            Assert.That(promoted[LinkageParameterCatalog.DualThresholdConflictFloorV2], Is.EqualTo(1m));
            Assert.That(promoted[LinkageParameterCatalog.DualThresholdConflictFloor], Is.EqualTo(.70000001m));
            Assert.That(promoted["FS_DECISION_THRESHOLD_PARETO_V1"], Is.EqualTo(1m));
            Assert.That(promoted["FS_DECISION_CALIBRATION_VALIDATION_FP"], Is.Zero);
            Assert.That(promoted["FS_DECISION_CALIBRATION_TEST_FP"], Is.Zero);
        });
    }

    [Test]
    public void Calibrate_TestNeverChangesFrozenValidationSelectionButCanBlockPromotion()
    {
        var validation = new[]
        {
            Positive("v-pos", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000011",
                Candidate("00000000-0000-0000-0000-000000000011", .98m, 3m)),
            Negative("v-neg", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000012",
                Candidate("00000000-0000-0000-0000-000000000019", .94m, 2m))
        };
        var safeTest = new[]
        {
            Positive("t-pos", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000013",
                Candidate("00000000-0000-0000-0000-000000000013", .99m, 3m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000014",
                Candidate("00000000-0000-0000-0000-000000000019", .90m, 1m))
        };
        var unsafeTest = new[]
        {
            Positive("t-pos", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000013",
                Candidate("00000000-0000-0000-0000-000000000013", .99m, 3m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000014",
                Candidate("00000000-0000-0000-0000-000000000019", .99m, 3m))
        };

        var a = FsDecisionThresholdCalibrator.Calibrate(
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(),
            validation.Concat(safeTest).ToArray(),
            20260919, 2000, 2000);
        var b = FsDecisionThresholdCalibrator.Calibrate(
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(),
            validation.Concat(unsafeTest).ToArray(),
            20260919, 2000, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(a.Selected, Is.Not.Null);
            Assert.That(b.Selected, Is.Not.Null);
            Assert.That(b.Selected!.Candidate.CandidateId, Is.EqualTo(a.Selected!.Candidate.CandidateId),
                "TEST não pode retroalimentar threshold/margem congelados em VALIDATION.");
            Assert.That(a.TestSafetyPassed, Is.True);
            Assert.That(b.TestSafetyPassed, Is.False);
            Assert.That(
                () => FsDecisionThresholdCalibrator.ApplySelected(Parameters(), b),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void Partition_IsDeterministicAndKeepsOneBasePersonInOnePartition()
    {
        var uuid = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var first = FsDecisionThresholdCalibrator.Partition(uuid, 20260919, 2000, 2000);

        for (var i = 0; i < 20; i++)
            Assert.That(
                FsDecisionThresholdCalibrator.Partition(uuid, 20260919, 2000, 2000),
                Is.EqualTo(first));
    }

    private static FsDecisionCalibrationScenario Positive(
        string id,
        FsDecisionCalibrationPartition partition,
        string truth,
        params FsDecisionRankedCandidate[] candidates)
    {
        var uuid = Guid.Parse(truth);
        return new FsDecisionCalibrationScenario(id, uuid, uuid, partition, candidates);
    }

    private static FsDecisionCalibrationScenario Negative(
        string id,
        FsDecisionCalibrationPartition partition,
        string basePerson,
        params FsDecisionRankedCandidate[] candidates) =>
        new(id, Guid.Parse(basePerson), null, partition, candidates);

    private static FsDecisionRankedCandidate Candidate(string uuid, decimal posterior, decimal logOdds) =>
        new(Guid.Parse(uuid), posterior, logOdds);

    private static IReadOnlyDictionary<string, decimal> Parameters()
    {
        var matched = new[]
        {
            new IdentityTrainingPair(
                "MARIA SILVA", new DateOnly(1980, 1, 2), "ANA SILVA",
                "MARIA SILVA", new DateOnly(1980, 1, 2), "ANA SILVA")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair(
                "MARIA SILVA", new DateOnly(1980, 1, 2), "ANA SILVA",
                "JOAO SOUZA", new DateOnly(1970, 3, 4), "TEREZA SOUZA")
        };

        return LinkageParameterEstimator.Estimate(
            matched,
            unmatched,
            populationSize: 1000,
            distinctBirthDates: 365,
            smoothingAlpha: .5m,
            threshold: .5m,
            conflictMargin: .01m);
    }
}

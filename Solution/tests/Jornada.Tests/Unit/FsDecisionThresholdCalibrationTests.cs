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
            Assert.That(result.TestWrongPersonFalsePositive, Is.Zero);
            Assert.That(result.TestLeaveTruthOutFalsePositive, Is.Zero);
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
            Assert.That(b.TestWrongPersonFalsePositive, Is.Zero);
            Assert.That(b.TestLeaveTruthOutFalsePositive, Is.EqualTo(1));
            var failure = Assert.Throws<InvalidOperationException>(
                () => FsDecisionThresholdCalibrator.ApplySelected(Parameters(), b));
            Assert.That(failure!.Message, Does.Contain("falsePositive=1"));
            Assert.That(failure.Message, Does.Contain("fpPessoaErrada=0"));
            Assert.That(failure.Message, Does.Contain("fpLeaveTruthOut=1"));
            Assert.That(failure.Message, Does.Contain("Nenhum threshold foi promovido."));
            Assert.That(failure.Message.Length, Is.LessThanOrEqualTo(500),
                "A evidência deve caber em identidade.modelo_linkage.falha_resumo.");
        });
    }

    [Test]
    public void Failure_diagnostics_separate_wrong_person_from_leave_truth_out_without_retuning_on_TEST()
    {
        var validation = new[]
        {
            Positive("v-pos", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000031",
                Candidate("00000000-0000-0000-0000-000000000031", .98m, 3m)),
            Negative("v-neg", FsDecisionCalibrationPartition.Validation, "00000000-0000-0000-0000-000000000032",
                Candidate("00000000-0000-0000-0000-000000000039", .94m, 2m))
        };
        var safe = validation.Concat(new[]
        {
            Positive("t-pos", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000033",
                Candidate("00000000-0000-0000-0000-000000000033", .99m, 3m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000034",
                Candidate("00000000-0000-0000-0000-000000000039", .90m, 1m))
        }).ToArray();
        var unsafePositive = validation.Concat(new[]
        {
            Positive("t-pos", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000033",
                Candidate("00000000-0000-0000-0000-000000000039", .99m, 3m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test, "00000000-0000-0000-0000-000000000034",
                Candidate("00000000-0000-0000-0000-000000000039", .90m, 1m))
        }).ToArray();

        var normal = FsDecisionThresholdCalibrator.Calibrate(
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(), safe, 20260919, 2000, 2000);
        var unsafeResult = FsDecisionThresholdCalibrator.Calibrate(
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(), unsafePositive, 20260919, 2000, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(unsafeResult.Selected, Is.Not.Null);
            Assert.That(unsafeResult.Selected!.Candidate.CandidateId,
                Is.EqualTo(normal.Selected!.Candidate.CandidateId),
                "A auditoria de TEST não deve influenciar a seleção feita com VALIDATION.");
            Assert.That(unsafeResult.TestWrongPersonFalsePositive, Is.EqualTo(1));
            Assert.That(unsafeResult.TestLeaveTruthOutFalsePositive, Is.Zero);
            Assert.That(unsafeResult.Selected.Test.FalsePositive, Is.EqualTo(1));
            Assert.That(unsafeResult.TestSafetyPassed, Is.False);
        });
        var exception = Assert.Throws<InvalidOperationException>(
            () => FsDecisionThresholdCalibrator.ApplySelected(Parameters(), unsafeResult));
        Assert.That(exception!.Message, Does.Contain("fpPessoaErrada=1"));
        Assert.That(exception.Message, Does.Contain("fpLeaveTruthOut=0"));
        Assert.That(exception.Message.Length, Is.LessThanOrEqualTo(500));
    }

    [Test]
    public void Positive_fp_budget_selects_lower_fn_on_validation_and_test_does_not_retune()
    {
        // O negativo mais pontuado que o positivo força a fronteira FP/FN:
        // zero FP deixa o positivo sem vínculo; aceitar um FP recupera o vínculo.
        var validation = new[]
        {
            Positive("v-pos", FsDecisionCalibrationPartition.Validation,
                "00000000-0000-0000-0000-000000000061",
                Candidate("00000000-0000-0000-0000-000000000061", .98m, 3m)),
            Negative("v-neg", FsDecisionCalibrationPartition.Validation,
                "00000000-0000-0000-0000-000000000062",
                Candidate("00000000-0000-0000-0000-000000000069", .995m, 4m))
        };
        var testWithFp = new[]
        {
            Positive("t-pos", FsDecisionCalibrationPartition.Test,
                "00000000-0000-0000-0000-000000000063",
                Candidate("00000000-0000-0000-0000-000000000063", .99m, 3.2m)),
            Negative("t-neg", FsDecisionCalibrationPartition.Test,
                "00000000-0000-0000-0000-000000000064",
                Candidate("00000000-0000-0000-0000-000000000069", .999m, 5m))
        };
        var testWithoutFp = new[]
        {
            testWithFp[0],
            Negative("t-neg", FsDecisionCalibrationPartition.Test,
                "00000000-0000-0000-0000-000000000064",
                Candidate("00000000-0000-0000-0000-000000000069", .50m, .1m))
        };
        FsDecisionThresholdCalibrationResult Run(
            IReadOnlyList<FsDecisionCalibrationScenario> test, int validationBp = 100) =>
            FsDecisionThresholdCalibrator.Calibrate(
                LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
                Parameters(), validation.Concat(test).ToArray(),
                20260926, 2000, 2000,
                maxFpValidationBasisPoints: validationBp,
                maxFpTestBasisPoints: 100);

        var withFp = Run(testWithFp);
        var withoutFp = Run(testWithoutFp);
        Assert.Multiple(() =>
        {
            Assert.That(withFp.Selected, Is.Not.Null);
            Assert.That(withFp.MaxFpValidationAbsolute, Is.EqualTo(1));
            Assert.That(withFp.MaxFpTestAbsolute, Is.EqualTo(1));
            Assert.That(withFp.Selected!.Validation.FalsePositive, Is.EqualTo(1));
            Assert.That(withFp.Selected.Validation.FalseNegative, Is.Zero);
            Assert.That(withFp.Selected.Test.FalsePositive, Is.EqualTo(1));
            Assert.That(withFp.TestLeaveTruthOutFalsePositive, Is.EqualTo(1));
            Assert.That(withFp.TestSafetyPassed, Is.True);
            Assert.That(withFp.Selected.Candidate.CandidateId,
                Is.EqualTo(withoutFp.Selected!.Candidate.CandidateId),
                "TEST não escolhe o threshold após validar a fronteira.");
        });

        var strict = Run(testWithFp, validationBp: 0);
        Assert.That(strict.Selected, Is.Not.Null);
        Assert.That(strict.Selected!.Validation.FalsePositive, Is.Zero);
        Assert.That(strict.Selected.Validation.FalseNegative, Is.EqualTo(1));
        var originalCall = Assert.Throws<InvalidOperationException>(
            () => FsDecisionThresholdCalibrator.ApplySelected(Parameters(), withFp));
        Assert.That(originalCall!.Message, Does.Contain("Limite TEST divergente"));
        var applied = FsDecisionThresholdCalibrator.ApplySelected(
            Parameters(), withFp, maxFpTest: withFp.MaxFpTestAbsolute);
        Assert.Multiple(() =>
        {
            Assert.That(applied["FS_DECISION_CALIBRATION_MAX_FP_VALIDATION_BP"], Is.EqualTo(100m));
            Assert.That(applied["FS_DECISION_CALIBRATION_MAX_FP_TEST_BP"], Is.EqualTo(100m));
            Assert.That(applied["FS_DECISION_CALIBRATION_VALIDATION_FP_LIMIT"], Is.EqualTo(1m));
            Assert.That(applied["FS_DECISION_CALIBRATION_TEST_FP_LIMIT"], Is.EqualTo(1m));
            Assert.That(applied["FS_DECISION_CALIBRATION_TEST_FP_LEAVE_TRUTH_OUT"], Is.EqualTo(1m));
            Assert.That(applied["FS_DECISION_CALIBRATION_TEST_FP_EFFECTIVE_CAP_BP"], Is.EqualTo(5000m),
                "Amostra pequena: o teto discreto não é uma taxa real de 1%.");
        });
    }

    [Test]
    public void Fp_budget_uses_all_labeled_scenarios_and_validates_bounds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FsDecisionThresholdCalibrator.FalsePositiveBudget(2, 100), Is.EqualTo(1));
            Assert.That(FsDecisionThresholdCalibrator.FalsePositiveBudget(200, 100), Is.EqualTo(2));
            Assert.That(FsDecisionThresholdCalibrator.FalsePositiveBudget(299, 100), Is.EqualTo(3));
            Assert.That(FsDecisionThresholdCalibrator.FalsePositiveBudget(299, 0), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FsDecisionThresholdCalibrator.FalsePositiveBudget(0, 100));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FsDecisionThresholdCalibrator.FalsePositiveBudget(10, 10001));
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

using Jornada.Linkage.Evaluation;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt15SyntheticPairedComparisonTests
{
    private static readonly Guid ActiveId =
        Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid DraftId =
        Guid.Parse("d1111111-1111-4111-8111-111111111111");

    [Test]
    public void Same_corpus_and_partition_compares_frozen_FS_outcomes_not_a_historical_run()
    {
        var result = Dt15SyntheticPairedComparison.Compare(Facts(true), Facts(false));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Dt15SyntheticPairedComparison.Comparable));
            Assert.That(result.Purpose, Is.EqualTo("ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE"));
            Assert.That(result.Active.ModelId, Is.EqualTo(ActiveId));
            Assert.That(result.Draft.ModelId, Is.EqualTo(DraftId));
            Assert.That(result.Blocking!.TrueInterSourcePairs, Is.EqualTo(20L));
            Assert.That(result.Blocking.ActiveTruePairsRetained, Is.EqualTo(16L));
            Assert.That(result.Blocking.DraftTruePairsRetained, Is.EqualTo(18L));
            Assert.That(result.Blocking.RecallDelta, Is.EqualTo(.1m));
            Assert.That(result.Blocking.ReductionDelta, Is.EqualTo(-.04m));
            Assert.That(result.Validation!.FalsePositiveDelta, Is.EqualTo(1));
            Assert.That(result.Validation.FalseNegativeDelta, Is.EqualTo(-1));
            Assert.That(result.Test!.FalsePositiveDelta, Is.EqualTo(1));
            Assert.That(result.Test.FalseNegativeDelta, Is.EqualTo(0));
            Assert.That(result.Strata, Has.Count.EqualTo(2));
            Assert.That(result.Safeguards, Has.Some.Contains("no score").IgnoreCase);
        }));
    }

    [Test]
    public void Different_truth_or_partitions_must_not_emit_a_fake_delta()
    {
        var active = Facts(true);
        var draft = Facts(false);
        var changedTruth = draft with
        {
            Corpus = draft.Corpus with { TruthSha256 = new string('9', 64) }
        };
        var changedSeed = draft with
        {
            Partition = draft.Partition! with { Seed = 10 }
        };
        var changedDenominator = draft with { TrueInterSourcePairs = 19 };
        foreach (var altered in new[] { changedTruth, changedSeed, changedDenominator })
        {
            var result = Dt15SyntheticPairedComparison.Compare(active, altered);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(result.Status, Is.EqualTo(Dt15SyntheticPairedComparison.NotComparable));
                Assert.That(result.Blocking, Is.Null);
                Assert.That(result.Validation, Is.Null);
                Assert.That(result.Test, Is.Null);
                Assert.That(result.Strata, Is.Null);
            }));
        }
    }

    [Test]
    public void Absent_FS_evidence_or_invalid_fingerprint_marks_incomplete()
    {
        var active = Facts(true);
        var draft = Facts(false);
        var noOracle = Dt15SyntheticPairedComparison.Compare(active, draft with { Decision = null });
        var noFingerprint = Dt15SyntheticPairedComparison.Compare(active,
            draft with { Reference = draft.Reference with { SnapshotSha256 = "" } });
        Assert.Multiple((Action)(() =>
        {
            Assert.That(noOracle.Status, Is.EqualTo(Dt15SyntheticPairedComparison.Incomplete));
            Assert.That(noOracle.Validation, Is.Null);
            Assert.That(noFingerprint.Status, Is.EqualTo(Dt15SyntheticPairedComparison.Incomplete));
            Assert.That(noFingerprint.Blocking, Is.Null);
        }));
    }

    [Test]
    public void Missing_or_different_stratum_denominator_blocks_comparison()
    {
        var active = Facts(true);
        var draft = Facts(false);
        var tampered = draft with
        {
            QualitySlices = draft.QualitySlices.Select(x =>
                x.Partition == "TEST" ? x with { Total = 9 } : x).ToArray()
        };
        var result = Dt15SyntheticPairedComparison.Compare(active, tampered);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Dt15SyntheticPairedComparison.NotComparable));
            Assert.That(result.Strata, Is.Null);
            Assert.That(result.Validation, Is.Null);
        }));
    }

    [Test]
    public void Fp_from_wrong_positive_can_overlap_fn_without_changing_the_truth_denominator()
    {
        // FS EvaluateFrozen counts the same wrong positive as FP *and* FN.
        // The truth denominator is TP+FN, not TN+FP.
        var active = Facts(true);
        var draft = Facts(false);
        var altered = draft with
        {
            Decision = draft.Decision! with
            {
                ModelValidation = new SyntheticDecisionObjective(4, 3, 2, 1, 1, 10)
            },
            QualitySlices = draft.QualitySlices.Select(x =>
                x.Partition == "VALIDATION"
                    ? x with { TruePositive = 4, FalsePositive = 2, FalseNegative = 1 }
                    : x).ToArray()
        };
        var result = Dt15SyntheticPairedComparison.Compare(active, altered);
        Assert.That(result.Status, Is.EqualTo(Dt15SyntheticPairedComparison.Comparable));
        Assert.That(result.Validation!.FalsePositiveDelta, Is.EqualTo(1));
    }

    [Test]
    public void Never_accept_two_drafts_as_a_paired_active_baseline()
    {
        var active = Facts(true);
        var fake = active with { Reference = active.Reference with { Status = "RASCUNHO" } };
        Assert.That(Dt15SyntheticPairedComparison.Compare(fake, Facts(false)).Status,
            Is.EqualTo(Dt15SyntheticPairedComparison.NotComparable));
    }

    private static Dt15SyntheticRunFacts Facts(bool active)
    {
        var model = new Dt15SyntheticModelReference(
            active ? ActiveId : DraftId, active ? 3 : 4,
            active ? "ATIVO" : "RASCUNHO",
            "FS_DECISION_V6", "IDENTITY_NORMALIZATION_V1",
            new string(active ? 'a' : 'b', 64),
            new string(active ? 'c' : 'd', 64));
        var corpus = new Dt15SyntheticCorpusReference(
            77UL, new string('2', 64), new string('e', 64),
            new string('f', 64), new string('0', 64),
            new string('1', 64), 100);
        var partition = new Dt15PartitionContract(12345, 2000, 2000);
        var validation = active
            ? new SyntheticDecisionObjective(4, 4, 1, 1, 1, 10)
            : new SyntheticDecisionObjective(5, 3, 2, 0, 0, 10);
        var test = active
            ? new SyntheticDecisionObjective(3, 4, 0, 1, 1, 8)
            : new SyntheticDecisionObjective(3, 3, 1, 1, 1, 8);
        var slices = new[]
        {
            new SyntheticDecisionQualitySlice(
                "VALIDATION", "ALL",
                validation.TruePositive, validation.FalsePositive,
                validation.FalseNegative, validation.Inconclusive, validation.Total,
                .8m, .8m),
            new SyntheticDecisionQualitySlice(
                "TEST", "ALL",
                test.TruePositive, test.FalsePositive,
                test.FalseNegative, test.Inconclusive, test.Total, .8m, .8m)
        };
        var oracle = new SyntheticThresholdOracle(
            Status: "EVALUATED",
            CalibrationPolicyVersion: "FS_CALIBRATION_V1",
            ScenarioCount: 18,
            ValidationScenarioCount: 10,
            TestScenarioCount: 8,
            ModelThreshold: .95m,
            ModelConflictMarginLogOdds: .1m,
            ModelConflictFloor: .8m,
            OracleThreshold: null,
            OracleConflictMarginLogOdds: null,
            OracleConflictFloor: null,
            ModelValidation: validation,
            ModelTest: test,
            OracleValidation: null,
            OracleTest: null,
            ValidationFrontierL1Distance: null,
            CoordinatesComparable: false,
            ThresholdAbsoluteDelta: null,
            ConflictMarginAbsoluteDelta: null,
            ConflictFloorAbsoluteDelta: null,
            ModelQuality: slices);
        return new Dt15SyntheticRunFacts(
            model, corpus, "Development", partition,
            TrueInterSourcePairs: 20, PossibleNonMatchPairs: 200,
            TruePairsRetained: active ? 16 : 18,
            CandidateUnionPairs: active ? 40 : 50,
            BlockingRecall: active ? .8m : .9m,
            ReductionRatio: active ? .88m : .84m,
            Decision: oracle,
            QualitySlices: slices);
    }
}

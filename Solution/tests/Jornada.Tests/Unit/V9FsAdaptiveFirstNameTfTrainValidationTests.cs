using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FsAdaptiveFirstNameTfTrainValidationTests
{
    private static readonly string TrainCorpus = new('a', 64);
    private static readonly string ValidationCorpus = new('b', 64);
    private static readonly string TrainGroups = new('c', 64);
    private static readonly string ValidationGroups = new('d', 64);
    private static readonly DateOnly Birth = new(1980, 1, 2);

    private static FrozenIbgeNominalTfV9 Reference()
    {
        var entries = new[]
        {
            new NominalTermFrequencyEntry("NOME_PRENOME", "MARIA", 60, 100, .60m),
            new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "ANA", 30, 100, .30m)
        };
        return FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(entries), entries, 123),
            "IBGE_PINNED", new string('e', 64), "BLOCK_SQL_PINNED",
            new NominalSyntheticIbgeBootstrapV9(
                CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                30_000, "CORPUS_30K", "GENERATOR_V1", 42UL, "HYPOTHESES_V1"));
    }

    private static V9FsAdaptiveCandidatePair Pair(
        string split, string left, string right, bool same,
        string first = "MARIA") =>
        new(split, left, right, same,
            first + " COSTA", first + " LIMA",
            null, null, Birth, Birth, 2);

    private static V9FsAdaptiveCandidateFrame Frame() =>
        new(TrainCorpus, ValidationCorpus, TrainGroups, ValidationGroups,
        [
            Pair("TRAIN", "TRAIN-A", "TRAIN-A", true),
            Pair("TRAIN", "TRAIN-B", "TRAIN-B", true),
            Pair("TRAIN", "TRAIN-C", "TRAIN-D", false),
            Pair("TRAIN", "TRAIN-D", "TRAIN-C", false),
            Pair("VALIDATION", "VAL-A", "VAL-A", true),
            Pair("VALIDATION", "VAL-B", "VAL-B", true),
            Pair("VALIDATION", "VAL-C", "VAL-D", false),
            Pair("VALIDATION", "VAL-D", "VAL-C", false)
        ]);

    private static AdaptiveCalibrationSlice Slice(string split) =>
        split == "TRAIN"
            ? new(split, TrainCorpus, TrainGroups, 4, true, false)
            : new(split, ValidationCorpus, ValidationGroups, 4, true, false);

    private static AdaptiveCalibrationModelOption Model(V9FsAdaptiveOptionConfiguration c) =>
        new(V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            c.OptionId, c.FingerprintSha256);

    [Test]
    public void Positive_weight_changes_real_V9_FS_validation_with_train_SQL_U_and_frozen_IBGE()
    {
        var neutral = new V9FsAdaptiveOptionConfiguration("NO_TF", .5m, .55m);
        var positive = new V9FsAdaptiveOptionConfiguration("FULL_TF", .5m, .55m, 1m);
        var fractional = new V9FsAdaptiveOptionConfiguration("HALF_TF", .5m, .55m, .5m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            Frame(), [neutral, positive, fractional], Reference());

        AdaptiveCalibrationHoldoutMetrics Validate(V9FsAdaptiveOptionConfiguration config)
        {
            var fitted = adapter.FitTrain(Model(config), Slice("TRAIN"));
            return adapter.EvaluateValidation(fitted, Slice("VALIDATION"));
        }
        var no = Validate(neutral);
        var half = Validate(fractional);
        var yes = Validate(positive);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(no.TrueAcceptedLinks, Is.Zero);
            Assert.That(no.FalseAcceptedLinks, Is.Zero);
            Assert.That(half.TrueAcceptedLinks, Is.Zero);
            Assert.That(yes.TrueAcceptedLinks, Is.EqualTo(2));
            Assert.That(yes.FalseAcceptedLinks, Is.EqualTo(2));
            Assert.That(yes.TruePairsWithoutCpf, Is.EqualTo(2));
            Assert.That(neutral.FingerprintSha256, Is.Not.EqualTo(positive.FingerprintSha256));
            Assert.That(fractional.FingerprintSha256, Is.Not.EqualTo(positive.FingerprintSha256));
        }));
    }

    [Test]
    public void Train_U_reference_counts_exact_first_values_and_refuses_test_leakage()
    {
        var frame = Frame();
        var fitted = V9TrainSqlFirstNameUCalibration.Fit(
            frame.Pairs.Where(x => x.Partition == "TRAIN").ToArray(),
            CompositeNominalTfSideV9.Person, .5m, TrainCorpus);
        Assert.That(fitted.UnmatchedExactFirstCandidatePairs, Is.EqualTo(2));
        Assert.That(fitted.FirstNameUnitUByValue["MARIA"], Is.EqualTo(2.5m/3m));
        Assert.That(fitted.FingerprintSha256, Has.Length.EqualTo(64));
        Assert.That((Action)(() => V9TrainSqlFirstNameUCalibration.Fit(
            frame.Pairs, CompositeNominalTfSideV9.Person, .5m, TrainCorpus)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Positive_TF_requires_pinned_snapshot_known_term_and_enough_train_U_support()
    {
        var config = new V9FsAdaptiveOptionConfiguration("TF", .5m, .55m, 1m);
        Assert.That((Action)(() =>
            new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [config])),
            Throws.TypeOf<ArgumentException>());
        Assert.That((Action)(() =>
            new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(),
                [config with { TfWeight = 1.01m }], Reference())),
            Throws.TypeOf<ArgumentException>());

        var unsupportedFrame = Frame() with
        {
            Pairs = Frame().Pairs.Select(x => x.Partition == "TRAIN" && !x.IsTruePair
                ? x with { RightPersonName = "OUTRO SOBRENOME" } : x).ToArray()
        };
        var unsupported = new V9FellegiSunterAdaptiveCalibrationAdapter(
            unsupportedFrame, [config], Reference());
        Assert.That((Action)(() => unsupported.FitTrain(Model(config), Slice("TRAIN"))),
            Throws.TypeOf<InvalidDataException>());

        var unknownFrame = Frame() with
        {
            Pairs = Frame().Pairs.Select(x => x.Partition == "VALIDATION"
                && x.LeftIdentityGroupId == "VAL-A"
                ? x with { LeftPersonName = "ZULEICA COSTA",
                           RightPersonName = "ZULEICA LIMA" } : x).ToArray()
        };
        var unknown = new V9FellegiSunterAdaptiveCalibrationAdapter(
            unknownFrame, [config], Reference());
        var fit = unknown.FitTrain(Model(config), Slice("TRAIN"));
        Assert.That((Action)(() => unknown.EvaluateValidation(fit, Slice("VALIDATION"))),
            Throws.TypeOf<InvalidDataException>());
    }
}

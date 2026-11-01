using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FsAdaptiveFirstNameTfTrainValidationTests
{
    // One parameterized regression protects the shared TRAIN/runtime selector;
    // do not duplicate these nine cases in separate test fixtures.
    [TestCase(CompositeNominalLevelV9.Missing, null)]
    [TestCase(CompositeNominalLevelV9.ExactFull, NominalFrequencyUnitV9.FirstName)]
    [TestCase(CompositeNominalLevelV9.ExactFirstLast, null)]
    [TestCase(CompositeNominalLevelV9.ReversedFirstLast, null)]
    [TestCase(CompositeNominalLevelV9.HighFull, null)]
    [TestCase(CompositeNominalLevelV9.HighFirstLast, null)]
    [TestCase(CompositeNominalLevelV9.ExactLast, NominalFrequencyUnitV9.LastContentSurname)]
    [TestCase(CompositeNominalLevelV9.ExactFirst, NominalFrequencyUnitV9.FirstName)]
    [TestCase(CompositeNominalLevelV9.Other, null)]
    public void Shared_TF_selector_covers_each_nominal_state_without_duplicate_scoring(
        CompositeNominalLevelV9 level, NominalFrequencyUnitV9? expected)
    {
        Assert.That(V9NominalTfUnitSelection.ForLevel(level), Is.EqualTo(expected));
        if (expected.HasValue)
            Assert.That(V9NominalTfUnitSelection.MarginalLevel(expected.Value),
                Is.EqualTo(expected == NominalFrequencyUnitV9.FirstName
                    ? CompositeNominalLevelV9.ExactFirst
                    : CompositeNominalLevelV9.ExactLast));
    }

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
            "IBGE_PINNED", new string('e', 64), new string('1', 64),
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
        ], CandidateBlockingPasses: [new LinkageBlockingPass("NAME", ["NOME"])],
        ProjectionSchemaVersion: "PROJECTION_V1", ProjectionFingerprintSha256: new string('f', 64),
        BlockingFrameFingerprintSha256: new string('1', 64));

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
    public void Export_freezes_the_actual_train_fit_and_never_drops_positive_TF()
    {
        var neutral = new V9FsAdaptiveOptionConfiguration("TF0", .5m, .55m);
        var positive = new V9FsAdaptiveOptionConfiguration("TF1", .5m, .55m, 1m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            Frame(), [neutral, positive], Reference());
        var fit = adapter.FitTrain(Model(neutral), Slice("TRAIN"));
        var passes = new[] { new LinkageBlockingPass("NAME", ["NOME"]) };
        var snapshot = adapter.ExportFrozenSnapshot(fit, passes, "PROJECTION_V1",
            new string('f', 64), .2m);
        var replay = V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(replay.Payload.FitFingerprintSha256,
                Is.EqualTo(fit.FittedParametersFingerprintSha256));
            Assert.That(replay.Payload.TrainCorpusSha256, Is.EqualTo(TrainCorpus));
            Assert.That(replay.Payload.JointCells, Has.Count.EqualTo(81));
            Assert.That(replay.Payload.Threshold, Is.EqualTo(neutral.DecisionThreshold));
            Assert.That(replay.Payload.TfWeight, Is.Zero);
        }));
        var positiveFit = adapter.FitTrain(Model(positive), Slice("TRAIN"));
        var positiveSnapshot = adapter.ExportFrozenSnapshot(positiveFit,
            passes, "PROJECTION_V1", new string('f', 64), .2m);
        var positiveReplay = V9FittedSnapshot.Load(positiveSnapshot.SerializeUtf8(),
            positiveSnapshot.ContentSha256);
        var activeTf = positiveReplay.Payload.UnitTfCalibrations!
            .Single(x => x.Side == CompositeNominalTfSideV9.Person
                && x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.That(activeTf.State, Is.EqualTo(V9SnapshotTfFitState.Active));
        Assert.That(activeTf.Calibration, Is.Not.Null);
        Assert.That(activeTf.Calibration!.Frequencies["MARIA"], Is.EqualTo(.60m));
        Assert.That(activeTf.Calibration.TrainUnitUByValue["MARIA"],
            Is.EqualTo(2.5m / 3m));
        var source = new IdentityObservation(null, "SEM_CPF", "MARIA COSTA", Birth, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA LIMA", Birth, null);
        var neutralDecision = replay.ResolvePreview(Guid.NewGuid(), source, [candidate]);
        var positiveDecision = positiveReplay.ResolvePreview(Guid.NewGuid(), source, [candidate]);
        Assert.That(neutralDecision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        Assert.That(positiveDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(positiveDecision.PessoaUuidResolvido, Is.EqualTo(candidate.PersonUuid));
        Assert.That((Action)(() => adapter.ExportFrozenSnapshot(
            fit with { OptionId = "TF1" }, passes, "PROJECTION_V1", new string('f', 64), .2m)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => adapter.ExportFrozenSnapshot(fit,
            [new LinkageBlockingPass("OTHER", ["NOME"])],
            "PROJECTION_V1", new string('f', 64), .2m)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => adapter.ExportFrozenSnapshot(fit, passes,
            "PROJECTION_V1", new string('a', 64), .2m)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Insufficient_TRAIN_U_fingerprint_is_sensitive_to_smoothing_alpha()
    {
        var rows = Frame().Pairs.Where(x => x.Partition == "TRAIN").ToArray();
        var first = V9TrainSqlFirstNameUCalibration.Fit(
            rows, CompositeNominalTfSideV9.Mother, .5m, TrainCorpus);
        var second = V9TrainSqlFirstNameUCalibration.Fit(
            rows, CompositeNominalTfSideV9.Mother, 1m, TrainCorpus);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(first.HasSufficientSupport, Is.False);
            Assert.That(second.HasSufficientSupport, Is.False);
            Assert.That(first.FingerprintSha256, Is.Not.EqualTo(second.FingerprintSha256));
        }));
    }

    [Test]
    public void Pinned_IBGE_marginal_availability_is_typed_and_unit_specific()
    {
        var reference = Reference();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(reference.HasMarginal(CompositeNominalTfSideV9.Person,
                CompositeNominalLevelV9.ExactFirst), Is.True);
            Assert.That(reference.HasMarginal(CompositeNominalTfSideV9.Mother,
                CompositeNominalLevelV9.ExactFirst), Is.True);
            Assert.That(reference.HasMarginal(CompositeNominalTfSideV9.Person,
                CompositeNominalLevelV9.ExactLast), Is.False);
            Assert.That(reference.HasMarginal(CompositeNominalTfSideV9.Mother,
                CompositeNominalLevelV9.ExactLast), Is.False);
        }));
        Assert.That((Action)(() => reference.HasMarginal(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFull)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Partial_TRAIN_support_exports_one_active_unit_and_three_insufficient_units()
    {
        var config = new V9FsAdaptiveOptionConfiguration("PARTIAL_TF", .5m, .55m, 1m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            Frame(), [config], Reference());
        var fit = adapter.FitTrain(Model(config), Slice("TRAIN"));
        var snapshot = adapter.ExportFrozenSnapshot(fit,
            [new LinkageBlockingPass("NAME", ["NOME"])],
            "PROJECTION_V1", new string('f', 64), .2m);
        var replay = V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
        var units = replay.Payload.UnitTfCalibrations!;
        Assert.That(units, Has.Count.EqualTo(4));
        Assert.That(units.Single(x => x.Side == CompositeNominalTfSideV9.Person
            && x.Unit == NominalFrequencyUnitV9.FirstName).State,
            Is.EqualTo(V9SnapshotTfFitState.Active));
        Assert.That(units.Single(x => x.Side == CompositeNominalTfSideV9.Mother
            && x.Unit == NominalFrequencyUnitV9.FirstName).State,
            Is.EqualTo(V9SnapshotTfFitState.InsufficientSupport));
        Assert.That(units.Count(x => x.State == V9SnapshotTfFitState.NoSource),
            Is.EqualTo(2));
        Assert.That(units.Where(x => x.State != V9SnapshotTfFitState.Active)
            .All(x => x.Calibration is null), Is.True);
    }

    [Test]
    public void Positive_TF_cannot_use_reference_from_another_blocking_frame()
    {
        var config = new V9FsAdaptiveOptionConfiguration("TF", .5m, .55m, 1m);
        Assert.That((Action)(() => _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
            Frame() with { BlockingFrameFingerprintSha256 = new string('2', 64) },
            [config], Reference())), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Pair_only_fixture_cannot_invent_blocking_provenance_when_exporting()
    {
        var config = new V9FsAdaptiveOptionConfiguration("TF0", .5m, .55m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame() with
        {
            CandidateBlockingPasses = null, ProjectionSchemaVersion = null,
            ProjectionFingerprintSha256 = null, BlockingFrameFingerprintSha256 = null
        }, [config]);
        var fit = adapter.FitTrain(Model(config), Slice("TRAIN"));
        Assert.That((Action)(() => adapter.ExportFrozenSnapshot(fit,
            [new LinkageBlockingPass("NAME", ["NOME"])],
            "PROJECTION_V1", new string('f', 64), .2m)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("frame"));
    }

    [Test]
    public void Positive_TF_cannot_be_silently_disabled_by_a_stale_comparison_level()
    {
        var rows = Frame().Pairs.Where(x => x.Partition == "TRAIN").ToArray();
        var fitted = V9TrainSqlFirstNameUCalibration.Fit(
            rows, CompositeNominalTfSideV9.Person, .5m, TrainCorpus);
        var pair = Frame().Pairs.First(x => x.Partition == "VALIDATION");
        Assert.That(CompositeNominalComparisonV9.Classify(
            pair.LeftPersonName, pair.RightPersonName), Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        Assert.That((Action)(() => V9TrainSqlFirstNameUCalibration.Preview(
            fitted, Reference(), CompositeNominalLevelV9.Other,
            CompositeNominalLevelV9.Missing, pair, 1m, .000001m, 1)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("divergem"));
        var preview = V9TrainSqlFirstNameUCalibration.Preview(
            fitted, Reference(), CompositeNominalLevelV9.ExactFirst,
            CompositeNominalLevelV9.Missing, pair, 1m, .000001m, 1);
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview!.TermFrequency, Is.EqualTo(.60m));
        Assert.That(preview.UnitReferenceU, Is.EqualTo(2.5m / 3m));
    }

    [Test]
    public void ExactFull_uses_first_name_marginal_and_rejects_invalid_configuration()
    {
        var rows = Frame().Pairs.Where(x => x.Partition == "TRAIN").ToArray();
        var fitted = V9TrainSqlFirstNameUCalibration.Fit(
            rows, CompositeNominalTfSideV9.Person, .5m, TrainCorpus);
        var pair = rows[0] with { RightPersonName = rows[0].LeftPersonName };
        Assert.That(CompositeNominalComparisonV9.Classify(
            pair.LeftPersonName, pair.RightPersonName), Is.EqualTo(CompositeNominalLevelV9.ExactFull));
        var exactFullPreview = V9TrainSqlFirstNameUCalibration.Preview(
            fitted, Reference(), CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing, pair, 1m, .000001m, 1);
        Assert.That(exactFullPreview, Is.Not.Null);
        Assert.That(exactFullPreview!.TermFrequency, Is.EqualTo(.60m));
        Assert.That((Action)(() => V9TrainSqlFirstNameUCalibration.Preview(
            fitted, Reference(), CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing, pair, 1m, -1m, 1)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9TrainSqlFirstNameUCalibration.Preview(
            fitted with { Side = (CompositeNominalTfSideV9)99 }, Reference(),
            CompositeNominalLevelV9.ExactFull, CompositeNominalLevelV9.Missing,
            pair, 1m, .000001m, 1)), Throws.TypeOf<InvalidDataException>());
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
            _ = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [config])),
            Throws.TypeOf<ArgumentException>());
        Assert.That((Action)(() =>
            _ = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(),
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

    private static V9FsAdaptiveCandidateFrame FrameWithMotherTf()
    {
        var frame = Frame();
        return frame with
        {
            Pairs = frame.Pairs.Select(pair => pair with
            {
                LeftMotherName = "ANA COSTA", RightMotherName = "ANA LIMA"
            }).ToArray()
        };
    }

    private static V9FsAdaptiveOptionConfiguration FourUnits(
        string id, CompositeNominalTfAdjustmentModeV9 personMode,
        CompositeNominalTfAdjustmentModeV9 motherMode, decimal motherWeight = .5m) =>
        new(id, .5m, .55m, UnitTfOptions:
        [
            new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName, 1m, personMode),
            new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname, 0m,
                CompositeNominalTfAdjustmentModeV9.Symmetric),
            new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName, motherWeight, motherMode),
            new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname, 0m,
                CompositeNominalTfAdjustmentModeV9.Symmetric)
        ]);

    [TestCase(CompositeNominalTfAdjustmentModeV9.Symmetric,
        CompositeNominalTfAdjustmentModeV9.Symmetric)]
    [TestCase(CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly,
        CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)]
    [TestCase(CompositeNominalTfAdjustmentModeV9.Symmetric,
        CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)]
    public void Bilateral_independent_TF_modes_match_holdout_and_frozen_runtime(
        CompositeNominalTfAdjustmentModeV9 personMode,
        CompositeNominalTfAdjustmentModeV9 motherMode)
    {
        var frame = FrameWithMotherTf();
        var config = FourUnits("BILATERAL", personMode, motherMode);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            frame, [config], Reference());
        var fitted = adapter.FitTrain(Model(config), Slice("TRAIN"));
        var snapshot = adapter.ExportFrozenSnapshot(fitted,
            [new LinkageBlockingPass("NAME", ["NOME"])],
            "PROJECTION_V1", new string('f', 64), .2m);
        var replay = V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
        var unitFits = replay.Payload.UnitTfCalibrations!;
        Assert.That(unitFits.Count, Is.EqualTo(4));
        Assert.That(unitFits.Count(x => x.State == V9SnapshotTfFitState.Active), Is.EqualTo(2));
        var person = unitFits.Single(x => x.Side == CompositeNominalTfSideV9.Person
            && x.Unit == NominalFrequencyUnitV9.FirstName);
        var mother = unitFits.Single(x => x.Side == CompositeNominalTfSideV9.Mother
            && x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.That(person.Calibration!.Mode, Is.EqualTo(personMode));
        Assert.That(person.Calibration.Weight, Is.EqualTo(1m));
        Assert.That(mother.Calibration!.Mode, Is.EqualTo(motherMode));
        Assert.That(mother.Calibration.Weight, Is.EqualTo(.5m));
        Assert.That(unitFits.Count(x => x.State == V9SnapshotTfFitState.NoSource),
            Is.EqualTo(2));

        var validationPair = frame.Pairs.First(x => x.Partition == "VALIDATION");
        var trainingPairs = frame.Pairs.Where(x => x.Partition == "TRAIN").ToArray();
        CompositeNominalTfPreviewV9 Term(CompositeNominalTfSideV9 side,
            decimal weight, CompositeNominalTfAdjustmentModeV9 mode)
        {
            var frozenU = V9TrainSqlFirstNameUCalibration.Fit(
                trainingPairs, side, config.SmoothingAlpha, TrainCorpus);
            return V9TrainSqlFirstNameUCalibration.Preview(
                frozenU, Reference(), CompositeNominalLevelV9.ExactFirst,
                CompositeNominalLevelV9.ExactFirst, validationPair,
                weight, config.TfMinimumFrequency, config.TfMinimumIndependentSupport,
                mode)!;
        }
        var nominal = FrozenFellegiSunterJointNominalV9.Freeze(
            replay.Payload.JointCells, replay.Payload.NominalContractVersion);
        var expected = FellegiSunterScoring.CalculateFrozenCompositeNominalV9BilateralPreviewScore(
            replay.Payload.Parameters,
            validationPair.LeftPersonName, validationPair.RightPersonName,
            validationPair.LeftMotherName, validationPair.RightMotherName,
            nominal, leftBirthDate: Birth, rightBirthDate: Birth,
            personTf: Term(CompositeNominalTfSideV9.Person, 1m, personMode),
            motherTf: Term(CompositeNominalTfSideV9.Mother, .5m, motherMode));
        var observed = replay.RankPreview(
            new IdentityObservation(null, "SEM_CPF",
                validationPair.LeftPersonName, Birth, validationPair.LeftMotherName),
            [new V9SnapshotCandidate(Guid.NewGuid(),
                validationPair.RightPersonName, Birth, validationPair.RightMotherName)]).Single();
        Assert.That(observed.LogOdds, Is.EqualTo(expected.LogOdds));
        Assert.That(observed.Score, Is.EqualTo(expected.Posterior));
        var metrics = adapter.EvaluateValidation(fitted, Slice("VALIDATION"));
        var expectedAccepted = expected.Posterior >= config.DecisionThreshold ? 2 : 0;
        Assert.That(metrics.TrueAcceptedLinks, Is.EqualTo(expectedAccepted));
        Assert.That(metrics.FalseAcceptedLinks, Is.EqualTo(expectedAccepted));
    }

    [Test]
    public void ZeroChosen_is_frozen_but_neutral_while_NotSelected_and_no_source_are_distinct()
    {
        var frame = FrameWithMotherTf();
        var config = FourUnits("ZERO_CHOSEN",
            CompositeNominalTfAdjustmentModeV9.Symmetric,
            CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly, 0m);
        var legacy = new V9FsAdaptiveOptionConfiguration("LEGACY", .5m, .55m, 1m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            frame, [config, legacy], Reference());
        V9FittedSnapshot Freeze(V9FsAdaptiveOptionConfiguration c) =>
            adapter.ExportFrozenSnapshot(adapter.FitTrain(Model(c), Slice("TRAIN")),
                [new LinkageBlockingPass("NAME", ["NOME"])],
                "PROJECTION_V1", new string('f', 64), .2m);
        var snap = Freeze(config);
        var replay = V9FittedSnapshot.Load(snap.SerializeUtf8(), snap.ContentSha256);
        var zero = replay.Payload.UnitTfCalibrations!.Single(
            x => x.Side == CompositeNominalTfSideV9.Mother
                && x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.That(zero.State, Is.EqualTo(V9SnapshotTfFitState.ZeroChosen));
        Assert.That(zero.Calibration, Is.Not.Null);
        Assert.That(zero.Calibration!.Weight, Is.Zero);
        Assert.That(zero.Calibration.Mode,
            Is.EqualTo(CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly));
        Assert.That(zero.Calibration.TrainUnitUByValue["ANA"], Is.GreaterThan(0m));
        var legacySnap = Freeze(legacy);
        Assert.That(legacySnap.Payload.UnitTfCalibrations!.Single(x =>
                x.Side == CompositeNominalTfSideV9.Mother
                && x.Unit == NominalFrequencyUnitV9.FirstName).State,
            Is.EqualTo(V9SnapshotTfFitState.NotSelected));

        var unitFits = replay.Payload.UnitTfCalibrations!;
        var malformed = replay.Payload with
        {
            UnitTfCalibrations = unitFits.Select(x => x == zero
                ? x with { Calibration = x.Calibration! with { Weight = .2m } } : x).ToArray()
        };
        Assert.That((Action)(() => V9FittedSnapshot.Create(malformed)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FittedSnapshot.Create(replay.Payload with
        {
            UnitTfCalibrations = unitFits.Select(x => x == zero
                ? x with { State = V9SnapshotTfFitState.Active } : x).ToArray()
        })), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FittedSnapshot.Create(replay.Payload with
        {
            UnitTfCalibrations = unitFits.Select(x => x == zero
                ? x with { Calibration = null } : x).ToArray()
        })), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Four_unit_positive_TF_rejects_missing_reference_bad_mode_and_wrong_blocking_frame()
    {
        var good = FourUnits("FOUR", CompositeNominalTfAdjustmentModeV9.Symmetric,
            CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly);
        Assert.That((Action)(() => new V9FellegiSunterAdaptiveCalibrationAdapter(
            FrameWithMotherTf(), [good])),
            Throws.TypeOf<ArgumentException>());
        var wrongMode = good with
        {
            UnitTfOptions = good.UnitTfOptions!.Select((x, i) =>
                i == 0 ? x with { Mode = (CompositeNominalTfAdjustmentModeV9)100 } : x).ToArray()
        };
        Assert.That((Action)(() => new V9FellegiSunterAdaptiveCalibrationAdapter(
            FrameWithMotherTf(), [wrongMode], Reference())),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => new V9FellegiSunterAdaptiveCalibrationAdapter(
            FrameWithMotherTf() with { BlockingFrameFingerprintSha256 = new string('2', 64) },
            [good], Reference())), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void All_four_TRAIN_U_distributions_follow_the_same_ExactFull_and_ExactLast_selector()
    {
        var first = FrameWithMotherTf().Pairs.Where(x => x.Partition == "TRAIN" && !x.IsTruePair).ToArray();
        var full = first.Select(x => x with
        {
            RightPersonName = x.LeftPersonName,
            RightMotherName = x.LeftMotherName
        });
        var last = first.Select(x => x with
        {
            LeftPersonName = "MARIA SILVA", RightPersonName = "JOAO SILVA",
            LeftMotherName = "ANA SOUZA", RightMotherName = "EVA SOUZA"
        });
        var all = first.Concat(full).Concat(last).ToArray();
        var fits = new[]
        {
            V9TrainSqlFirstNameUCalibration.Fit(all, CompositeNominalTfSideV9.Person,
                .5m, TrainCorpus, NominalFrequencyUnitV9.FirstName),
            V9TrainSqlFirstNameUCalibration.Fit(all, CompositeNominalTfSideV9.Person,
                .5m, TrainCorpus, NominalFrequencyUnitV9.LastContentSurname),
            V9TrainSqlFirstNameUCalibration.Fit(all, CompositeNominalTfSideV9.Mother,
                .5m, TrainCorpus, NominalFrequencyUnitV9.FirstName),
            V9TrainSqlFirstNameUCalibration.Fit(all, CompositeNominalTfSideV9.Mother,
                .5m, TrainCorpus, NominalFrequencyUnitV9.LastContentSurname)
        };
        Assert.That(fits.All(x => x.HasSufficientSupport), Is.True);
        Assert.That(fits.Select(x => x.FingerprintSha256).Distinct().Count(), Is.EqualTo(4));
        Assert.That(fits[0].UnmatchedExactFirstCandidatePairs, Is.EqualTo(4));
        Assert.That(fits[1].UnmatchedExactFirstCandidatePairs, Is.EqualTo(2));
        Assert.That(fits[2].UnmatchedExactFirstCandidatePairs, Is.EqualTo(4));
        Assert.That(fits[3].UnmatchedExactFirstCandidatePairs, Is.EqualTo(2));
        Assert.That(fits[0].FirstNameUnitUByValue.ContainsKey("MARIA"), Is.True);
        Assert.That(fits[1].FirstNameUnitUByValue.ContainsKey("SILVA"), Is.True);
        Assert.That(fits[2].FirstNameUnitUByValue.ContainsKey("ANA"), Is.True);
        Assert.That(fits[3].FirstNameUnitUByValue.ContainsKey("SOUZA"), Is.True);
    }
}

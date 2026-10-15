using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FellegiSunterAdaptiveCalibrationAdapterTests
{
    private static readonly string TrainSha = new('a', 64);
    private static readonly string ValidationSha = new('b', 64);
    private static readonly string TrainGroupsSha = new('c', 64);
    private static readonly string ValidationGroupsSha = new('d', 64);
    private static readonly string StudySha = new('e', 64);
    private static readonly DateOnly Birth = new(1980, 5, 6);

    [Test]
    public void Real_V9_adapter_fits_train_and_scores_validation_with_existing_Csharp_FS()
    {
        var option = new V9FsAdaptiveOptionConfiguration("T050", .5m, .50m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);

        var request = new AdaptiveCalibrationStudyRequest(
            StudySha,
            Slice("TRAIN", TrainSha, TrainGroupsSha, 4),
            Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 4),
            [new AdaptiveCalibrationModelOption(
                V9FellegiSunterAdaptiveCalibrationAdapter.Family,
                V9FellegiSunterAdaptiveCalibrationAdapter.Version,
                option.OptionId,
                option.FingerprintSha256)],
            ManuallyRequestedCalibration: true,
            new AdaptiveCalibrationPreviewBudget(
                AdaptiveCalibrationPreviewBudget.CurrentVersion,
                StudySha,
                MinimumGlobalAcceptedRecall: .95,
                MaximumObservedAcceptedFdrPoint: .01,
                MaximumBlockingFalseDegreeP99: 10,
                MinimumValidationIdentityGroups: 4));

        var report = AdaptiveCalibrationModelSearch.Evaluate(request, [adapter]);

        Assert.Multiple(() =>
        {
            Assert.That(report.Options, Has.Count.EqualTo(1));
            Assert.That(report.Options[0].Validation.TruePairsWithoutCpf, Is.EqualTo(2));
            Assert.That(report.Options[0].Validation.TrueAcceptedLinks, Is.EqualTo(2));
            Assert.That(report.Options[0].Validation.FalseAcceptedLinks, Is.EqualTo(0));
            Assert.That(report.Options[0].Validation.Abstentions, Is.EqualTo(0));
            Assert.That(report.Options[0].ObservedAcceptedFdrPoint, Is.EqualTo(0d));
            Assert.That(report.Options[0].GlobalAcceptedRecall, Is.EqualTo(1d));
            Assert.That(report.SuggestedModelFamily,
                Is.EqualTo(V9FellegiSunterAdaptiveCalibrationAdapter.Family));
            Assert.That(report.TestUsed, Is.False);
            Assert.That(report.OperationalModelCreated, Is.False);
            Assert.That(report.AutomaticRecalibrationOrActivationAllowed, Is.False);
        });
    }

    [Test]
    public void Configuration_fingerprint_is_bound_to_threshold_and_fit_is_train_only()
    {
        var option = new V9FsAdaptiveOptionConfiguration("T090", .5m, .90m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId,
            option.FingerprintSha256);

        var fit = adapter.FitTrain(model, Slice("TRAIN", TrainSha, TrainGroupsSha, 4));

        Assert.That(fit.TrainOnly, Is.True);
        Assert.That(fit.TrainingCorpusFingerprintSha256, Is.EqualTo(TrainSha));
        Assert.That(fit.FittedParametersFingerprintSha256, Has.Length.EqualTo(64));

        var forged = model with { ConfigurationFingerprintSha256 = new string('f', 64) };
        Assert.That(
            (Action)(() => { _ = adapter.FitTrain(
                forged, Slice("TRAIN", TrainSha, TrainGroupsSha, 4)); }),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Frame_rejects_test_partition_and_identity_leakage()
    {
        var pairs = Frame().Pairs.ToList();
        pairs.Add(new V9FsAdaptiveCandidatePair(
            "TEST", Guid.NewGuid(), true,
            "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA",
            Birth, Birth, 1));

        Assert.That(
            (Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
                Frame() with { Pairs = pairs },
                [new V9FsAdaptiveOptionConfiguration("DEFAULT", .5m, .5m)]); }),
            Throws.TypeOf<InvalidDataException>());

        var leaked = Frame();
        var same = leaked.Pairs[0].IdentityGroupId;
        var changed = leaked.Pairs.Select((x, i) =>
            i == 4 ? x with { IdentityGroupId = same } : x).ToArray();
        Assert.That(
            (Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
                leaked with { Pairs = changed },
                [new V9FsAdaptiveOptionConfiguration("DEFAULT", .5m, .5m)]); }),
            Throws.TypeOf<InvalidDataException>());
    }

    private static AdaptiveCalibrationSlice Slice(
        string partition, string corpus, string groups, int count) =>
        new(partition, corpus, groups, count, true, false);

    private static V9FsAdaptiveCandidateFrame Frame()
    {
        var g = Enumerable.Range(1, 8)
            .Select(i => Guid.Parse($"00000000-0000-4000-8000-{i:000000000000}"))
            .ToArray();
        return new V9FsAdaptiveCandidateFrame(
            TrainSha, ValidationSha, TrainGroupsSha, ValidationGroupsSha,
            [
                Pair("TRAIN", g[0], true, "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA", Birth, Birth, 2),
                Pair("TRAIN", g[1], true, "JOAO SOUZA", "JOAO SOUZA", "LUCIA SOUZA", "LUCIA SOUZA", Birth, Birth, 2),
                Pair("TRAIN", g[2], false, "MARIA SILVA", "CARLOS LIMA", "ANA COSTA", "ROSA LIMA", Birth, Birth.AddDays(20), 5),
                Pair("TRAIN", g[3], false, "JOAO SOUZA", "PEDRO ALVES", "LUCIA SOUZA", "TERESA ALVES", Birth, Birth.AddYears(3), 6),

                Pair("VALIDATION", g[4], true, "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA", Birth, Birth, 2),
                Pair("VALIDATION", g[5], true, "JOAO SOUZA", "JOAO SOUZA", "LUCIA SOUZA", "LUCIA SOUZA", Birth, Birth, 3),
                Pair("VALIDATION", g[6], false, "MARIA SILVA", "ROBERTO MENDES", "ANA COSTA", "HELENA MENDES", Birth, Birth.AddYears(5), 7),
                Pair("VALIDATION", g[7], false, "JOAO SOUZA", "PAULO NUNES", "LUCIA SOUZA", "MARTA NUNES", Birth, Birth.AddDays(10), 8)
            ]);
    }

    private static V9FsAdaptiveCandidatePair Pair(
        string partition, Guid group, bool isTrue,
        string leftName, string rightName, string leftMother, string rightMother,
        DateOnly leftBirth, DateOnly rightBirth, int degree) =>
        new(partition, group, isTrue, leftName, rightName, leftMother, rightMother,
            leftBirth, rightBirth, degree);
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class AdaptiveCalibrationModelSearchTests
{
    private static readonly string Study = new('a', 64);
    private static readonly string Config = new('b', 64);
    private static readonly string Fit = new('c', 64);
    private static readonly string Train = new('d', 64);
    private static readonly string Validation = new('e', 64);
    private static readonly string TrainGroups = new('f', 64);
    private static readonly string ValGroups = new('1', 64);

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
        => Assert.That(action, Throws.TypeOf<TException>());

    private sealed class FakeAdapter(
        string family, long tp, long fp, bool wrongTrain = false)
        : IAdaptiveCalibrationFamilyAdapter
    {
        public string ModelFamily => family;
        public string AdapterVersion => "MODEL_ADAPTER_V1";
        public bool FittedTrainOnly { get; private set; }
        public bool EvaluatedValidationOnly { get; private set; }

        public AdaptiveCalibrationFitDraft FitTrain(
            AdaptiveCalibrationModelOption modelOption, AdaptiveCalibrationSlice train)
        {
            FittedTrainOnly = train.Partition == "TRAIN" && !train.UsesTestLabels;
            return new AdaptiveCalibrationFitDraft(
                modelOption.ModelFamily, modelOption.AdapterVersion, modelOption.OptionId,
                modelOption.ConfigurationFingerprintSha256, Fit,
                wrongTrain ? Validation : train.CorpusFingerprintSha256, true);
        }

        public AdaptiveCalibrationHoldoutMetrics EvaluateValidation(
            AdaptiveCalibrationFitDraft fitted, AdaptiveCalibrationSlice validation)
        {
            EvaluatedValidationOnly = validation.Partition == "VALIDATION"
                && !validation.UsesTestLabels;
            return new AdaptiveCalibrationHoldoutMetrics(
                TruePairsWithoutCpf: 1000,
                TrueAcceptedLinks: tp,
                FalseAcceptedLinks: fp,
                Abstentions: 1000 - tp,
                BlockingFalseCandidatePairs: 200,
                BlockingFalseDegreeP99: 8.0,
                IndependentlyLabeledIdentityGroups: 600);
        }
    }

    private static AdaptiveCalibrationStudyRequest Request(
        AdaptiveCalibrationPreviewBudget? policy = null, bool manual = true)
        => new(Study,
            new AdaptiveCalibrationSlice("TRAIN", Train, TrainGroups, 800, true, false),
            new AdaptiveCalibrationSlice("VALIDATION", Validation, ValGroups, 850, true, false),
            [
                new AdaptiveCalibrationModelOption("FAMILY_FS_V9", "MODEL_ADAPTER_V1", "DEFAULT", Config),
                new AdaptiveCalibrationModelOption("FAMILY_FUTURE", "MODEL_ADAPTER_V1", "GRID_1", Config)
            ], manual, policy);

    private static AdaptiveCalibrationPreviewBudget Budget()
        => new(AdaptiveCalibrationPreviewBudget.CurrentVersion, Study, 0.95, 0.01, 50, 500);

    [Test]
    public void All_Accepted_Links_False_Always_Fails_Preliminary_Fdr_Screen()
    {
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(Budget()), [new FakeAdapter("FAMILY_FS_V9", 0, 200),
                                new FakeAdapter("FAMILY_FUTURE", 0, 200)]);
        Assert.That(report.Options.All(x => x.ObservedAcceptedFdrPoint == 1d), Is.True);
        Assert.That(report.Options.All(x => x.NominalPairwiseFdrUpper95 == 1d), Is.True);
        Assert.That(report.Options.All(x => !x.MeetsNecessaryNominalFdrScreen), Is.True);
        Assert.That(report.SuggestedOptionId, Is.Null);
        Assert.That(report.OperationalModelCreated, Is.False);
    }

    [Test]
    public void Family_Agnostic_Search_Can_Suggest_Better_Of_Two_Models_Without_Promotion()
    {
        var fs = new FakeAdapter("FAMILY_FS_V9", 965, 3);
        var future = new FakeAdapter("FAMILY_FUTURE", 980, 6);
        var report = AdaptiveCalibrationModelSearch.Evaluate(Request(Budget()), [fs, future]);
        Assert.That(fs.FittedTrainOnly && future.FittedTrainOnly, Is.True);
        Assert.That(fs.EvaluatedValidationOnly && future.EvaluatedValidationOnly, Is.True);
        Assert.That(report.Options.Count, Is.EqualTo(2));
        Assert.That(report.SuggestedModelFamily, Is.EqualTo("FAMILY_FS_V9"));
        Assert.That(report.SuggestedOptionId, Is.EqualTo("DEFAULT"));
        Assert.That(report.Status, Is.EqualTo("SUGGESTION_ONLY_UNCERTIFIED_GROUP_FDR_AND_PHYSICAL_SQL"));
        Assert.That(report.Options.All(x => x.MeetsDescriptiveBudgetOnly), Is.True);
        Assert.That(report.Options[0].MeetsNecessaryNominalFdrScreen, Is.True);
        Assert.That(report.Options[1].MeetsNecessaryNominalFdrScreen, Is.False);
        Assert.That(report.Options[0].NominalPairwiseFdrUpper95, Is.LessThan(.01d));
        Assert.That(report.Options[1].NominalPairwiseFdrUpper95, Is.GreaterThan(.01d));
        Assert.That(report.ConfidenceBoundForFdrMeasured, Is.False);
        Assert.That(report.OperationalModelCreated, Is.False);
        Assert.That(report.AutomaticRecalibrationOrActivationAllowed, Is.False);
        Assert.That(report.TestUsed, Is.False);
    }

    [Test]
    public void No_Budget_Emits_Only_Descriptive_Results_Not_A_Decision()
    {
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(), [new FakeAdapter("FAMILY_FS_V9", 980, 2),
                        new FakeAdapter("FAMILY_FUTURE", 950, 1)]);
        Assert.That(report.Status, Is.EqualTo("NO_APPROVED_POLICY_DESCRIPTIVE_ONLY"));
        Assert.That(report.SuggestedModelFamily, Is.Null);
        Assert.That(report.Options[0].MeetsDescriptiveBudgetOnly, Is.False);
    }

    [Test]
    public void Failed_Risk_Or_Recall_Budget_Never_Promotes_Or_Selects()
    {
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(Budget()), [new FakeAdapter("FAMILY_FS_V9", 920, 0),
                new FakeAdapter("FAMILY_FUTURE", 980, 150)]);
        Assert.That(report.Status, Is.EqualTo("NO_MODEL_MEETS_PRELIMINARY_FDR_SCREEN"));
        Assert.That(report.SuggestedOptionId, Is.Null);
        Assert.That(report.AutomaticRecalibrationOrActivationAllowed, Is.False);
    }

    [Test]
    public void Zero_False_Accepted_On_Small_Frame_Is_Not_Evidence_Of_One_Percent_Risk()
    {
        // No FP among just two accepted pairs still has a ~57.5% nominal
        // one-sided UCB; the pairwise approximation is only a necessary screen.
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(Budget()),
            [new FakeAdapter("FAMILY_FS_V9", 2, 0),
             new FakeAdapter("FAMILY_FUTURE", 2, 0)]);
        Assert.That(report.Options.All(x => x.ObservedAcceptedFdrPoint == 0d), Is.True);
        Assert.That(report.Options.All(x => x.NominalPairwiseFdrUpper95 > .57d), Is.True);
        Assert.That(report.Options.All(x => !x.MeetsNecessaryNominalFdrScreen), Is.True);
        Assert.That(report.SuggestedOptionId, Is.Null);
        Assert.That(report.Status, Is.EqualTo("NO_MODEL_MEETS_PRELIMINARY_FDR_SCREEN"));
        Assert.That(report.ConfidenceBoundForFdrMeasured, Is.False);
    }

    [Test]
    public void Calibration_Requires_Explicit_Manual_Execution_And_No_Test_Leakage()
    {
        var adapters = new IAdaptiveCalibrationFamilyAdapter[] {
            new FakeAdapter("FAMILY_FS_V9", 980, 2),
            new FakeAdapter("FAMILY_FUTURE", 960, 2)
        };
        AssertThrows<InvalidOperationException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(manual: false), adapters));
        var request = Request();
        var invalid = request with {
            Validation = request.Validation with { UsesTestLabels = true }
        };
        AssertThrows<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(invalid, adapters));
        invalid = request with {
            Validation = request.Validation with { IndependentIdentityPartitionFingerprintSha256 = TrainGroups }
        };
        AssertThrows<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(invalid, adapters));
    }

    [Test]
    public void Unknown_Or_Version_Mismatched_Family_Fails_Closed()
    {
        AssertThrows<InvalidOperationException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2)
            ]));
        AssertThrows<ArgumentException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2),
                new FakeAdapter("FAMILY_FS_V9", 970, 2),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }

    [Test]
    public void Forged_Source_Fingerprint_Is_Not_Allowed_As_A_Train_Fit()
    {
        AssertThrows<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(Budget()), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2, wrongTrain: true),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }

    [Test]
    public void Impossible_False_Acceptance_Exceeding_U_Candidates_Fails_Closed()
    {
        AssertThrows<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(Budget()), [
                new FakeAdapter("FAMILY_FS_V9", 980, 201),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }

    [Test]
    public void Unsafe_Budget_Must_Not_Be_Loosened_Silently()
    {
        var request = Request(Budget() with { MaximumObservedAcceptedFdrPoint = 0.2 });
        AssertThrows<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(request, [
                new FakeAdapter("FAMILY_FS_V9", 980, 2),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }
}

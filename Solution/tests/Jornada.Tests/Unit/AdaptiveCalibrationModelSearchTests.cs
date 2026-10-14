using Jornada.Linkage.Parameters.Worker;
using Xunit;

namespace Jornada.Tests.Unit;

public sealed class AdaptiveCalibrationModelSearchTests
{
    private static readonly string Study = new('a', 64);
    private static readonly string Config = new('b', 64);
    private static readonly string Fit = new('c', 64);
    private static readonly string Train = new('d', 64);
    private static readonly string Validation = new('e', 64);
    private static readonly string TrainGroups = new('f', 64);
    private static readonly string ValGroups = new('1', 64);

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

    [Fact]
    public void Family_Agnostic_Search_Can_Suggest_Better_Of_Two_Models_Without_Promotion()
    {
        var fs = new FakeAdapter("FAMILY_FS_V9", 965, 3);
        var future = new FakeAdapter("FAMILY_FUTURE", 980, 6);
        var report = AdaptiveCalibrationModelSearch.Evaluate(Request(Budget()), [fs, future]);
        Assert.True(fs.FittedTrainOnly && future.FittedTrainOnly);
        Assert.True(fs.EvaluatedValidationOnly && future.EvaluatedValidationOnly);
        Assert.Equal(2, report.Options.Count);
        Assert.Equal("FAMILY_FUTURE", report.SuggestedModelFamily);
        Assert.Equal("GRID_1", report.SuggestedOptionId);
        Assert.Equal("SUGGESTION_ONLY_UNCERTIFIED_FDR_CONFIDENCE_AND_PHYSICAL_SQL",
            report.Status);
        Assert.All(report.Options, x => Assert.True(x.MeetsDescriptiveBudgetOnly));
        Assert.False(report.ConfidenceBoundForFdrMeasured);
        Assert.False(report.OperationalModelCreated);
        Assert.False(report.AutomaticRecalibrationOrActivationAllowed);
        Assert.False(report.TestUsed);
    }

    [Fact]
    public void No_Budget_Emits_Only_Descriptive_Results_Not_A_Decision()
    {
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(), [new FakeAdapter("FAMILY_FS_V9", 980, 2),
                        new FakeAdapter("FAMILY_FUTURE", 950, 1)]);
        Assert.Equal("NO_APPROVED_POLICY_DESCRIPTIVE_ONLY", report.Status);
        Assert.Null(report.SuggestedModelFamily);
        Assert.False(report.Options[0].MeetsDescriptiveBudgetOnly);
    }

    [Fact]
    public void Failed_Risk_Or_Recall_Budget_Never_Promotes_Or_Selects()
    {
        var report = AdaptiveCalibrationModelSearch.Evaluate(
            Request(Budget()), [new FakeAdapter("FAMILY_FS_V9", 920, 0),
                new FakeAdapter("FAMILY_FUTURE", 980, 250)]);
        Assert.Equal("NO_MODEL_MEETS_DESCRIPTIVE_BUDGET", report.Status);
        Assert.Null(report.SuggestedOptionId);
        Assert.False(report.AutomaticRecalibrationOrActivationAllowed);
    }

    [Fact]
    public void Calibration_Requires_Explicit_Manual_Execution_And_No_Test_Leakage()
    {
        var adapters = new IAdaptiveCalibrationFamilyAdapter[] {
            new FakeAdapter("FAMILY_FS_V9", 980, 2),
            new FakeAdapter("FAMILY_FUTURE", 960, 2)
        };
        Assert.Throws<InvalidOperationException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(manual: false), adapters));
        var request = Request();
        var invalid = request with {
            Validation = request.Validation with { UsesTestLabels = true }
        };
        Assert.Throws<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(invalid, adapters));
        invalid = request with {
            Validation = request.Validation with { IndependentIdentityPartitionFingerprintSha256 = TrainGroups }
        };
        Assert.Throws<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(invalid, adapters));
    }

    [Fact]
    public void Unknown_Or_Version_Mismatched_Family_Fails_Closed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2)
            ]));
        Assert.Throws<ArgumentException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2),
                new FakeAdapter("FAMILY_FS_V9", 970, 2),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }

    [Fact]
    public void Forged_Source_Fingerprint_Is_Not_Allowed_As_A_Train_Fit()
    {
        Assert.Throws<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(Request(Budget()), [
                new FakeAdapter("FAMILY_FS_V9", 980, 2, wrongTrain: true),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }

    [Fact]
    public void Unsafe_Budget_Must_Not_Be_Loosened_Silently()
    {
        var request = Request(Budget() with { MaximumObservedAcceptedFdrPoint = 0.2 });
        Assert.Throws<InvalidDataException>(() =>
            AdaptiveCalibrationModelSearch.Evaluate(request, [
                new FakeAdapter("FAMILY_FS_V9", 980, 2),
                new FakeAdapter("FAMILY_FUTURE", 980, 2)
            ]));
    }
}

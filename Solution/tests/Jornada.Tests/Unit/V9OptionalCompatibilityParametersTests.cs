using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class V9OptionalCompatibilityParametersTests
{
    private static byte[] HistoricalPlanBytes()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Jornada.sln")))
            d = d.Parent;
        if (d is null) throw new DirectoryNotFoundException("Solution root");
        return File.ReadAllBytes(Path.Combine(d.FullName, "data", "calibration", "v9",
            "nested-train-selection-exploratory", "plan.json"));
    }

    [Test]
    public void Old_plan_without_new_J_fields_keeps_exact_legacy_defaults_and_option_fingerprints()
    {
        var original = HistoricalPlanBytes();
        var legacy = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(original)!;
        var effective = JsonSerializer.Serialize(legacy);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(legacy.RecallEquivalenceMargin, Is.Null);
            Assert.That(legacy.TieBreakOrder, Is.Null);
            Assert.That(legacy.ClusterUnit, Is.Null);
            Assert.That(legacy.Estimator, Is.Null);
            Assert.That(legacy.ManualReviewSampleSize, Is.Null);
            Assert.That(legacy.RecalibrationTriggers, Is.Null);
            Assert.That(legacy.PredeclaredCandidateCpMultiplicity, Is.Null);
            Assert.That(legacy.Calibration.MaximumParallelRuleSets, Is.EqualTo(1));
            Assert.That(effective, Does.Not.Contain("RecallEquivalenceMargin"));
            Assert.That(effective, Does.Not.Contain("TieBreakOrder"));
            Assert.That(effective, Does.Not.Contain("ClusterUnit"));
            Assert.That(effective, Does.Not.Contain("Estimator"));
            Assert.That(effective, Does.Not.Contain("ManualReviewSampleSize"));
            Assert.That(effective, Does.Not.Contain("RecalibrationTriggers"));
        }));
        V9OptionalCompatibilityParameters.Validate(legacy);
        var opt = legacy.Calibration.ModelOptions[0];
        var before = opt.FingerprintSha256;
        var explicitNeutral = legacy with
        {
            RecallEquivalenceMargin = 0m,
            TieBreakOrder = ["recall", "FDR upper", "SHA"],
            ClusterUnit = "IDENTITY",
            Estimator = "LABELED",
            ManualReviewSampleSize = 0,
            RecalibrationTriggers = false
        };
        V9OptionalCompatibilityParameters.Validate(explicitNeutral);
        Assert.That(explicitNeutral.Calibration.ModelOptions[0].FingerprintSha256, Is.EqualTo(before));
        Assert.That(explicitNeutral.Calibration, Is.SameAs(legacy.Calibration));
    }

    [Test]
    public void Every_option_reports_source_plan_or_code_default_from_original_json()
    {
        var bytes = HistoricalPlanBytes();
        var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(bytes)!;
        var id = plan.Calibration.ModelOptions[0].OptionId;
        var eval = new V9NestedCandidateEvaluation("test-sha", 1, null,
            Array.Empty<string>(), OptionId: id);
        var report = V9OptionalCompatibilityParameters.Describe(bytes, plan, [eval]).Single();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.BaseOptionId, Is.EqualTo(id));
            Assert.That(report.BaseOptionParameters["OptionId"].Source,
                Is.EqualTo(V9OptionalCompatibilityParameters.FromPlan));
            Assert.That(report.NeutralParameters["RecallEquivalenceMargin"].Source,
                Is.EqualTo(V9OptionalCompatibilityParameters.CodeDefault));
            Assert.That(report.NeutralParameters["ClusterUnit"].Source,
                Is.EqualTo(V9OptionalCompatibilityParameters.CodeDefault));
            Assert.That(report.NeutralParameters["ClusterUnit"].EffectiveValue.GetString(),
                Is.EqualTo("IDENTITY"));
            Assert.That(report.NeutralParameters["ManualReviewSampleSize"].EffectiveValue.GetInt32(),
                Is.Zero);
            Assert.That(report.NeutralParameters["RecalibrationTriggers"].EffectiveValue.GetBoolean(),
                Is.False);
            Assert.That(report.NeutralParameters["TieBreakOrder"].EffectiveValue.EnumerateArray()
                .Select(x => x.GetString()).ToArray(),
                Is.EqualTo(new[] { "recall", "FDR upper", "SHA" }));
        }));
    }

    [Test]
    public void Present_neutral_parameters_are_taken_from_file_and_unknown_fields_fail_closed()
    {
        var planBytes = HistoricalPlanBytes();
        using var source = JsonDocument.Parse(planBytes);
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(planBytes)!;
        root["RecallEquivalenceMargin"] = JsonSerializer.SerializeToElement(0m);
        root["ClusterUnit"] = JsonSerializer.SerializeToElement("IDENTITY");
        root["Estimator"] = JsonSerializer.SerializeToElement("LABELED");
        root["RecalibrationTriggers"] = JsonSerializer.SerializeToElement(false);
        var explicitBytes = JsonSerializer.SerializeToUtf8Bytes(root);
        V9OptionalCompatibilityParameters.ValidateRawInput(explicitBytes);
        var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(explicitBytes)!;
        V9OptionalCompatibilityParameters.Validate(plan);
        var report = V9OptionalCompatibilityParameters.Describe(explicitBytes, plan,
            [new V9NestedCandidateEvaluation("test-sha",1,null,Array.Empty<string>(),
                OptionId:plan.Calibration.ModelOptions[0].OptionId)]).Single();
        Assert.That(report.NeutralParameters["ClusterUnit"].Source,
            Is.EqualTo(V9OptionalCompatibilityParameters.FromPlan));
        Assert.That(report.NeutralParameters["RecallEquivalenceMargin"].Source,
            Is.EqualTo(V9OptionalCompatibilityParameters.FromPlan));
        root["UnexpectedJParameter"] = JsonSerializer.SerializeToElement(123);
        var strict = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        Assert.Throws<JsonException>((Action)(() =>
            JsonSerializer.Deserialize<V9NestedCalibrationPlan>(JsonSerializer.Serialize(root), strict)));
        root.Remove("UnexpectedJParameter");
        root["ClusterUnit"] = JsonSerializer.SerializeToElement<string?>(null);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9OptionalCompatibilityParameters.ValidateRawInput(JsonSerializer.SerializeToUtf8Bytes(root))));
    }

    [Test]
    public void Nonneutral_new_values_are_not_ignored_or_silently_selected()
    {
        var legacy = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(HistoricalPlanBytes())!;
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9OptionalCompatibilityParameters.Validate(legacy with { RecallEquivalenceMargin = .01m })));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9OptionalCompatibilityParameters.Validate(legacy with { ClusterUnit = "PAIR" })));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9OptionalCompatibilityParameters.Validate(legacy with { RecalibrationTriggers = true })));
    }
}

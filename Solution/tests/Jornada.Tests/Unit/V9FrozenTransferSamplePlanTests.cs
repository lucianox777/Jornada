using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FrozenTransferSamplePlanTests
{
    private static V9StatisticalSamplePlan.FrozenTransferSampleDesign Design() =>
        new(2, 4, 1.35, 1.3, .0036, .80, .20, .20, .90, .05);

    [Test]
    public void Tiny_paired_recall_gain_requires_real_independent_identity_sample()
    {
        var d = Design();
        var result = V9StatisticalSamplePlan.CalculateFrozenTransfer(d);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.PairedValidationIdentitiesForPower, Is.GreaterThan(14_000_000L));
            Assert.That(result.ValidationIdentitiesNeeded,
                Is.GreaterThanOrEqualTo(result.PairedValidationIdentitiesForPower));
            Assert.That(result.BaseIdentitiesNeededUnderAssumedRetention, Is.GreaterThan(80_000_000L));
            Assert.That(result.FdrRisk.ExternalValidationMultiplicity, Is.EqualTo(2));
            Assert.That(result.FdrRisk.FrozenTestMultiplicity, Is.EqualTo(1));
            Assert.That(result.FdrRisk.RiskBands.Single().MaximumFdr, Is.EqualTo(.01));
            Assert.That(result.PairedContrasts, Is.EqualTo(1));
            Assert.That(result.Status, Does.Contain("NO_CERTIFICATION"));
        }));
        // Tightest integer bound from two one-sided Hoeffding tails; no
        // counting individual source pairs as IID trial units.
        var alpha = d.FamilyAlpha / 2;
        double threshold(long n) => d.MaximumSourcesPerBaseIdentity * Math.Sqrt(2 * Math.Log(1 / alpha) / n)
            + d.MaximumSourcesPerBaseIdentity * Math.Sqrt(2 * Math.Log(1 / (1 - d.PairedPower)) / n);
        var target = d.MinimumPairedRecallGain * d.AssumedEligibleSourcesPerBaseIdentity;
        Assert.That(threshold(result.PairedValidationIdentitiesForPower), Is.LessThanOrEqualTo(target));
        Assert.That(threshold(result.PairedValidationIdentitiesForPower - 1), Is.GreaterThan(target));
    }

    [Test]
    public void Larger_contrast_reduces_paired_power_need_but_not_zero_error_fdr_need()
    {
        var baseline = V9StatisticalSamplePlan.CalculateFrozenTransfer(Design());
        var biggerContrast = V9StatisticalSamplePlan.CalculateFrozenTransfer(
            Design() with { MinimumPairedRecallGain = .0072 });
        Assert.That(biggerContrast.PairedValidationIdentitiesForPower,
            Is.LessThan(baseline.PairedValidationIdentitiesForPower));
        Assert.That(biggerContrast.FdrRisk.RiskBands.Single(),
            Is.EqualTo(baseline.FdrRisk.RiskBands.Single()));
        var threeFinalists = V9StatisticalSamplePlan.CalculateFrozenTransfer(
            Design() with { ValidationFinalists = 3 });
        Assert.That(threeFinalists.FdrRisk.RiskBands.Single().ValidationIndependentUnitsZeroError,
            Is.GreaterThan(baseline.FdrRisk.RiskBands.Single().ValidationIndependentUnitsZeroError!.Value));
        Assert.That(threeFinalists.FdrRisk.RiskBands.Single().TestIndependentUnitsZeroError,
            Is.EqualTo(baseline.FdrRisk.RiskBands.Single().TestIndependentUnitsZeroError));
    }

    [Test]
    public void Nondeclared_or_impossible_design_is_rejected_fail_closed()
    {
        var d = Design();
        var bad = new[]
        {
            d with { ValidationFinalists = 4 },
            d with { MaximumSourcesPerBaseIdentity = 0 },
            d with { AssumedAcceptedPerBaseIdentity = 4.5 },
            d with { AssumedEligibleSourcesPerBaseIdentity = double.NaN },
            d with { MinimumPairedRecallGain = 0 },
            d with { PairedPower = 1 },
            d with { FamilyAlpha = double.PositiveInfinity },
            d with { ValidationFraction = .8 },
            d with { RetainedBaseIdentityFraction = 0 },
            d with { MinimumPairedRecallGain = 1e-12 },
        };
        foreach (var item in bad)
            Assert.That((Action)(() => V9StatisticalSamplePlan.CalculateFrozenTransfer(item)),
                Throws.TypeOf<InvalidDataException>());
    }
}

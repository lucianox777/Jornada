using System;
using System.Collections.Generic;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9TfDirectionDecision19Tests
{
    private const string TrainSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void UnseenTrainU_MustPreserveRareNameAdvantage_Decision19()
    {
        // Frozen TRAIN: 2 nonmatch agreements in 10 candidate pairs, independent
        // of the IBGE frequencies of terms not encountered in the TRAIN frame.
        var uref = V9NominalTfUReference.Fit(CompositeNominalTfSideV9.Person,
            NominalFrequencyUnitV9.FirstName, 10, 2, .5m, TrainSha);
        var ibge = new Dictionary<string, decimal>
        {
            ["OBSERVADO"] = .50m, ["COMUM"] = .40m, ["RARO"] = .01m
        };
        var common = Math.Log((double)(uref.ResolveTerm(ibge, "COMUM") / ibge["COMUM"]));
        var rare = Math.Log((double)(uref.ResolveTerm(ibge, "RARO") / ibge["RARO"]));
        Assert.That(rare, Is.GreaterThan(common));
        Assert.That(uref.ResolveTerm(ibge, "RARO"), Is.EqualTo(uref.ResolveTerm(ibge, "COMUM")));
        Assert.Throws<InvalidDataException>((Action)(() => uref.ResolveTerm(ibge, "SEM_IBGE")));
    }

    [Test]
    public void ObservedTrainU_MustNotRewardCommonNameOverRare_Decision19()
    {
        // Even if a conditional per-value diagnostic says common=0.80 and
        // rare=0.10, the actual score uses ONE nonmatch event U_ref.
        var observedConditionalU = new Dictionary<string, decimal>
        {
            ["COMUM"] = .80m, ["RARO"] = .10m
        };
        var ibge = new Dictionary<string, decimal>
        {
            ["COMUM"] = .40m, ["RARO"] = .10m
        };
        var uref = V9NominalTfUReference.Fit(CompositeNominalTfSideV9.Person,
            NominalFrequencyUnitV9.FirstName, 10, 2, .5m, TrainSha);
        var common = Math.Log((double)(uref.ResolveTerm(ibge, "COMUM") / ibge["COMUM"]));
        var rare = Math.Log((double)(uref.ResolveTerm(ibge, "RARO") / ibge["RARO"]));
        Assert.That(rare, Is.GreaterThan(common));
        Assert.That(observedConditionalU["COMUM"], Is.GreaterThan(observedConditionalU["RARO"]));
    }

    [Test]
    public void URef_is_frozen_by_side_unit_TRAIN_support_and_policy()
    {
        var reference = V9NominalTfUReference.Fit(CompositeNominalTfSideV9.Mother,
            NominalFrequencyUnitV9.LastContentSurname, 20, 3, 1m, TrainSha);
        Assert.That(reference.Probability, Is.EqualTo(4m / 22m));
        var tampered = reference with { Probability = .8m };
        Assert.Throws<InvalidDataException>((Action)(() => tampered.Validate(
            CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname, TrainSha)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            reference.Validate(CompositeNominalTfSideV9.Person,
                NominalFrequencyUnitV9.LastContentSurname, TrainSha)));
    }
}

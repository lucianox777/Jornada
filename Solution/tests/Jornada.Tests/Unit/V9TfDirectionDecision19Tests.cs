using System;
using System.Collections.Generic;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;
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
        // Mathematical counterexample for the RETIRED per-name U rule,
        // not a claim of a historically executed NUnit failure.
        // ln(.80/.40)=ln(2) > ln(.10/.10)=0 rewards the common name.
        var legacyCommon = Math.Log((double)(observedConditionalU["COMUM"] / ibge["COMUM"]));
        var legacyRare = Math.Log((double)(observedConditionalU["RARO"] / ibge["RARO"]));
        Assert.That(legacyCommon, Is.GreaterThan(legacyRare));
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
    [Test]
    public void URef_uses_all_nonmatch_agreements_even_if_composite_level_selects_no_TF()
    {
        // ExactFirstLast has agreeing first tokens, but the shared selector
        // intentionally applies no first-name TF to that composite level.
        // Decision 19 defines U_ref from the agreement EVENT, not the
        // subset of levels selected to receive the TF correction.
        var rows = new[]
        {
            new V9FsAdaptiveCandidatePair("TRAIN", "A", "B", false,
                "MARIA LUZ SILVA", "MARIA ANA SILVA", null, null, null, null, 1),
            new V9FsAdaptiveCandidatePair("TRAIN", "C", "D", false,
                "JOANA LUZ COSTA", "JOANA ANA COSTA", null, null, null, null, 1),
            new V9FsAdaptiveCandidatePair("TRAIN", "E", "F", false,
                "ANA SILVA", "BIA COSTA", null, null, null, null, 1),
            new V9FsAdaptiveCandidatePair("TRAIN", "G", "H", false,
                "CARLA COSTA", "DORA SILVA", null, null, null, null, 1)
        };
        Assert.That(CompositeNominalComparisonV9.Classify(
            rows[0].LeftPersonName, rows[0].RightPersonName),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirstLast));
        var fitted = V9TrainSqlNominalUCalibration.Fit(rows,
            CompositeNominalTfSideV9.Person, .5m, TrainSha);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(fitted.UnmatchedUnitCandidatePairs, Is.Zero,
                "No selected-level diagnostic agreements in this TRAIN frame.");
            Assert.That(fitted.HasSufficientSupport, Is.True,
                "The reference event has two agreements in four nonmatches.");
            Assert.That(fitted.NominalUReference?.UnmatchedCandidatePairs, Is.EqualTo(4));
            Assert.That(fitted.NominalUReference?.MatchingNominalPairs, Is.EqualTo(2));
            Assert.That(fitted.NominalUReference?.Probability, Is.EqualTo(.5m));
        }));
    }

    [TestCase(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName)]
    [TestCase(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName)]
    [TestCase(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname)]
    [TestCase(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname)]
    public void URef_is_constant_across_observed_and_unseen_values_for_each_side_unit(
        CompositeNominalTfSideV9 side, NominalFrequencyUnitV9 unit)
    {
        var fitted = V9NominalTfUReference.Fit(side, unit, 12, 3, .5m, TrainSha);
        var ibge = new Dictionary<string, decimal>
        {
            ["COMUM"] = .40m, ["RARO"] = .01m, ["NAO_VISTO"] = .001m
        };
        var common = fitted.ResolveTerm(ibge, "COMUM");
        var rare = fitted.ResolveTerm(ibge, "RARO");
        var unseen = fitted.ResolveTerm(ibge, "NAO_VISTO");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(rare, Is.EqualTo(common));
            Assert.That(unseen, Is.EqualTo(common));
            Assert.That(Math.Log((double)(rare / ibge["RARO"])),
                Is.GreaterThan(Math.Log((double)(common / ibge["COMUM"]))));
            Assert.That(Math.Log((double)(unseen / ibge["NAO_VISTO"])),
                Is.GreaterThan(Math.Log((double)(rare / ibge["RARO"]))));
        }));
        Assert.Throws<InvalidDataException>((Action)(() => fitted.ResolveTerm(ibge, "AUSENTE")));
    }

}

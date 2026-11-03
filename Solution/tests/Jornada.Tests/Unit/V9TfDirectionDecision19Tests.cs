using System;
using System.Collections.Generic;
using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class V9TfDirectionDecision19Tests
{
    [Test]
    public void UnseenTrainU_MustPreserveRareNameAdvantage_Decision19()
    {
        // Synthetic TRAIN conditional U: 90% observed and 10% reserved.
        // The current residual allocation is proportional to IBGE frequency.
        // Decision 19 requires a single U_ref per side/unit so that a rarer
        // exact agreement receives a strictly larger TF adjustment.
        var trainU = new Dictionary<string, decimal> { ["OBSERVADO"] = 0.90m };
        var ibge = new Dictionary<string, decimal>
        {
            ["OBSERVADO"] = 0.50m,
            ["COMUM"] = 0.40m,
            ["RARO"] = 0.01m
        };
        var allocation = new V9UnseenTfUAllocation(
            V9UnseenTfUDistribution.Version, 0.10m, 0.41m);
        var commonU = V9UnseenTfUDistribution.Resolve(trainU, ibge, allocation, "COMUM");
        var rareU = V9UnseenTfUDistribution.Resolve(trainU, ibge, allocation, "RARO");
        var commonTf = Math.Log((double)(commonU / ibge["COMUM"]));
        var rareTf = Math.Log((double)(rareU / ibge["RARO"]));
        Assert.That(rareTf, Is.GreaterThan(commonTf),
            "Decision 19: the fitted TRAIN U must not cancel the rarity advantage.");
    }
    [Test]
    public void ObservedTrainU_MustNotRewardCommonNameOverRare_Decision19()
    {
        // Deliberately adversarial TRAIN distribution: the observed U for
        // the common value is larger than the rare value's marginal advantage.
        // A per-value U ratio can invert TF; a single U_ref cannot.
        var trainU = new Dictionary<string, decimal>
        {
            ["COMUM"] = 0.80m,
            ["RARO"] = 0.10m
        };
        var ibge = new Dictionary<string, decimal>
        {
            ["COMUM"] = 0.40m,
            ["RARO"] = 0.10m
        };
        var commonU = V9UnseenTfUDistribution.Resolve(trainU, ibge, null, "COMUM");
        var rareU = V9UnseenTfUDistribution.Resolve(trainU, ibge, null, "RARO");
        var commonTf = Math.Log((double)(commonU / ibge["COMUM"]));
        var rareTf = Math.Log((double)(rareU / ibge["RARO"]));
        Assert.That(rareTf, Is.GreaterThan(commonTf),
            "Decision 19: observed value-specific U must not invert TF.");
    }

}

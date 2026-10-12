using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkageTermFrequencyOrderingEnvelopeTests
{
    private static NominalTermFrequencySnapshot Snapshot() =>
        NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute, "MARIA", 600, 1000, .6m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute, "ZULEICA", 10, 1000, .01m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.MotherFirstNameAttribute, "ANA", 500, 1000, .5m)
        });

    private static Dictionary<string, decimal> Parameters(
        decimal personExactM = .25m, decimal personExactU = .25m,
        decimal personHighM = .25m, decimal personHighU = .25m,
        decimal motherExactM = .8m, decimal motherExactU = .01m,
        decimal motherHighM = .1m, decimal motherHighU = .1m,
        decimal weight = 1m)
        => new(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.TermFrequencyScoring] = 1m,
            [LinkageParameterCatalog.TermFrequencyWeight] = weight,
            [LinkageParameterCatalog.TermFrequencyMinimumU] = .01m,
            ["M_NOME_EXACT"] = personExactM,
            ["U_NOME_EXACT"] = personExactU,
            ["M_NOME_HIGH"] = personHighM,
            ["U_NOME_HIGH"] = personHighU,
            ["M_NOME_MAE_EXACT"] = motherExactM,
            ["U_NOME_MAE_EXACT"] = motherExactU,
            ["M_NOME_MAE_HIGH"] = motherHighM,
            ["U_NOME_MAE_HIGH"] = motherHighU,
        };

    [Test]
    public void P0_CommonExactHasNegativeMarginAndMustNotProduceNewDraft()
    {
        var result = LinkageTermFrequencyOrderingEnvelope.Assess(
            Parameters(), Snapshot()).Single(x => x.Attribute == "NOME");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.ExactBaseLogOdds, Is.EqualTo(0d).Within(1e-12));
            Assert.That(result.HighLogOdds, Is.EqualTo(0d).Within(1e-12));
            Assert.That(result.WorstPublishedExactLogOdds,
                Is.EqualTo(Math.Log(.25d / .6d)).Within(1e-12));
            Assert.That(result.IsStrictlyOrdered, Is.False);
        }));
        Assert.That((Action)(() =>
                LinkageTermFrequencyOrderingEnvelope.RequireStrictOrdering(Parameters(), Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("EXACT pode ficar abaixo/empatar HIGH"));
    }

    [Test]
    public void CalibratedIllustrativeDistributionPassesWithoutScoreRewriting()
    {
        var parameters = Parameters(
            personExactM: .8m, personExactU: .01m,
            personHighM: .1m, personHighU: .1m);
        var results = LinkageTermFrequencyOrderingEnvelope.Assess(parameters, Snapshot());
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(x => x.IsStrictlyOrdered), Is.True);
        Assert.DoesNotThrow((Action)(() =>
            LinkageTermFrequencyOrderingEnvelope.RequireStrictOrdering(parameters, Snapshot())));
        // TF math is unchanged; verification is pre-publish only.
        var exact = results.Single(x => x.Attribute == "NOME");
        Assert.That(exact.WorstPublishedExactLogOdds,
            Is.EqualTo(Math.Log(.8d / .01d) +
                       SplinkCompatibleTermFrequency.LogBayesAdjustment(
                           .6m, .6m, .01m, 1m, .01m)).Within(1e-12));
    }

    [Test]
    public void MotherFrequencyIsIndependentAndCanRejectEvenIfPersonIsSafe()
    {
        var parameters = Parameters(
            personExactM: .8m, personExactU: .01m,
            personHighM: .1m, personHighU: .1m,
            motherExactM: .2m, motherExactU: .2m,
            motherHighM: .2m, motherHighU: .2m);
        var results = LinkageTermFrequencyOrderingEnvelope.Assess(parameters, Snapshot());
        Assert.That(results.Single(x => x.Attribute == "NOME").IsStrictlyOrdered, Is.True);
        Assert.That(results.Single(x => x.Attribute == "NOME_MAE").IsStrictlyOrdered, Is.False);
        Assert.That((Action)(() =>
                LinkageTermFrequencyOrderingEnvelope.RequireStrictOrdering(parameters, Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("NOME_MAE"));
    }

    [Test]
    public void ZeroWeightPreservesBaseFsOrderingAndDoesNotManufactureEvidence()
    {
        var p = Parameters(
            personExactM: .6m, personExactU: .2m,
            personHighM: .1m, personHighU: .2m,
            weight: 0m);
        var name = LinkageTermFrequencyOrderingEnvelope.Assess(p, Snapshot())
            .Single(x => x.Attribute == "NOME");
        Assert.That(name.WorstPublishedExactLogOdds,
            Is.EqualTo(name.ExactBaseLogOdds).Within(1e-12));
        Assert.That(name.IsStrictlyOrdered, Is.True);
    }

    [Test]
    public void EqualEvidenceIsNotCertifiedBecauseRankUsesUuidTiebreaker()
    {
        var p = Parameters(weight: 0m);
        Assert.That(LinkageTermFrequencyOrderingEnvelope.Assess(p, Snapshot())
            .Single(x => x.Attribute == "NOME").IsStrictlyOrdered, Is.False);
    }

    [Test]
    public void MissingOrOutOfDomainInputsFailClosed()
    {
        var p = Parameters();
        p.Remove("U_NOME_HIGH");
        Assert.That((Action)(() => LinkageTermFrequencyOrderingEnvelope.Assess(p, Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("U_NOME_HIGH"));
        p = Parameters();
        p[LinkageParameterCatalog.TermFrequencyMinimumU] = 0m;
        Assert.That((Action)(() => LinkageTermFrequencyOrderingEnvelope.Assess(p, Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("Peso ou piso TF"));
        p = Parameters();
        p[LinkageParameterCatalog.TermFrequencyWeight] = -1m;
        Assert.That((Action)(() => LinkageTermFrequencyOrderingEnvelope.Assess(p, Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("Peso ou piso TF"));
    }
}

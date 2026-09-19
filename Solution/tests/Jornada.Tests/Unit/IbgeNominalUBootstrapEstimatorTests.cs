using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IbgeNominalUBootstrapEstimatorTests
{
    [Test]
    public void Estimate_IsDeterministic_AndUsesPublishedMarginalWeights()
    {
        var entries = new[]
        {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 3),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "BIA", 1),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 2),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SOUZA", 2)
        };
        var options = new IbgeNominalUBootstrapOptions(20260917, 20_000);

        var first = IbgeNominalUBootstrapEstimator.Estimate(entries, options);
        var replay = IbgeNominalUBootstrapEstimator.Estimate(entries, options);

        Assert.Multiple(() =>
        {
            Assert.That(Projection(replay), Is.EqualTo(Projection(first)));
            Assert.That(first.AnalyticExactFirstNameProbability, Is.EqualTo(0.625m));
            Assert.That(first.AnalyticExactSurnameProbability, Is.EqualTo(0.5m));
            Assert.That(first.AnalyticExactSyntheticFullNameProbability, Is.EqualTo(0.3125m));
            Assert.That(first.States.Sum(state => state.Support), Is.EqualTo(options.PairCount));
            Assert.That(first.States.Sum(state => state.Probability), Is.EqualTo(1m));
            Assert.That(first.ObservationChannelVersion, Is.EqualTo("CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1"));
        });

        var exact = first.States.Single(state => state.State == "EXACT");
        Assert.That(
            Math.Abs(exact.Probability - first.AnalyticExactSyntheticFullNameProbability),
            Is.LessThan(0.015m),
            "Monte Carlo deve convergir para a colisão exata analítica da composição sintética.");
    }

    private static IReadOnlyList<string> Projection(IbgeNominalUBootstrapEstimate result) =>
        new[]
        {
            result.MethodVersion,
            result.JointConstructionVersion,
            result.ObservationChannelVersion,
            result.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.PairCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.AnalyticExactFirstNameProbability.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.AnalyticExactSurnameProbability.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.AnalyticExactSyntheticFullNameProbability.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }
        .Concat(result.States.Select(state => string.Join('|',
            state.State,
            state.Support,
            state.Probability,
            state.StandardError)))
        .ToArray();

    [Test]
    public void Estimate_UsesSelectedNameComparisonContractWithoutChangingDefaultV1()
    {
        var entries = new[]
        {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 100),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 100),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SOUZA", 100)
        };
        var options = new IbgeNominalUBootstrapOptions(20260918, 20_000);

        var v1 = IbgeNominalUBootstrapEstimator.Estimate(entries, options);
        var v2 = IbgeNominalUBootstrapEstimator.Estimate(
            entries,
            options,
            NameComparisonContract.PtBrContentTokenGuardV2);

        var v1Medium = v1.States.Single(state => state.State == "MEDIUM");
        var v1Low = v1.States.Single(state => state.State == "LOW");
        var v2Medium = v2.States.Single(state => state.State == "MEDIUM");
        var v2Low = v2.States.Single(state => state.State == "LOW");

        Assert.Multiple(() =>
        {
            Assert.That(v1Medium.Support, Is.GreaterThan(0));
            Assert.That(v1Low.Support, Is.EqualTo(0));
            Assert.That(v2Medium.Support, Is.EqualTo(0));
            Assert.That(v2Low.Support, Is.EqualTo(v1Medium.Support));
            Assert.That(v2.States.Sum(state => state.Support), Is.EqualTo(options.PairCount));
        });
    }

    [Test]
    public void Estimate_AllowsExactSameNameForDifferentSyntheticIdentities()
    {
        var entries = new[]
        {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 100),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 100)
        };

        var result = IbgeNominalUBootstrapEstimator.Estimate(
            entries,
            new IbgeNominalUBootstrapOptions(7, 1_000));

        var exact = result.States.Single(state => state.State == "EXACT");
        Assert.Multiple(() =>
        {
            Assert.That(exact.Support, Is.EqualTo(1_000));
            Assert.That(exact.Probability, Is.EqualTo(1m));
            Assert.That(result.AnalyticExactSyntheticFullNameProbability, Is.EqualTo(1m));
        });
    }

    [Test]
    public void Estimate_RejectsMissingMarginalInsteadOfInventingFrequency()
    {
        var entries = new[]
        {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 100)
        };

        Assert.That(
            () => IbgeNominalUBootstrapEstimator.Estimate(
                entries,
                new IbgeNominalUBootstrapOptions(1, 1_000)),
            Throws.ArgumentException.With.Message.Contains("Surname"));
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class NominalDfCalibrationDatasetTests
{
    [Test]
    public void Create_uses_published_first_name_probability_and_keeps_missing_frequency_censored()
    {
        var matched = new[]
        {
            Pair("BIA SILVA", "BIA SOUZA")
        };
        var unmatched = new[]
        {
            Pair("ANA SILVA", "BIA SOUZA"),
            Pair("ANA SILVA", "CARLA SOUZA")
        };
        var reference = new[]
        {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 3),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "BIA", 1),
            // Não pode contaminar a distribuição de primeiro nome.
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 10_000)
        };

        var dataset = NominalDfCalibrationDatasetFactory.Create(
            matched,
            unmatched,
            reference);

        var trueMatch = dataset.Observations.Single(observation => observation.IsTrueMatch);
        var censored = dataset.Observations.Single(observation =>
            observation.Evidence.FrequencyCensored);

        Assert.Multiple(() =>
        {
            Assert.That(dataset.Observations, Has.Count.EqualTo(3));
            Assert.That(dataset.MatchedInputCount, Is.EqualTo(1));
            Assert.That(dataset.UnmatchedInputCount, Is.EqualTo(2));
            Assert.That(dataset.FrequencyCensoredCount, Is.EqualTo(1));
            Assert.That(dataset.PublishedFirstNameOccurrences, Is.EqualTo(4));
            Assert.That(dataset.ReferenceExactUProbability, Is.EqualTo(0.625m));
            Assert.That(dataset.PublicationMethodVersion,
                Is.EqualTo("IBGE_CENSO_2022_NOMES_PUBLICACAO_V1"));
            Assert.That(dataset.EvidenceAlgorithmVersion,
                Is.EqualTo("NOMINAL_DF_SPLINK_COMPATIBLE_V1"));

            Assert.That(trueMatch.Evidence.LeftFrequency, Is.EqualTo(0.25m));
            Assert.That(trueMatch.Evidence.RightFrequency, Is.EqualTo(0.25m));
            Assert.That(trueMatch.Evidence.EffectiveFrequency, Is.EqualTo(0.25m));
            Assert.That(trueMatch.Evidence.TermFrequencyLogAdjustment,
                Is.EqualTo(Math.Log(2.5d)).Within(1e-12));

            Assert.That(censored.Evidence.LeftFrequency, Is.EqualTo(0.75m));
            Assert.That(censored.Evidence.RightFrequency, Is.Null);
            Assert.That(censored.Evidence.TermFrequencyLogAdjustment, Is.Null);
        });
    }

    [Test]
    public void Censored_observation_remains_inconclusive_in_df_threshold_evaluation()
    {
        var dataset = NominalDfCalibrationDatasetFactory.Create(
            new[] { Pair("BIA SILVA", "BIA SOUZA") },
            new[]
            {
                Pair("ANA SILVA", "BIA SOUZA"),
                Pair("ANA SILVA", "CARLA SOUZA")
            },
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 3),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "BIA", 1)
            });

        var matchEvidence = dataset.Observations.Single(observation => observation.IsTrueMatch).Evidence;
        var candidate = new DfThresholdCandidate(
            "MATCH_FRONTIER",
            matchEvidence.Similarity,
            matchEvidence.TermFrequencyLogAdjustment!.Value);

        var evaluation = DfThresholdSearch.Evaluate(candidate, dataset.Observations);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.TruePositive, Is.EqualTo(1));
            Assert.That(evaluation.FalsePositive, Is.EqualTo(0));
            Assert.That(evaluation.Inconclusive, Is.EqualTo(2));
            Assert.That(evaluation.Total, Is.EqualTo(3));
        });
    }

    [Test]
    public void Create_does_not_infer_surname_frequency_from_full_name_tokens()
    {
        var dataset = NominalDfCalibrationDatasetFactory.Create(
            new[] { Pair("ANA RARA", "ANA COMUM") },
            new[] { Pair("BIA RARA", "ANA COMUM") },
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 9),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "BIA", 1),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "RARA", 1),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "COMUM", 999_999)
            });

        var match = dataset.Observations.Single(observation => observation.IsTrueMatch);

        Assert.Multiple(() =>
        {
            Assert.That(dataset.PublishedFirstNameOccurrences, Is.EqualTo(10));
            Assert.That(dataset.ReferenceExactUProbability, Is.EqualTo(0.82m));
            Assert.That(match.Evidence.LeftFrequency, Is.EqualTo(0.9m));
            Assert.That(match.Evidence.RightFrequency, Is.EqualTo(0.9m));
        });
    }

    private static IdentityTrainingPair Pair(string leftName, string rightName) =>
        new(
            leftName,
            new DateOnly(1980, 1, 1),
            null,
            rightName,
            new DateOnly(1980, 1, 1),
            null);
}

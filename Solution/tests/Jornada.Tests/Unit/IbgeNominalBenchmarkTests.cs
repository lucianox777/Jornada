using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IbgeNominalBenchmarkTests
{
    [Test]
    public void Benchmark_UsesOnlyObservedIbgeVocabularyAndKeepsPersonInSinglePartition()
    {
        var snapshot = Snapshot();
        var people = new[]
        {
            new IbgeBenchmarkPerson("P1", "MARIA", "SILVA"),
            new IbgeBenchmarkPerson("P2", "MARINA", "SOUZA"),
            new IbgeBenchmarkPerson("P3", "MARIO", "SOUSA"),
            new IbgeBenchmarkPerson("P4", "JOSE", "SANTOS"),
            new IbgeBenchmarkPerson("P5", "JOAO", "SANTO")
        };
        var options = new IbgeNominalBenchmarkOptions(Seed: 20260913, MinimumSupportOccurrences: 10);

        var first = IbgeNominalBenchmarkGenerator.Generate(snapshot, people, options);
        var replay = IbgeNominalBenchmarkGenerator.Generate(snapshot, people, options);

        Assert.That(ReplayProjection(replay), Is.EqualTo(ReplayProjection(first)));

        var observedFirstNames = snapshot.Entries
            .Where(x => x.StatisticKind == IbgeNameStatisticKind.FirstName)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);
        var observedSurnames = snapshot.Entries
            .Where(x => x.StatisticKind == IbgeNameStatisticKind.Surname)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(first.Where(x => x.IsTrueMatch), Is.Not.Empty);
            Assert.That(first.All(pair => observedFirstNames.Contains(pair.Left.FirstName) && observedFirstNames.Contains(pair.Right.FirstName)), Is.True);
            Assert.That(first.All(pair => observedSurnames.Contains(pair.Left.Surname) && observedSurnames.Contains(pair.Right.Surname)), Is.True);
            Assert.That(first.SelectMany(pair => pair.Substitutions).All(substitution => substitution.SourceValue != substitution.TargetValue), Is.True);
        });

        foreach (var person in people)
        {
            var partitions = first
                .Where(pair => pair.Left.BasePersonId == person.BasePersonId || pair.Right.BasePersonId == person.BasePersonId)
                .Select(pair => pair.Partition)
                .Distinct()
                .ToArray();
            Assert.That(partitions, Has.Length.EqualTo(1), $"{person.BasePersonId} vazou entre partições.");
        }
    }

    [Test]
    public void Benchmark_RejectsNameOutsideSupportedIbgeUniverse()
    {
        var people = new[]
        {
            new IbgeBenchmarkPerson("P1", "INVENTADO", "SILVA"),
            new IbgeBenchmarkPerson("P2", "MARIA", "SOUZA")
        };

        Assert.That(
            () => IbgeNominalBenchmarkGenerator.Generate(
                Snapshot(),
                people,
                new IbgeNominalBenchmarkOptions(1, 10)),
            Throws.ArgumentException.With.Message.Contains("não pertence ao universo IBGE"));
    }

    [Test]
    public void FactorialCandidates_CreateAllMAndUCombinationsWithoutMixingPrefixes()
    {
        var m = new[]
        {
            new ParameterEstimate(ParameterEstimatorKind.Jornada, "J1", new Dictionary<string, decimal> { ["M_NOME_EXACT"] = 0.9m }),
            new ParameterEstimate(ParameterEstimatorKind.Splink, "S1", new Dictionary<string, decimal> { ["M_NOME_EXACT"] = 0.8m })
        };
        var u = new[]
        {
            new ParameterEstimate(ParameterEstimatorKind.Jornada, "J1", new Dictionary<string, decimal> { ["U_NOME_EXACT"] = 0.01m }),
            new ParameterEstimate(ParameterEstimatorKind.Splink, "S1", new Dictionary<string, decimal> { ["U_NOME_EXACT"] = 0.02m })
        };

        var candidates = FactorialCalibrationCandidateFactory.Create(
            m,
            u,
            new Dictionary<string, decimal> { ["PRIOR_MATCH_PROBABILITY"] = 0.001m });

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(4));
            Assert.That(candidates.Any(x => x.MEstimator == ParameterEstimatorKind.Splink && x.UEstimator == ParameterEstimatorKind.Jornada), Is.True);
            Assert.That(candidates.All(x => x.Parameters.ContainsKey("M_NOME_EXACT")), Is.True);
            Assert.That(candidates.All(x => x.Parameters.ContainsKey("U_NOME_EXACT")), Is.True);
            Assert.That(candidates.All(x => x.Parameters["PRIOR_MATCH_PROBABILITY"] == 0.001m), Is.True);
        });
    }

    [Test]
    public void FactorialCandidates_RejectPartialEstimatorMasqueradingAsCompleteModel()
    {
        var m = new[]
        {
            new ParameterEstimate(ParameterEstimatorKind.Jornada, "J1", new Dictionary<string, decimal>
            {
                ["M_NOME_EXACT"] = 0.9m,
                ["M_NASC_ANO_EXACT"] = 0.8m
            }),
            new ParameterEstimate(ParameterEstimatorKind.Splink, "S1", new Dictionary<string, decimal>
            {
                ["M_NOME_EXACT"] = 0.85m
            })
        };
        var u = new[]
        {
            new ParameterEstimate(ParameterEstimatorKind.Jornada, "J1", new Dictionary<string, decimal>
            {
                ["U_NOME_EXACT"] = 0.01m,
                ["U_NASC_ANO_EXACT"] = 0.02m
            }),
            new ParameterEstimate(ParameterEstimatorKind.Splink, "S1", new Dictionary<string, decimal>
            {
                ["U_NOME_EXACT"] = 0.03m
            })
        };

        Assert.That(
            () => FactorialCalibrationCandidateFactory.Create(m, u),
            Throws.ArgumentException.With.Message.Contains("mesmo conjunto semântico"));
    }

    private static IReadOnlyList<string> ReplayProjection(IEnumerable<IbgeNominalBenchmarkPair> pairs) =>
        pairs.Select(pair => string.Join('|',
            pair.PairId,
            pair.Partition,
            pair.IsTrueMatch,
            pair.Left.BasePersonId,
            pair.Left.FirstName,
            pair.Left.Surname,
            pair.Left.BirthDate,
            pair.Right.BasePersonId,
            pair.Right.FirstName,
            pair.Right.Surname,
            pair.Right.BirthDate,
            string.Join(';', pair.Substitutions.Select(substitution => string.Join(':',
                substitution.StatisticKind,
                substitution.SourceValue,
                substitution.TargetValue,
                substitution.SourceOccurrences,
                substitution.TargetOccurrences,
                substitution.Similarity)))))
            .ToArray();

    private static IbgeTypedNameFrequencySnapshot Snapshot() =>
        IbgeTypedNameFrequencyCatalog.Create(
            "TEST-IBGE-1",
            IbgeGeographicScope.Brazil,
            geographicCode: null,
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 1000),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARINA", 700),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIO", 600),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "JOSE", 900),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "JOAO", 850),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 2000),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SOUZA", 1200),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SOUSA", 1100),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SANTOS", 1800),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SANTO", 300)
            });
}

using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class NominalDfBenchmarkCalibrationTests
{
    [Test]
    public void Calibrate_selects_frontier_only_on_validation_and_freezes_it_for_test()
    {
        var snapshot = Snapshot();
        var options = new IbgeNominalBenchmarkOptions(
            Seed: 20260918,
            MinimumSupportOccurrences: 10);

        var validation = ValidationPairs();
        var firstTest = new[]
        {
            Pair("T-M-1", BenchmarkPartition.Test, true, "T1", "ANA", "T1", "ANNA"),
            Pair("T-U-1", BenchmarkPartition.Test, false, "T2", "MARIO", "T3", "MARINA")
        };
        var secondTest = new[]
        {
            Pair("T-M-2", BenchmarkPartition.Test, true, "T4", "MARIA", "T4", "MARIA"),
            Pair("T-U-2", BenchmarkPartition.Test, false, "T5", "JOSE", "T6", "BIA")
        };

        var first = NominalDfBenchmarkCalibrator.Calibrate(
            snapshot,
            validation.Concat(firstTest),
            options);
        var second = NominalDfBenchmarkCalibrator.Calibrate(
            snapshot,
            validation.Concat(secondTest),
            options);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.ValidationCandidates.Select(static candidate => candidate.CandidateId),
                Is.EqualTo(second.ValidationCandidates.Select(static candidate => candidate.CandidateId)),
                "TEST não pode criar nem remover thresholds candidatos.");

            Assert.That(
                first.FrozenFrontier.Select(static item => item.Candidate.CandidateId),
                Is.EqualTo(second.FrozenFrontier.Select(static item => item.Candidate.CandidateId)),
                "TEST não pode alterar a fronteira selecionada em VALIDATION.");

            Assert.That(first.FrozenFrontier, Is.Not.Empty);
            Assert.That(first.FrozenFrontier.All(static item =>
                item.Candidate.CandidateId == item.Validation.CandidateId &&
                item.Candidate.CandidateId == item.Test.CandidateId), Is.True);
        });
    }

    [Test]
    public void Calibrate_ignores_train_for_threshold_selection_and_preserves_provenance()
    {
        var snapshot = Snapshot();
        var options = new IbgeNominalBenchmarkOptions(
            Seed: 424242,
            MinimumSupportOccurrences: 10,
            TrainBasisPoints: 5000,
            ValidationBasisPoints: 2500,
            TestBasisPoints: 2500);

        var validationAndTest = ValidationPairs()
            .Concat(new[]
            {
                Pair("T-M", BenchmarkPartition.Test, true, "T1", "ANA", "T1", "ANNA"),
                Pair("T-U", BenchmarkPartition.Test, false, "T2", "MARIO", "T3", "BIA")
            })
            .ToArray();

        var withTrain = validationAndTest
            .Concat(new[]
            {
                Pair("R-M", BenchmarkPartition.Train, true, "R1", "MARIA", "R1", "BIA"),
                Pair("R-U", BenchmarkPartition.Train, false, "R2", "JOSE", "R3", "ANA")
            })
            .ToArray();

        var baseline = NominalDfBenchmarkCalibrator.Calibrate(
            snapshot,
            validationAndTest,
            options);
        var result = NominalDfBenchmarkCalibrator.Calibrate(
            snapshot,
            withTrain,
            options);

        var expectedTotal = snapshot.Entries
            .Where(static entry =>
                entry.StatisticKind == IbgeNameStatisticKind.FirstName &&
                entry.Occurrences > 0)
            .Sum(static entry => entry.Occurrences);

        var probabilities = snapshot.Entries
            .Where(static entry =>
                entry.StatisticKind == IbgeNameStatisticKind.FirstName &&
                entry.Occurrences > 0)
            .Select(entry => (decimal)entry.Occurrences / expectedTotal)
            .ToArray();
        var expectedExactU = probabilities.Sum(static probability => probability * probability);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.ValidationCandidates.Select(static candidate => candidate.CandidateId),
                Is.EqualTo(baseline.ValidationCandidates.Select(static candidate => candidate.CandidateId)));

            Assert.That(result.ValidationObservationCount, Is.EqualTo(2));
            Assert.That(result.TestObservationCount, Is.EqualTo(2));
            Assert.That(result.PublishedFirstNameOccurrences, Is.EqualTo(expectedTotal));
            Assert.That(result.ReferenceExactUProbability, Is.EqualTo(expectedExactU));
            Assert.That(result.ReferenceSource, Is.EqualTo(snapshot.Source));
            Assert.That(result.ReferenceSourceVersion, Is.EqualTo(snapshot.SourceVersion));
            Assert.That(result.ReferenceFingerprintSha256, Is.EqualTo(snapshot.FingerprintSha256));
            Assert.That(result.ReferenceGeographicScope, Is.EqualTo(snapshot.GeographicScope));
            Assert.That(result.ReferenceGeographicCode, Is.EqualTo(snapshot.GeographicCode));
            Assert.That(result.BenchmarkGeneratorVersion, Is.EqualTo(IbgeNominalBenchmarkOptions.GeneratorVersion));
            Assert.That(result.CalibrationAlgorithmVersion, Is.EqualTo(NominalDfBenchmarkCalibrator.AlgorithmVersion));
            Assert.That(result.BenchmarkSeed, Is.EqualTo(options.Seed));
            Assert.That(result.BenchmarkMinimumSupportOccurrences, Is.EqualTo(options.MinimumSupportOccurrences));
            Assert.That(result.TrainBasisPoints, Is.EqualTo(options.TrainBasisPoints));
            Assert.That(result.ValidationBasisPoints, Is.EqualTo(options.ValidationBasisPoints));
            Assert.That(result.TestBasisPoints, Is.EqualTo(options.TestBasisPoints));
            Assert.That(result.EvidenceAlgorithmVersion, Is.EqualTo(NominalDfEvidenceCalculator.AlgorithmVersion));
            Assert.That(result.TermFrequencyAlgorithmVersion, Is.EqualTo(SplinkCompatibleTermFrequency.AlgorithmVersion));
        });
    }

    [Test]
    public void Calibrate_rejects_person_leakage_between_partitions()
    {
        var pairs = new[]
        {
            Pair("V-M", BenchmarkPartition.Validation, true, "SHARED", "MARIA", "SHARED", "MARINA"),
            Pair("V-U", BenchmarkPartition.Validation, false, "V2", "JOSE", "V3", "JOAO"),
            Pair("T-M", BenchmarkPartition.Test, true, "SHARED", "ANA", "SHARED", "ANNA"),
            Pair("T-U", BenchmarkPartition.Test, false, "T2", "MARIO", "T3", "BIA")
        };

        Assert.That(
            () => NominalDfBenchmarkCalibrator.Calibrate(
                Snapshot(),
                pairs,
                new IbgeNominalBenchmarkOptions(1, 10)),
            Throws.ArgumentException.With.Message.Contains("vazamento"));
    }

    private static IbgeNominalBenchmarkPair[] ValidationPairs() =>
    [
        Pair("V-M", BenchmarkPartition.Validation, true, "V1", "MARIA", "V1", "MARINA"),
        Pair("V-U", BenchmarkPartition.Validation, false, "V2", "JOSE", "V3", "JOAO")
    ];

    private static IbgeNominalBenchmarkPair Pair(
        string pairId,
        BenchmarkPartition partition,
        bool isTrueMatch,
        string leftPersonId,
        string leftFirstName,
        string rightPersonId,
        string rightFirstName) =>
        new(
            pairId,
            partition,
            isTrueMatch,
            new IbgeBenchmarkPerson(leftPersonId, leftFirstName, "SILVA"),
            new IbgeBenchmarkPerson(rightPersonId, rightFirstName, "SILVA"),
            Array.Empty<NominalSubstitution>());

    private static IbgeTypedNameFrequencySnapshot Snapshot() =>
        IbgeTypedNameFrequencyCatalog.Create(
            "TEST-IBGE-DF-CALIBRATION-1",
            IbgeGeographicScope.Brazil,
            geographicCode: null,
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 1000),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARINA", 500),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "JOSE", 900),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "JOAO", 800),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 2000),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANNA", 100),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIO", 600),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "BIA", 50),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 99999)
            });
}

using Jornada.Contracts;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticBirthDistanceAuditV9Tests
{
    private static readonly DateOnly Truth = new(1978, 3, 15);
    private static readonly DateOnly TestTruth = new(1980, 7, 12);

    [Test]
    public void Audit_reports_realized_hamming_and_year_swap_without_using_test()
    {
        var audit = SyntheticBirthDistanceAuditBuilderV9.Analyze(Fixture());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(audit.Version, Is.EqualTo(SyntheticBirthDistanceAuditBuilderV9.Version));
            Assert.That(audit.Status, Does.Contain("NOT_EMPIRICAL_ERROR_RATES_OR_FS_MU"));
            Assert.That(audit.Partition, Is.EqualTo("TRAIN"));
            Assert.That(audit.BasePeopleInTraining, Is.EqualTo(1));
            Assert.That(audit.ObservationsInTraining, Is.EqualTo(5));
            Assert.That(audit.MissingBirthObservations, Is.EqualTo(1));
            Assert.That(audit.ExactBirthObservations, Is.EqualTo(1));
            Assert.That(audit.DifferingBirthObservations, Is.EqualTo(3));
            Assert.That(audit.SameDayMonthDifferentYear, Is.EqualTo(2));
            Assert.That(audit.AdjacentYearSameDayMonth, Is.EqualTo(1));
            Assert.That(audit.LastTwoYearDigitsSwappedSameDayMonth, Is.EqualTo(1));
            Assert.That(audit.ConventionalDayOneOrFifteenFromDifferentTruth, Is.EqualTo(1));
            Assert.That(audit.DigitDistances[0], Is.EqualTo(1));
            Assert.That(audit.DigitDistances[1], Is.EqualTo(1));
            Assert.That(audit.DigitDistances[2], Is.EqualTo(2));
            Assert.That(audit.DigitDistances.Values.Sum(), Is.EqualTo(4));
            Assert.That(audit.SemanticStates[BirthDateSemanticEvidence.PartialComponentAgreement],
                Is.EqualTo(3));
            Assert.That(audit.ComponentAgreementMasks["011"], Is.EqualTo(2));
            Assert.That(audit.ComponentAgreementMasks["110"], Is.EqualTo(1));
            Assert.That(audit.ComponentAgreementMasks["111"], Is.EqualTo(1));
        }));

        var changedTest = Fixture();
        changedTest.Observations.Last().BirthDate = new DateOnly(1826, 1, 1);
        var independentlyAudited = SyntheticBirthDistanceAuditBuilderV9.Analyze(changedTest);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(independentlyAudited),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(audit)));
    }

    [Test]
    public void Hamming_metric_keeps_semantic_order_and_correct_digit_distance()
    {
        var yearSwap = new DateOnly(1987, 3, 15);
        var adjacent = new DateOnly(1979, 3, 15);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(BirthDateSemanticEvidence.DigitDistance(Truth, yearSwap), Is.EqualTo(2));
            Assert.That(BirthDateSemanticEvidence.DigitDistance(yearSwap, Truth), Is.EqualTo(2));
            Assert.That(BirthDateSemanticEvidence.DigitDistance(Truth, adjacent), Is.EqualTo(1));
            Assert.That(BirthDateSemanticEvidence.Classify(Truth, yearSwap),
                Is.EqualTo(BirthDateSemanticEvidence.PartialComponentAgreement));
            Assert.That(BirthDateSemanticEvidence.Classify(Truth, adjacent),
                Is.EqualTo(BirthDateSemanticEvidence.PartialComponentAgreement));
            Assert.That(BirthDateSemanticEvidence.Classify(Truth, Truth),
                Is.EqualTo(BirthDateSemanticEvidence.Exact));
        }));
    }

    [Test]
    public void Audit_rejects_invalid_truth_inventory_and_leaked_partitions()
    {
        var fixture = Fixture();
        var duplicate = fixture with { People = [fixture.People[0], fixture.People[0]] };
        var unknown = Fixture();
        unknown = unknown with { Observations = unknown.Observations.Select((o, i) =>
            i == 0 ? Observation("A0", "NOT_SELECTED", "TRAIN", Truth) : o).ToArray() };
        var partitionLeak = Fixture();
        partitionLeak = partitionLeak with { Observations = partitionLeak.Observations.Select((o, i) =>
            i == 0 ? Observation("A0", "A", "TEST", Truth) : o).ToArray() };
        var duplicateObservation = Fixture();
        duplicateObservation = duplicateObservation with
        {
            Observations = duplicateObservation.Observations.Select((o, i) =>
                i == 1 ? Observation("A0", "A", "TRAIN", new DateOnly(1987, 3, 15))
                    : o).ToArray()
        };

        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => SyntheticBirthDistanceAuditBuilderV9.Analyze(duplicate)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => SyntheticBirthDistanceAuditBuilderV9.Analyze(unknown)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => SyntheticBirthDistanceAuditBuilderV9.Analyze(partitionLeak)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => SyntheticBirthDistanceAuditBuilderV9.Analyze(duplicateObservation)),
                Throws.InvalidOperationException);
        }));
    }

    private static SyntheticCorpusGeneration Fixture()
    {
        var people = new[]
        {
            new SyntheticPerson
            {
                BasePersonId = "A", Partition = "TRAIN", Name = "Maria Silva",
                MotherName = "Ana Souza", BirthDate = Truth, Sex = "F", EvaluationWeight = 1
            },
            new SyntheticPerson
            {
                BasePersonId = "B", Partition = "TEST", Name = "Maria Silva",
                MotherName = "Ana Souza", BirthDate = TestTruth, Sex = "F", EvaluationWeight = 1
            }
        };
        return new SyntheticCorpusGeneration(
            people,
            [
                Observation("A0", "A", "TRAIN", Truth),
                Observation("A1", "A", "TRAIN", new DateOnly(1987, 3, 15)),
                Observation("A2", "A", "TRAIN", new DateOnly(1979, 3, 15)),
                Observation("A3", "A", "TRAIN", new DateOnly(1978, 3, 1)),
                Observation("A4", "A", "TRAIN", null),
                Observation("B0", "B", "TEST", TestTruth)
            ],
            new Dictionary<string, SyntheticEmpiricalM>(StringComparer.Ordinal),
            new SyntheticCorpusOptions(People: 2));
    }

    private static SyntheticObservation Observation(
        string id, string person, string partition, DateOnly? birthDate) => new()
    {
        ObservationId = id,
        BasePersonId = person,
        Partition = partition,
        Gestor = "G0",
        Name = "Maria Silva",
        MotherName = "Ana Souza",
        BirthDate = birthDate,
        Sex = "F",
        EvaluationWeight = 1,
        Corruptions = string.Empty
    };
}

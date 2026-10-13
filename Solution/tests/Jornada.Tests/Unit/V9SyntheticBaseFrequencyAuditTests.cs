using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SyntheticBaseFrequencyAuditTests
{
    private static V9SyntheticBaseFrequencyAudit.BasePerson[] Population() =>
    [
        new("T1", "TRAIN", "Maria Aparecida Silva", "Ana Souza"),
        new("T2", "TRAIN", "Maria Cecilia Silva", "Ana Souza"),
        new("T3", "TRAIN", "Maria Silva", null),
        new("T4", "TRAIN", "Joana Costa", "Ana Souza"),
        new("T5", "TRAIN", "Maria Silva", "Eva Maria"),
        new("V1", "VALIDATION", "Maria Silva", "Ana Souza"),
        new("E1", "TEST", "Maria Silva", "Ana Souza")
    ];

    [Test]
    public void Frequency_uses_one_true_person_per_train_base_and_keeps_full_and_joint_distinct()
    {
        var report = V9SyntheticBaseFrequencyAudit.Analyze(Population(), "FRAME_42");
        var person = report.Units.Where(x => x.Side == "PERSON").ToArray();
        var full = person.Single(x => x.Unit == NominalFrequencyUnitV9.FullName);
        var firstLast = person.Single(x => x.Unit == NominalFrequencyUnitV9.FirstLastJoint);
        var first = person.Single(x => x.Unit == NominalFrequencyUnitV9.FirstName);
        var mother = report.Units.Single(x => x.Side == "MOTHER" &&
            x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(
                V9SyntheticBaseFrequencyAudit.Status));
            Assert.That(report.Partition, Is.EqualTo("TRAIN"));
            Assert.That(report.CorpusBasePeople, Is.EqualTo(7));
            Assert.That(report.TrainingBasePeople, Is.EqualTo(5));
            Assert.That(report.Units.Count, Is.EqualTo(8));
            Assert.That(full.DistinctValues, Is.EqualTo(4));
            Assert.That(full.LargestValueCount, Is.EqualTo(2));
            Assert.That(firstLast.DistinctValues, Is.EqualTo(2));
            Assert.That(firstLast.LargestValueCount, Is.EqualTo(4));
            Assert.That(first.DistinctValues, Is.EqualTo(2));
            Assert.That(first.LargestValueCount, Is.EqualTo(4));
            Assert.That(mother.MissingTrainingBasePeople, Is.EqualTo(1));
            Assert.That(mother.PresentTrainingBasePeople, Is.EqualTo(4));
            Assert.That(mother.LargestValueCount, Is.EqualTo(3));
            Assert.That(first.ValuesObservedAtLeastFiveBases, Is.Zero);
            Assert.That(first.BasesCoveredByValuesObservedAtLeastFive, Is.Zero);
        });
    }

    [Test]
    public void Validation_and_test_names_never_change_train_frequency()
    {
        var baseline = V9SyntheticBaseFrequencyAudit.Analyze(Population(), "FRAME_42");
        var withOtherEvaluation = Population();
        withOtherEvaluation[5] = new("V1", "VALIDATION", "Joana Costa", null);
        withOtherEvaluation[6] = new("E1", "TEST", "Anacleto Nogueira", null);
        var changed = V9SyntheticBaseFrequencyAudit.Analyze(withOtherEvaluation, "FRAME_42");
        Assert.That(changed.Units, Is.EqualTo(baseline.Units));
    }

    [Test]
    public void Duplicate_base_or_wrong_split_fails_closed()
    {
        var original = Population();
        Assert.Multiple(() =>
        {
            Assert.That(() => V9SyntheticBaseFrequencyAudit.Analyze(
                original.Concat([original[0]]).ToArray(), "FRAME_42"),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => V9SyntheticBaseFrequencyAudit.Analyze(
                [new("X", "OTHER", "Ana Lima", "Eva Costa")], "FRAME_42"),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => V9SyntheticBaseFrequencyAudit.Analyze(
                [new("X", "TEST", "Ana Lima", "Eva Costa")], "FRAME_42"),
                Throws.TypeOf<InvalidDataException>());
        });
    }
}

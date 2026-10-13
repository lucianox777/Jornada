using Jornada.Contracts;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticJointSupportReportV9Tests
{
    private static SyntheticPerson Person(string id, string partition) => new()
    {
        BasePersonId = id, Partition = partition, Name = "Maria Silva",
        MotherName = "Ana Souza", BirthDate = new DateOnly(1990, 1, 1),
        Sex = "F", EvaluationWeight = 1.0
    };

    private static SyntheticObservation Observe(string id, string partition, string gestor,
        string name, string? mother) => new()
    {
        ObservationId = id + "-" + gestor,
        BasePersonId = id, Partition = partition, Gestor = gestor,
        Name = name, MotherName = mother, Sex = "F",
        BirthDate = new DateOnly(1990, 1, 1),
        EvaluationWeight = 1.0, Corruptions = ""
    };

    [Test]
    public void Counts_all_81_cells_in_each_truth_partition_and_does_not_cross_groups()
    {
        var people = new[]
        {
            Person("P001", "TRAIN"), Person("P002", "TRAIN"),
            Person("P003", "TEST")
        };
        var observations = new[]
        {
            Observe("P001", "TRAIN", "G1", "Maria Silva", "Ana Souza"),
            Observe("P001", "TRAIN", "G2", "MARIA SILVA", "ANA SOUZA"),
            Observe("P002", "TRAIN", "G1", "Maria Costa", "Ana Souza"),
            Observe("P002", "TRAIN", "G2", "Maria Costa", null),
            Observe("P003", "TEST", "G1", "Joao Dias", "Eva Lima"),
            Observe("P003", "TEST", "G2", "Joao Dias", "Eva Lima")
        };
        var result = SyntheticJointSupportReportV9.Build(people, observations, 42, "SHA256-INPUT");
        var train = result.Partitions.Single(p => p.Partition == "TRAIN");
        var test = result.Partitions.Single(p => p.Partition == "TEST");
        var validation = result.Partitions.Single(p => p.Partition == "VALIDATION");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.People, Is.EqualTo(3));
            Assert.That(result.Partitions, Has.Count.EqualTo(3));
            Assert.That(result.Status, Is.EqualTo(SyntheticJointSupportReportV9.Status));
            Assert.That(train.Cells, Has.Count.EqualTo(81));
            Assert.That(train.MatchedPairs, Is.EqualTo(2));
            Assert.That(train.UnmatchedProxyPairs, Is.EqualTo(1));
            Assert.That(test.MatchedPairs, Is.EqualTo(1));
            Assert.That(test.UnmatchedProxyPairs, Is.Zero);
            Assert.That(validation.MatchedPairs, Is.Zero);
            Assert.That(train.Cells.Single(c => c.Person == CompositeNominalLevelV9.ExactFull
                && c.Mother == CompositeNominalLevelV9.ExactFull).MatchedPairs, Is.EqualTo(1));
        }));
    }

    [Test]
    public void Cross_partition_or_mismatched_truth_is_rejected()
    {
        var people = new[] { Person("P001", "TRAIN") };
        var bad = new[] { Observe("P001", "TEST", "G1", "Maria", "Ana") };
        Assert.That((Action)(() => SyntheticJointSupportReportV9.Build(
            people, bad, 42, "INPUT")), Throws.InvalidOperationException);
    }

    [Test]
    public void No_personal_names_or_identifiers_are_serialized()
    {
        var people = new[] { Person("SECRET-P001", "TRAIN") };
        var observations = new[]
        {
            Observe("SECRET-P001", "TRAIN", "G1", "MARIA SENSIVEL", "ANA SENSIVEL"),
            Observe("SECRET-P001", "TRAIN", "G2", "MARIA SENSIVEL", "ANA SENSIVEL")
        };
        var evidence = SyntheticJointSupportReportV9.Build(people, observations, 42, "INPUT");
        var json = System.Text.Json.JsonSerializer.Serialize(evidence);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("SECRET-P001"));
            Assert.That(json, Does.Not.Contain("SENSIVEL"));
            Assert.That(json, Does.Not.Contain("MARIA"));
            Assert.That(json, Does.Not.Contain("ANA"));
        }));
    }
}

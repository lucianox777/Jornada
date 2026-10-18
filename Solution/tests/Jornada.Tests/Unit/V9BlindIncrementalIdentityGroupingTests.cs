using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9BlindIncrementalIdentityGroupingTests
{
    private static readonly DateOnly Birth = new(1980, 5, 6);

    private static V9PhysicalCalibrationObservation Row(
        int id, string gestor, string name = "ANA MARIA SILVA",
        string mother = "MARIA LIMA", string baseId = "TRUTH-A",
        string partition = "VALIDATION", DateOnly? birth = null) =>
        new(id, baseId, partition, gestor, name, mother, birth ?? Birth);

    private static V9BlindFsSqlEdge Edge(
        int a, int b, decimal forward = 8m, decimal reverse = 8m) =>
        new(a, b, forward, reverse);

    [Test]
    public void Strong_FS_pair_with_middle_name_variation_forms_blind_candidate()
    {
        var rows = new[]
        {
            Row(1, "G1"),
            Row(2, "G2", name: "ANA PAULA SILVA"),
            Row(3, "G3", name: "JOAO CARLOS SOUZA", baseId: "TRUTH-B")
        };
        var result = V9BlindIncrementalIdentityGrouping.Build(
            rows, [Edge(1, 2)], minimumPairPosterior: .99m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.IdentityByObservation[1],
                Is.EqualTo(result.IdentityByObservation[2]));
            Assert.That(result.IdentityByObservation[3],
                Is.Not.EqualTo(result.IdentityByObservation[1]));
            Assert.That(result.AcceptedTransitions, Is.EqualTo(1));
            Assert.That(result.OperationallyCertified, Is.False);
        }));
    }

    [Test]
    public void Chain_cannot_merge_without_all_cross_group_SQL_edges()
    {
        var rows = new[] { Row(1, "G1"), Row(2, "G2"), Row(3, "G3") };
        var result = V9BlindIncrementalIdentityGrouping.Build(
            rows, [Edge(1, 2, 9m, 9m), Edge(1, 3, 8m, 8m)]);
        Assert.That(result.IdentityByObservation[1],
            Is.EqualTo(result.IdentityByObservation[2]));
        Assert.That(result.IdentityByObservation[3],
            Is.Not.EqualTo(result.IdentityByObservation[1]));
        Assert.That(result.RejectedWeakOrIncompleteLinks, Is.GreaterThan(0));
    }

    [Test]
    public void Both_directions_must_pass_FS_and_all_observed_guards()
    {
        var rows = new[]
        {
            Row(1, "G1"), Row(2, "G2"),
            Row(3, "G3", mother: "OUTRA MAE")
        };
        var result = V9BlindIncrementalIdentityGrouping.Build(
            rows, [Edge(1, 2, 9m, 0m), Edge(1, 3)]);
        Assert.That(result.AcceptedTransitions, Is.Zero);
        Assert.That(result.RejectedWeakOrIncompleteLinks, Is.EqualTo(1));
        Assert.That(result.RejectedConsistencyGuards, Is.EqualTo(1));
    }

    [Test]
    public void Synthetic_truth_labels_never_change_membership()
    {
        var rows = new[] { Row(1, "G1"), Row(2, "G2", name: "ANA PAULA SILVA") };
        var edges = new[] { Edge(1, 2) };
        var first = V9BlindIncrementalIdentityGrouping.Build(rows, edges);
        var changedTruth = rows.Select(x => x with
        {
            BasePersonId = "ALTERED-TRUTH-" + x.ObservationId
        }).Reverse().ToArray();
        var second = V9BlindIncrementalIdentityGrouping.Build(
            changedTruth, edges.Reverse().ToArray());
        Assert.That(second.IdentityByObservation[1],
            Is.EqualTo(first.IdentityByObservation[1]));
        Assert.That(second.IdentityByObservation[2],
            Is.EqualTo(first.IdentityByObservation[2]));
    }

    [Test]
    public void Same_manager_and_missing_fields_never_join()
    {
        var rows = new[]
        {
            Row(1, "G1"), Row(2, "G2", mother: ""),
            Row(3, "G1")
        };
        var result = V9BlindIncrementalIdentityGrouping.Build(
            rows, [Edge(1, 2)]);
        Assert.That(result.AcceptedTransitions, Is.Zero);
        Assert.That(result.IdentityByObservation[1],
            Is.Not.EqualTo(result.IdentityByObservation[2]));
        Assert.That((Action)(() =>
            V9BlindIncrementalIdentityGrouping.Build(
                rows, [Edge(1, 3)])),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Complete_linkage_respects_predeclared_group_size_cap()
    {
        var rows = new[] { Row(1, "G1"), Row(2, "G2"), Row(3, "G3") };
        var result = V9BlindIncrementalIdentityGrouping.Build(
            rows, [Edge(1, 2, 9m, 9m), Edge(1, 3, 8m, 8m),
                Edge(2, 3, 7m, 7m)],
            maximumObservationsPerIdentity: 2);
        Assert.That(result.AcceptedTransitions, Is.EqualTo(1));
        Assert.That(result.RejectedSizeCap, Is.GreaterThan(0));
    }

    [Test]
    public void Invalid_test_duplicates_unknown_or_reversed_edges_fail_closed()
    {
        var rows = new[] { Row(1, "G1"), Row(2, "G2") };
        Assert.That((Action)(() =>
            V9BlindIncrementalIdentityGrouping.Build(
                [rows[0] with { Partition = "TEST" }, rows[1]],
                [Edge(1, 2)])), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() =>
            V9BlindIncrementalIdentityGrouping.Build(
                [rows[0], rows[0]], [Edge(1, 2)])),
            Throws.TypeOf<InvalidDataException>());
        foreach (var invalid in new[]
        {
            new[] { Edge(2, 1) },
            new[] { Edge(1, 4) },
            new[] { Edge(1, 2), Edge(1, 2) }
        })
            Assert.That((Action)(() =>
                V9BlindIncrementalIdentityGrouping.Build(rows, invalid)),
                Throws.TypeOf<InvalidDataException>());
    }
}

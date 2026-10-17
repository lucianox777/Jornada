using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PhysicalSqlAdaptiveCalibrationProbeTests
{
    private static readonly AdaptiveCalibrationPreviewBudget Budget = new(
        AdaptiveCalibrationPreviewBudget.CurrentVersion,
        new string('e', 64),
        MinimumGlobalAcceptedRecall: .95,
        MaximumObservedAcceptedFdrPoint: .01,
        MaximumBlockingFalseDegreeP99: 100,
        MinimumValidationIdentityGroups: 2);

    [Test]
    public void Eligible_M_Excludes_Same_Manager_And_Never_Crosses_Splits()
    {
        var rows = new[]
        {
            new V9PhysicalCalibrationObservation(1, "BASE-1", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(2, "BASE-1", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(3, "BASE-1", "VALIDATION", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(4, "BASE-2", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(5, "BASE-2", "TRAIN", "G2", null, null, null)
        };
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "TRAIN"), Is.EqualTo(1));
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "VALIDATION"), Is.Zero);
    }

    [Test]
    public void Eligible_M_Counts_Unordered_Three_Manager_Combinations_Exactly_Once()
    {
        var rows = new[]
        {
            new V9PhysicalCalibrationObservation(1, "PERSON-A", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(2, "PERSON-A", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(3, "PERSON-A", "TRAIN", "G3", null, null, null),
            new V9PhysicalCalibrationObservation(4, "PERSON-B", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(5, "PERSON-B", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(6, "PERSON-C", "VALIDATION", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(7, "PERSON-C", "VALIDATION", "G2", null, null, null)
        };
        // Three unordered combinations for PERSON-A plus one for PERSON-B.
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "TRAIN"), Is.EqualTo(4));
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "VALIDATION"), Is.EqualTo(1));
    }

    [Test]
    public void Rejects_test_partition_before_any_sql_command()
    {
        using var connection = new SqlConnection();
        var rows = BaseRows().Append(new V9PhysicalCalibrationObservation(
            99, "TEST-1", "TEST", "G1", "MARIA", "ANA", new DateOnly(1980, 1, 1))).ToArray();

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, rows, Passes(), Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Rejects_duplicate_observation_ids_before_any_sql_command()
    {
        using var connection = new SqlConnection();
        var rows = BaseRows().ToArray();
        rows[3] = rows[3] with { ObservationId = rows[0].ObservationId };

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, rows, Passes(), Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("ObservationId duplicado"));
    }

    [Test]
    public void Rejects_missing_validation_partition_before_any_sql_command()
    {
        using var connection = new SqlConnection();
        var rows = BaseRows().Select(x => x with { Partition = "TRAIN" }).ToArray();

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, rows, Passes(), Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("TRAIN e VALIDATION"));
    }

    [Test]
    public void Rejects_identity_leakage_between_train_and_validation_before_sql()
    {
        using var connection = new SqlConnection();
        var rows = BaseRows().ToArray();
        rows[2] = rows[2] with { BasePersonId = rows[0].BasePersonId };

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, rows, Passes(), Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Rejects_blocking_feature_outside_calibrator_catalog_before_sql()
    {
        using var connection = new SqlConnection();
        var bad = new[] { LinkageBlockingPass.Create("BAD", ["not_a_real_feature"]) };

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, BaseRows(), bad, Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Eligible_M_denominator_is_counted_before_blocking_and_across_managers_only()
    {
        var rows = new[]
        {
            new V9PhysicalCalibrationObservation(1, "BASE-A", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(2, "BASE-A", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(3, "BASE-A", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(4, "BASE-A", "TRAIN", "G3", null, null, null),
            new V9PhysicalCalibrationObservation(5, "BASE-B", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(6, "BASE-B", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(7, "BASE-B", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(8, "BASE-C", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(9, "BASE-C", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(10, "BASE-A", "VALIDATION", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(11, "BASE-A", "VALIDATION", "G2", null, null, null)
        };

        // BASE-A: G1(2)*G2(1) + G1(2)*G3(1) + G2(1)*G3(1) = 5.
        // BASE-B: G1(1)*G2(2) = 2. BASE-C: same-gestor only = 0.
        Assert.That(
            V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "TRAIN"),
            Is.EqualTo(7));
        Assert.That(
            V9PhysicalSqlAdaptiveCalibrationProbe.CountEligibleTruePairsAcrossManagers(rows, "VALIDATION"),
            Is.EqualTo(1));
        Assert.That(
            (Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
                .CountEligibleTruePairsAcrossManagers(rows, "TEST")),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Physical_sql_timing_contract_keeps_distinct_phase_measurements()
    {
        var timings = new V9PhysicalSqlMeasuredTimings(1, 2, 3, 4, 5);
        Assert.Multiple((TestDelegate)(() =>
        {
            Assert.That(timings.CreateTablesMilliseconds, Is.EqualTo(1));
            Assert.That(timings.BulkObservationsMilliseconds, Is.EqualTo(2));
            Assert.That(timings.BulkSignaturesMilliseconds, Is.EqualTo(3));
            Assert.That(timings.MaterializePairsMilliseconds, Is.EqualTo(4));
            Assert.That(timings.ReadPairsMilliseconds, Is.EqualTo(5));
        }));
    }

    [Test]
    public void Valid_preflight_reaches_open_connection_guard()
    {
        using var connection = new SqlConnection();

        Assert.That(
            (Func<Task>)(async () => await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                connection, BaseRows(), Passes(), Options(), Budget, new string('e', 64),
                CancellationToken.None)),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Conexão SQL precisa estar aberta"));
    }

    private static V9PhysicalCalibrationObservation[] BaseRows() =>
    [
        new(1, "TRAIN-A", "TRAIN", "G1", "MARIA SILVA", "ANA COSTA", new DateOnly(1980, 1, 1)),
        new(2, "TRAIN-B", "TRAIN", "G2", "MARIA SILVA", "ROSA COSTA", new DateOnly(1980, 1, 1)),
        new(3, "VAL-A", "VALIDATION", "G1", "JOAO SOUZA", "LUCIA SOUZA", new DateOnly(1982, 2, 2)),
        new(4, "VAL-B", "VALIDATION", "G2", "JOAO SOUZA", "TERESA SOUZA", new DateOnly(1982, 2, 2))
    ];

    private static LinkageBlockingPass[] Passes() =>
    [
        LinkageBlockingPass.Create("P_BIRTH", [
            BlockingFeatureNames.FullName,
            BlockingFeatureNames.BirthYear])
    ];

    private static V9FsAdaptiveOptionConfiguration[] Options() =>
    [
        new("T050", .5m, .50m)
    ];
}

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

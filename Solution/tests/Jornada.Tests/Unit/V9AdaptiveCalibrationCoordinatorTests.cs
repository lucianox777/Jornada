using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9AdaptiveCalibrationCoordinatorTests
{
    private static V9AdaptiveCalibrationPlan Plan()
    {
        var sha = new string('a', 64);
        return new(sha, new V9TrainBlockingDiscoveryOptions(),
            Enumerable.Range(0, 100).Select(i => new V9FsAdaptiveOptionConfiguration("OPT-" + i, .5m, .9m)).ToArray(),
            Enumerable.Range(1, 10).Select(i => i / 10m).ToArray(),
            new(AdaptiveCalibrationPreviewBudget.CurrentVersion, sha, .95, .01, 100, 2),
            new(.95, .01, 2), 100_000, 100_000_000,
            new(MaximumPrefixLength: 4, EndToEndRepetitions: 20,
                FsBlindGroupingOnly: true, MaximumBlindClusterSize: 16));
    }

    [Test]
    public void Known_control_is_retained_beyond_the_discovery_pool_and_counted_without_order_duplicates()
    {
        IReadOnlyList<LinkageBlockingPass> first = [LinkageBlockingPass.Create("A", ["name_full"])];
        IReadOnlyList<LinkageBlockingPass> control =
        [
            LinkageBlockingPass.Create("Z", ["name_phonetic_ptbr", "mother_name_last"]),
            LinkageBlockingPass.Create("B", ["name_first", "birth_year"]),
            LinkageBlockingPass.Create("C", ["name_last", "mother_name_phonetic_ptbr"])
        ];
        var combined = V9AdaptiveCalibrationCoordinator.CombineCandidateRuleSets([first], [control, control.Reverse().ToArray()]);
        Assert.That(combined, Has.Count.EqualTo(2));
        Assert.That(combined[1].Select(p => p.PassId), Is.EqualTo(new[] { "B", "C", "Z" }));
        Assert.That(V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(Plan(), combined.Count), Is.EqualTo(2000));
        Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(
            Plan() with { MaximumPhysicalRuleSets = 1 }, combined.Count)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Large_family_counts_every_ruleset_option_margin_and_previous_comparison()
    {
        var plan = Plan() with { PreviouslyComparedValidationConfigurations = 123 };
        Assert.That(V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(plan, 10_000),
            Is.EqualTo(10_000_123));
        Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(
                plan with { MaximumComparedConfigurations = 10_000_122 }, 10_000)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("nenhuma configuração foi truncada"));
        Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(
                plan with { Execution = plan.Execution with { TotalComparedConfigurations = 1000 } }, 10_000)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("orçamento"));
    }

    [Test]
    public void More_compute_cannot_relax_risk_or_substitute_projection_or_zero_margin()
    {
        var plan = Plan();
        var invalid = new[]
        {
            plan with { SelectionPolicy = plan.SelectionPolicy with { MaximumAcceptedFdrUpper = .02 } },
            plan with { SelectionPolicy = plan.SelectionPolicy with { MinimumGlobalRecall = .9 } },
            plan with { Execution = plan.Execution with { MaximumPrefixLength = 3 } },
            plan with { Execution = plan.Execution with { FsBlindGroupingOnly = false } },
            plan with { Execution = plan.Execution with { EndToEndRepetitions = 19 } },
            plan with { Execution = plan.Execution with { MaximumBlindClusterSize = 1 } },
            plan with { ConflictMargins = [0m] }
        };
        foreach (var candidate in invalid)
            Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.ValidatePlan(candidate)),
                Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Test_labels_and_identity_leakage_fail_before_discovery_or_closed_SQL()
    {
        using var sql = new SqlConnection();
        var rows = new[]
        {
            new V9PhysicalCalibrationObservation(1, "A", "TRAIN", "G1", "MARIA", null, null),
            new V9PhysicalCalibrationObservation(2, "A", "TRAIN", "G2", "MARIA", null, null),
            new V9PhysicalCalibrationObservation(3, "B", "VALIDATION", "G1", "JOAO", null, null),
            new V9PhysicalCalibrationObservation(4, "B", "VALIDATION", "G2", "JOAO", null, null)
        };
        var exception = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9AdaptiveCalibrationCoordinator.RunAsync(sql,
                rows.Append(rows[3] with { ObservationId = 5, Partition = "TEST" }).ToArray(),
                Plan(), CancellationToken.None)));
        Assert.That(exception!.Message, Does.Contain("sem TEST"));
        exception = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9AdaptiveCalibrationCoordinator.RunAsync(sql,
                rows.Select(x => x with { BasePersonId = "SHARED" }).ToArray(), Plan(), CancellationToken.None)));
        Assert.That(exception!.Message, Does.Contain("vazamento de identidade"));
    }

    [Test]
    public void Prefix_schema_is_opt_in_and_reaches_physical_SQL_boundary()
    {
        using var sql = new SqlConnection();
        var rows = Enumerable.Range(1, 4).Select(i => new V9PhysicalCalibrationObservation(
            i, i <= 2 ? "A" : "B", i <= 2 ? "TRAIN" : "VALIDATION",
            i % 2 == 0 ? "G2" : "G1", "MARIA", null, null)).ToArray();
        var plan = Plan();
        var passes = new[] { LinkageBlockingPass.Create("PREFIX", [V9CalibrationBlockingProjection.NamePrefixFeature(3)]) };
        var legacy = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(sql, rows, passes,
                plan.ModelOptions.Take(1).ToArray(), plan.Budget, plan.StudyFingerprintSha256, CancellationToken.None)));
        Assert.That(legacy!.Message, Does.Contain("fora do catálogo"));
        var optIn = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(sql, rows, passes,
                plan.ModelOptions.Take(1).ToArray(), plan.Budget, plan.StudyFingerprintSha256, CancellationToken.None,
                evaluateFinalResolve: true, includeFsBlindGrouping: true, adaptiveExecution: plan.Execution)));
        Assert.That(optIn!.Message, Does.Contain("Conexão SQL precisa estar aberta"));
    }

    [Test]
    public void Enlarged_resource_cap_keeps_preflight_conservative_and_local_rejections_identifiable()
    {
        var rows = Enumerable.Range(1, 4000).Select(i => new V9PhysicalCalibrationObservation(
            i, "BASE-" + i, "TRAIN", i <= 2000 ? "G1" : "G2", "MARIA", null, null)).ToArray();
        var signatures = rows.Select(x => (x.ObservationId, x.Partition, PassId: "P", Signature: "SAME")).ToArray();
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountCrossManagerPairIncidences(signatures, rows),
            Is.EqualTo(1_000_001));
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountCrossManagerPairIncidences(signatures, rows, 2_000_000),
            Is.EqualTo(2_000_001));
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountCrossManagerPairIncidences(signatures, rows, 5_000_000),
            Is.EqualTo(4_000_000));

        using var sql = new SqlConnection();
        var frame = rows.Take(2).Select((x, i) => x with { Gestor = "G" + i }).Concat(
            rows.Skip(2).Take(2).Select((x, i) => x with { Partition = "VALIDATION", Gestor = "G" + i })).ToArray();
        var plan = Plan();
        var exception = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(sql, frame,
                [LinkageBlockingPass.Create("P", [V9CalibrationBlockingProjection.NamePrefixFeature(1)])],
                plan.ModelOptions.Take(1).ToArray(), plan.Budget, plan.StudyFingerprintSha256, CancellationToken.None,
                evaluateFinalResolve: true, includeFsBlindGrouping: true,
                adaptiveExecution: plan.Execution with { MaximumCandidatePairIncidences = 1 })));
        Assert.That(exception!.Message, Does.Contain("incidências excedem 1"));
        Assert.That(exception.Data["V9_DECLARED_CANDIDATE_REJECTION"], Is.EqualTo(true));
    }
}

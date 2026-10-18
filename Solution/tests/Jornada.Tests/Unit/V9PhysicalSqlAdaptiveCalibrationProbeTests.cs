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
    public void Physical_frame_global_recall_includes_M_missed_by_blocking()
    {
        var rows = new[]
        {
            CoverageOption("T050", mSql: 6, sqlU: 20, tp: 5, fp: 1)
        };
        var result = V9PhysicalSqlAdaptiveCalibrationProbe.CalculateValidationGlobalCoverage(
            eligibleTruePairs: 10, recoveredTruePairs: 6, recoveredFalsePairs: 20,
            options: rows, minimumGlobalRecall: .95);
        Assert.That(result, Has.Count.EqualTo(1));
        var metric = result[0];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(metric.EligibleTruePairsBeforeBlocking, Is.EqualTo(10));
            Assert.That(metric.BlockingTruePairRecall, Is.EqualTo(.6d));
            Assert.That(metric.ConditionalAcceptedPairRecall, Is.EqualTo(5d / 6d).Within(1e-12));
            Assert.That(metric.GlobalAcceptedPairRecall, Is.EqualTo(.5d).Within(1e-12));
            Assert.That(metric.ThresholdAcceptedPairFdr, Is.EqualTo(1d / 6d).Within(1e-12));
            Assert.That(metric.MeetsGlobalRecallBudget, Is.False);
            Assert.That(metric.FinalRankResolveEvaluated, Is.False);
        }));
    }

    [Test]
    public void Physical_frame_rejects_wrong_M_SQL_or_impossible_FS_accepted_counts()
    {
        var valid = CoverageOption("T050", mSql: 6, sqlU: 20, tp: 5, fp: 1);
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .CalculateValidationGlobalCoverage(5, 6, 20, [valid], .95)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .CalculateValidationGlobalCoverage(10, 5, 20, [valid], .95)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .CalculateValidationGlobalCoverage(10, 6, 19, [valid], .95)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .CalculateValidationGlobalCoverage(10, 6, 20,
                [CoverageOption("T050", 6, 20, 7, 1)], .95)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .CalculateValidationGlobalCoverage(10, 6, 20,
                [CoverageOption("T050", 6, 20, 5, 21)], .95)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void No_threshold_accepted_pairs_has_undefined_FDR_not_zero_risk()
    {
        var result = V9PhysicalSqlAdaptiveCalibrationProbe.CalculateValidationGlobalCoverage(
            10, 6, 20, [CoverageOption("T0", 6, 20, 0, 0)], .95);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result[0].GlobalAcceptedPairRecall, Is.Zero);
            Assert.That(result[0].ThresholdAcceptedPairFdr, Is.Null);
            Assert.That(result[0].MeetsGlobalRecallBudget, Is.False);
        }));
    }

    private static AdaptiveCalibrationOptionResult CoverageOption(
        string id, long mSql, long sqlU, long tp, long fp)
    {
        var holdout = new AdaptiveCalibrationHoldoutMetrics(
            TruePairsWithoutCpf: mSql,
            TrueAcceptedLinks: tp,
            FalseAcceptedLinks: fp,
            Abstentions: Math.Max(0, mSql - tp),
            BlockingFalseCandidatePairs: sqlU,
            BlockingFalseDegreeP99: 2d,
            IndependentlyLabeledIdentityGroups: 20);
        var accepted = tp + fp;
        return new AdaptiveCalibrationOptionResult(
            "FELLEGI_SUNTER_V9", "V9_TEST", id, new string('a', 64),
            holdout, mSql == 0 ? 0 : (double)tp / mSql,
            accepted == 0 ? null : (double)fp / accepted,
            MeetsDescriptiveBudgetOnly: false);
    }

    [Test]
    public void Physical_sql_does_not_suggest_a_model_with_only_conditional_recall()
    {
        // 98% conditional FS recall would pass a nominal 95% screen,
        // but recovery of just 50% M in SQL means global recall is 49%.
        var option = CoverageOption("T050", mSql: 100, sqlU: 200, tp: 98, fp: 0);
        var report = CoverageReport([option], "T050");
        var physical = V9PhysicalSqlAdaptiveCalibrationProbe.CalculateValidationGlobalCoverage(
            eligibleTruePairs: 200, recoveredTruePairs: 100, recoveredFalsePairs: 200,
            options: [option], minimumGlobalRecall: .95);
        Assert.That(physical[0].ConditionalAcceptedPairRecall, Is.EqualTo(.98d));
        Assert.That(physical[0].GlobalAcceptedPairRecall, Is.EqualTo(.49d));
        var reconciled = V9PhysicalSqlAdaptiveCalibrationProbe
            .EnforcePhysicalGlobalRecallForSuggestion(report, physical);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(reconciled.Status,
                Is.EqualTo("NO_MODEL_MEETS_PHYSICAL_GLOBAL_RECALL_BUDGET"));
            Assert.That(reconciled.SuggestedModelFamily, Is.Null);
            Assert.That(reconciled.SuggestedOptionId, Is.Null);
            Assert.That(reconciled.OperationalModelCreated, Is.False);
            Assert.That(reconciled.AutomaticRecalibrationOrActivationAllowed, Is.False);
        }));
    }

    [Test]
    public void Physical_sql_keeps_descriptive_suggestion_only_when_global_recall_passes()
    {
        var option = CoverageOption("T050", mSql: 100, sqlU: 200, tp: 98, fp: 0);
        var report = CoverageReport([option], "T050");
        var physical = V9PhysicalSqlAdaptiveCalibrationProbe.CalculateValidationGlobalCoverage(
            eligibleTruePairs: 102, recoveredTruePairs: 100, recoveredFalsePairs: 200,
            options: [option], minimumGlobalRecall: .95);
        var reconciled = V9PhysicalSqlAdaptiveCalibrationProbe
            .EnforcePhysicalGlobalRecallForSuggestion(report, physical);
        Assert.That(physical[0].GlobalAcceptedPairRecall, Is.GreaterThan(.95d));
        Assert.That(reconciled, Is.SameAs(report));
        Assert.That(reconciled.OperationalModelCreated, Is.False);
    }

    [Test]
    public void Physical_sql_suggestion_requires_matching_unambiguous_option_fingerprints()
    {
        var option = CoverageOption("T050", mSql: 100, sqlU: 200, tp: 98, fp: 0);
        var report = CoverageReport([option], "T050");
        var physical = V9PhysicalSqlAdaptiveCalibrationProbe.CalculateValidationGlobalCoverage(
            102, 100, 200, [option], .95);
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .EnforcePhysicalGlobalRecallForSuggestion(report,
                [physical[0] with { OptionId = "FORGED" }])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .EnforcePhysicalGlobalRecallForSuggestion(
                report with { SuggestedModelFamily = null }, physical)),
            Throws.TypeOf<InvalidDataException>());
        var noSuggestion = report with
        {
            SuggestedModelFamily = null,
            SuggestedOptionId = null
        };
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe
            .EnforcePhysicalGlobalRecallForSuggestion(noSuggestion, physical),
            Is.SameAs(noSuggestion));
    }

    private static AdaptiveCalibrationStudyReport CoverageReport(
        IReadOnlyList<AdaptiveCalibrationOptionResult> options,
        string suggestedOption) => new(
            AdaptiveCalibrationModelSearch.Version, new string('b', 64),
            "SUGGESTION_ONLY_UNCERTIFIED_GROUP_FDR_AND_PHYSICAL_SQL",
            options, "FELLEGI_SUNTER_V9", suggestedOption,
            TestUsed: false, ConfidenceBoundForFdrMeasured: false,
            OperationalModelCreated: false,
            AutomaticRecalibrationOrActivationAllowed: false);

    [Test]
    public void Physical_sql_cohort_counts_identities_with_no_candidate_pairs()
    {
        // A single true pair contributes one base identity, not two.
        // Another two bases appear in a false pair; two input identities
        // never appear in SQL and must remain in the cohort denominator.
        var pairs = new[]
        {
            CandidateForCohort("VALIDATION", "BASE-A", "BASE-A", true),
            CandidateForCohort("VALIDATION", "BASE-B", "BASE-C", false)
        };
        var result = V9PhysicalSqlAdaptiveCalibrationProbe.SummarizeIdentityCohort(
            "VALIDATION", ["BASE-A", "BASE-B", "BASE-C", "BASE-D", "BASE-E"], pairs);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.BaseIdentitiesBeforeBlocking, Is.EqualTo(5));
            Assert.That(result.BaseIdentitiesRepresentedBySql, Is.EqualTo(3));
            Assert.That(result.BaseIdentitiesWithoutSqlCandidates, Is.EqualTo(2));
            Assert.That(result.BaseIdentityCandidateCoverage, Is.EqualTo(.6d));
        }));
    }

    [Test]
    public void Empty_sql_frame_does_not_erase_input_identity_groups()
    {
        var result = V9PhysicalSqlAdaptiveCalibrationProbe.SummarizeIdentityCohort(
            "TRAIN", ["BASE-A", "BASE-B"], Array.Empty<V9FsAdaptiveCandidatePair>());
        Assert.That(result.BaseIdentitiesBeforeBlocking, Is.EqualTo(2));
        Assert.That(result.BaseIdentitiesRepresentedBySql, Is.Zero);
        Assert.That(result.BaseIdentitiesWithoutSqlCandidates, Is.EqualTo(2));
        Assert.That(result.BaseIdentityCandidateCoverage, Is.Zero);
    }

    [Test]
    public void Physical_sql_cohort_rejects_unseen_groups_split_leakage_and_duplicate_input()
    {
        var valid = CandidateForCohort("TRAIN", "BASE-A", "BASE-B", false);
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .SummarizeIdentityCohort("TRAIN", ["BASE-A", "BASE-A"], [valid])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .SummarizeIdentityCohort("TRAIN", ["BASE-A", "BASE-C"], [valid])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .SummarizeIdentityCohort("VALIDATION", ["BASE-A", "BASE-B"], [valid])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlAdaptiveCalibrationProbe
            .SummarizeIdentityCohort("TEST", ["BASE-A", "BASE-B"], [valid])),
            Throws.TypeOf<ArgumentException>());
    }

    private static V9FsAdaptiveCandidatePair CandidateForCohort(
        string partition, string leftGroup, string rightGroup, bool truePair) =>
        new(partition, leftGroup, rightGroup, truePair,
            null, null, null, null, null, null, 1);

    [Test]
    public void Repeated_sql_materialization_requires_explicit_20_to_100_runs_before_any_db_access()
    {
        using var connection = new SqlConnection();
        foreach (var invalid in new[] { -1, 1, 19, 101, int.MaxValue })
        {
            Assert.That(
                (Func<Task>)(async () =>
                    await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
                        connection, BaseRows(), Passes(), Options(), Budget,
                        new string('e', 64), CancellationToken.None,
                        repeatedSqlMaterializationRuns: invalid)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    [Test]
    public void Physical_materialization_quantile_contract_remains_empty_unless_opted_in()
    {
        // No repeated runs may be misrepresented as P95/P99 on a
        // single SQL phase. The optional result field defaults to null.
        var diagnostic = new V9PhysicalCalibrationRunResult(
            "TEST", new string('a', 64), 100, 25, 10, 15, 2, 2,
            CoverageReport([CoverageOption("X", 6, 20, 5, 1)], "X"));
        Assert.That(diagnostic.RepeatedSqlMaterializationQuantiles, Is.Null);
        Assert.That(V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(
                Enumerable.Range(1, 20).Select(x => (double)x))
            .P99Milliseconds, Is.EqualTo(20d));
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
    public void Physical_preflight_excludes_same_manager_and_counts_duplicate_pass_hits()
    {
        var rows = new[]
        {
            new V9PhysicalCalibrationObservation(1, "A", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(2, "B", "TRAIN", "G1", null, null, null),
            new V9PhysicalCalibrationObservation(3, "C", "TRAIN", "G2", null, null, null),
            new V9PhysicalCalibrationObservation(4, "D", "VALIDATION", "G3", null, null, null)
        };
        var signatures = new (int ObservationId, string Partition, string PassId, string Signature)[]
        {
            (1, "TRAIN", "P1", "X"), (2, "TRAIN", "P1", "X"),
            (3, "TRAIN", "P1", "X"), (1, "TRAIN", "P2", "X"),
            (3, "TRAIN", "P2", "X"), (4, "VALIDATION", "P1", "X")
        };
        // P1: 2 cross-manager hits (not 3); P2: 1 hit; split is isolated.
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountCrossManagerPairIncidences(
            signatures, rows), Is.EqualTo(3));
        Assert.That(V9PhysicalSqlAdaptiveCalibrationProbe.CountCrossManagerPairIncidences(
            signatures.Where(x => x.ObservationId != 3).ToArray(), rows), Is.Zero);
    }

    [Test]
    public void Physical_sql_timing_contract_keeps_distinct_phase_measurements()
    {
        var timings = new V9PhysicalSqlMeasuredTimings(1, 2, 3, 4, 5);
        Assert.Multiple((Action)(() =>
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

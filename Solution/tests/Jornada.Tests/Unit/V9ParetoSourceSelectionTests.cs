using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ParetoSourceSelectionTests
{
    private static T? Throws<T>(Action action) where T : Exception => Assert.Throws<T>(action);
    private static void Multiple(Action action) => Assert.Multiple(action);
    private static readonly AdaptiveCalibrationPreviewBudget Budget = new(
        AdaptiveCalibrationPreviewBudget.CurrentVersion, new string('a', 64), 0, .01, 100, 2);
    private static readonly V9BootstrapPareto2D.SelectionPolicy Policy = new(0, .01, 2);
    private static readonly V9IdentityClusterFdr.ClusterAnyErrorCpRatioBound Bound =
        V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            Enumerable.Range(0, 10000).Select(i => new V9IdentityClusterFdr.IdentityGroup(
                $"identity-{i}", i < 9600 ? 2 : 0, 0)), 2, 100);
    private static V9PhysicalSourceResolveMetric Row(string id = "one") => new(
        id, .99m, .5m, 1m, "BLIND_FS_COMPLETE_LINK_CANDIDATES", 20000, 20000, 20000,
        19200, 0, 0, 800, 1, .96, 0, .02, Bound.IndependentGroups,
        new Dictionary<string, long> { ["ACCEPTED"] = 19200, ["UNRESOLVED"] = 800 },
        false, false, 0, Bound.UpperFdrConditional, Bound.UpperProbabilityOfAnyGroupErrorClopperPearson,
        Bound.ComparedConfigurations, true);
    private static Dictionary<string, double> Fanout(IEnumerable<V9PhysicalSourceResolveMetric> rows) =>
        rows.Select(x => x.OptionId).Distinct().ToDictionary(x => x, _ => 10d);
    private static V9ParetoPhysicalEvidence Evidence(V9PhysicalSourceResolveMetric row,
        IReadOnlyList<V9PhysicalSourceResolveMetric> all) => new(row.OptionId, row.CandidateGrouping, row.ConflictMargin,
        new string('b', 64), new string('c', 64), new string('d', 64),
        V9PostResolveAdaptiveSelector.EvaluateValidation("VALIDATION", false, Budget, all, Fanout(all)).EvidenceFingerprintSha256,
        new string('e', 64), new string('f', 64), 20, true);
    private static V9ParetoSourceSelectionReport Evaluate(IReadOnlyList<V9PhysicalSourceResolveMetric> rows,
        IReadOnlyList<V9ParetoPhysicalEvidence> evidence) => V9ParetoSourceSelection.EvaluateValidation(
            "VALIDATION", false, Budget, rows, Fanout(rows), evidence, Policy);

    [Test]
    public void Integrates_actual_source_counts_with_digest_bound_physical_evidence()
    {
        var row = Row(); var result = Evaluate([row], [Evidence(row, [row])]);
        Multiple(() =>
        {
            Assert.That(result.Pareto.InitialSuggestedOptionId,
                Is.EqualTo(V9ParetoSourceSelection.ConfigurationId(row.OptionId, row.CandidateGrouping, row.ConflictMargin)));
            Assert.That(result.Pareto.Table[0].Option.TrueAccepted, Is.EqualTo(19200));
            Assert.That(result.Pareto.Table[0].Option.GlobalEligibleTruth, Is.EqualTo(20000));
            Assert.That(result.Pareto.Table[0].Option.IdentityClusterFdrUpper, Is.EqualTo(Bound.UpperFdrConditional));
            Assert.That(result.SourceReport.AutomaticActivationAllowed, Is.False);
            Assert.That(result.TestUsed, Is.False);
        });
    }

    [Test]
    public void Missing_latency_or_boolean_only_cluster_attestation_never_suggests()
    {
        var row = Row() with { ConditionalClusterCpFdrUpper = null };
        var result = Evaluate([row], [Evidence(row, [row]) with { PhysicalEndToEndP95Milliseconds = null }]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("EVIDENCIA_ESTATISTICA_INSUFICIENTE"));
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("P95_FISICO_NAO_ATESTADO"));
    }

    [TestCase("RAW_SQL_OBSERVATION_CANDIDATES", 1)]
    [TestCase("BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING", 1)]
    [TestCase("BLIND_FS_COMPLETE_LINK_CANDIDATES", 0)]
    [TestCase("ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL", 1)]
    public void Rejects_nonoperational_groupings_and_zero_margin(string grouping, int margin)
    {
        var row = Row() with { CandidateGrouping = grouping, ConflictMargin = margin,
            SyntheticGoldOracleGrouping = grouping.StartsWith("ORACLE", StringComparison.Ordinal) };
        var result = Evaluate([row], [Evidence(row, [row])]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].Eligible, Is.False);
    }

    [Test]
    public void Rejects_physical_evidence_from_another_metric_report()
    {
        var row = Row(); var evidence = Evidence(row, [row]) with { SourceEvidenceFingerprintSha256 = new string('0', 64) };
        var result = Evaluate([row], [evidence]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons,
            Does.Contain("EVIDENCIA_FISICA_NAO_VINCULADA_A_METRICAS"));
    }

    [Test]
    public void Rejects_test_and_incomparable_cohorts()
    {
        var rows = new[] { Row("one"), Row("two") };
        Throws<InvalidDataException>(() => V9ParetoSourceSelection.EvaluateValidation(
            "TEST", true, Budget, rows, Fanout(rows), [], Policy));
        Throws<InvalidDataException>(() => Evaluate(rows,
            [Evidence(rows[0], rows), Evidence(rows[1], rows) with { ValidationCohortFingerprintSha256 = new string('0', 64) }]));
        Throws<InvalidDataException>(() => Evaluate([rows[0], rows[1] with { PreBlockingBaseIdentities = 49 }], []));
    }

    [Test]
    public void Missing_physical_evidence_preserves_the_option_in_rejected_table()
    {
        var result = Evaluate([Row()], []);
        Assert.That(result.Pareto.Table, Has.Count.EqualTo(1));
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("EVIDENCIA_FISICA_AUSENTE"));
    }

    [Test]
    public void Cannot_weaken_the_original_source_budget_with_a_separate_policy()
    {
        var row = Row();
        var strict = Policy with { MinimumGlobalRecall = .9, MinimumIndependentIdentityGroups = 20 };
        foreach (var policy in new[] { strict with { MaximumAcceptedFdrUpper = .02 },
                     strict with { MinimumGlobalRecall = 0 }, strict with { MinimumIndependentIdentityGroups = 2 } })
            Throws<InvalidDataException>(() => V9ParetoSourceSelection.EvaluateValidation(
                "VALIDATION", false, Budget with { MinimumGlobalAcceptedRecall = .9, MinimumValidationIdentityGroups = 20 },
                [row], Fanout([row]), [Evidence(row, [row])], policy));
    }

    [Test]
    public void Rejects_reused_configuration_hash_for_different_decision_margins()
    {
        var rows = new[] { Row(), Row() with { ConflictMargin = 2 } };
        var result = Evaluate(rows, rows.Select(x => Evidence(x, rows)).ToArray());
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table.All(x => x.ExclusionReasons.Contains(
            "FINGERPRINT_REUTILIZADO_COM_PARAMETROS_DIVERGENTES", StringComparer.Ordinal)), Is.True);
    }

    [Test]
    public void Independently_unvalidated_clusters_do_not_become_eligible_with_a_numeric_bound()
    {
        var row = Row() with { ClusterIidIndependentlyValidated = false };
        var result = Evaluate([row], [Evidence(row, [row])]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("EVIDENCIA_ESTATISTICA_INSUFICIENTE"));
    }

    [Test]
    public void Duplicate_or_unmeasured_physical_evidence_is_rejected()
    {
        var row = Row(); var evidence = Evidence(row, [row]);
        Throws<InvalidDataException>(() => Evaluate([row], [evidence, evidence]));
        Throws<InvalidDataException>(() => Evaluate([row], [evidence with { ConflictMargin = 2 }]));
    }

    [Test]
    public void Shared_training_validation_corpus_is_rejected()
    {
        var row = Row(); var evidence = Evidence(row, [row]);
        var result = Evaluate([row], [evidence with { TrainingCorpusFingerprintSha256 = evidence.ValidationCohortFingerprintSha256 }]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("TRAIN_VALIDATION_NAO_INDEPENDENTES"));
    }

    [TestCase(null)]
    [TestCase(0d)]
    [TestCase(.5d)]
    public void Missing_or_inconsistent_cp_component_cannot_support_eligibility(double? cp)
    {
        var row = Row() with { ClopperPearsonUpperAnyFpByIdentity = cp };
        var result = Evaluate([row], [Evidence(row, [row])]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("COMPONENTE_CP_AUSENTE_OU_INCONSISTENTE"));
    }

    [Test]
    public void More_independent_groups_than_source_observations_is_rejected()
    {
        var row = Row() with { PreBlockingBaseIdentities = 20001 };
        var result = Evaluate([row], [Evidence(row, [row])]);
        Assert.That(result.Pareto.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Pareto.Table[0].ExclusionReasons, Does.Contain("SUPORTE_INDEPENDENTE_INCONSISTENTE"));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1d)]
    [TestCase(1.1d)]
    public void Invalid_cp_probability_is_rejected_by_existing_source_reconciliation(double cp)
    {
        var row = Row() with { ClopperPearsonUpperAnyFpByIdentity = cp };
        Throws<InvalidDataException>(() => Evaluate([row], []));
    }
}

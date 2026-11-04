using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9BootstrapPareto2DTests
{
    private static T? Throws<T>(Action action) where T : Exception => Assert.Throws<T>(action);
    private static void Multiple(Action action) => Assert.Multiple(action);
    private static readonly V9BootstrapPareto2D.SelectionPolicy Policy = new(0, .1, 20);
    private static V9BootstrapPareto2D.CandidateOption Option(string id, long tp, long fp = 0,
        char hash = 'a', double p95 = 10) => new(id, new string(hash, 64), tp / 100d,
            tp + fp == 0 ? null : fp / (double)(tp + fp), p95, tp, fp, 100, 50,
            true, true, .1, 10);

    [Test]
    public void Keeps_equivalent_points_and_records_all_dominators_without_using_latency_as_axis()
    {
        var result = V9BootstrapPareto2D.Evaluate([
            Option("best", 95, hash: 'b', p95: 60000), Option("equivalent", 95, hash: 'a'),
            Option("dominated", 90, hash: 'c')], Policy);
        Multiple(() =>
        {
            Assert.That(result.InitialSuggestedOptionId, Is.EqualTo("equivalent"));
            Assert.That(result.Table.Where(x => x.OnFrontier).Select(x => x.Option.OptionId),
                Is.EquivalentTo(new[] { "best", "equivalent" }));
            var dominated = result.Table.Single(x => x.Option.OptionId == "dominated");
            Assert.That(dominated.DominatedByOptionIds, Is.EquivalentTo(new[] { "best", "equivalent" }));
            Assert.That(dominated.Eligible, Is.True);
            Assert.That(result.Table.Single(x => x.Option.OptionId == "best").EquivalentOptionIds,
                Is.EqualTo(new[] { "equivalent" }));
        });
    }

    [Test]
    public void Chooses_normalized_ideal_distance_and_is_independent_of_input_order()
    {
        var options = new[] { Option("safe", 80), Option("balance", 90, 1, 'b'), Option("recall", 100, 4, 'c') };
        var forward = V9BootstrapPareto2D.Evaluate(options, Policy);
        var reverse = V9BootstrapPareto2D.Evaluate(options.Reverse(), Policy);
        Assert.That(forward.InitialSuggestedOptionId, Is.EqualTo("balance"));
        Assert.That(reverse.InitialSuggestedOptionId, Is.EqualTo(forward.InitialSuggestedOptionId));
        Assert.That(forward.Table.Single(x => x.Option.OptionId == "balance").IdealSquaredDistance,
            Is.LessThan(1));
    }

    [Test]
    public void Exploratory_Pareto_selects_mathematically_without_P95_or_cluster_risk_certificate()
    {
        var options = new[] { Option("safe", 80), Option("balance", 90, 1, 'b'),
            Option("recall", 100, 4, 'c'), Option("dominated", 70, 4, 'd') }
            .Select(x => x with
            {
                PhysicalP95Milliseconds = null, PhysicalEndToEndAttested = false,
                IdentityClusterUncertaintyAttested = false, IdentityClusterFdrUpper = null
            }).ToArray();
        var strictPolicy = Policy with { MinimumGlobalRecall = .99, MaximumAcceptedFdrUpper = .001 };
        var first = V9BootstrapPareto2D.Evaluate(options, strictPolicy);
        var reversed = V9BootstrapPareto2D.Evaluate(options.Reverse(), strictPolicy);
        Multiple(() =>
        {
            Assert.That(first.Version, Is.EqualTo("V9_BOOTSTRAP_PARETO_2D_IDEAL_DISTANCE_V3"),
                "A nova fronteira matemática altera o contrato serializado; versões V2 históricas não são reescritas.");
            Assert.That(first.InitialSuggestedOptionId, Is.Null);
            Assert.That(first.Status, Is.EqualTo("NO_ELIGIBLE_CONFIGURATION"));
            Assert.That(first.Table.All(x => !x.Eligible && !x.OnFrontier), Is.True);
            Assert.That(first.ExploratoryFrontierOptionIds,
                Is.EquivalentTo(new[] { "safe", "balance", "recall" }));
            Assert.That(first.ExploratorySuggestedOptionId, Is.EqualTo("balance"));
            Assert.That(first.ExploratoryStatus, Is.EqualTo("EXPLORATORY_ONLY_NOT_CERTIFIED"));
            Assert.That(reversed.ExploratorySuggestedOptionId,
                Is.EqualTo(first.ExploratorySuggestedOptionId));
        });
    }

    [Test]
    public void Exploratory_Pareto_does_not_reintroduce_invalid_or_oracle_candidates()
    {
        var candidates = new[] { Option("inconsistent", 90) with { GlobalRecall = .4 },
            Option("oracle", 100) with { EvidenceRejectionReasons = ["ORACLE_NAO_OPERACIONAL"] },
            Option("physical", 80) with { PhysicalEndToEndAttested = false,
                IdentityClusterUncertaintyAttested = false } };
        var result = V9BootstrapPareto2D.Evaluate(candidates, Policy);
        Multiple(() =>
        {
            Assert.That(result.ExploratoryFrontierOptionIds, Is.EqualTo(new[] { "physical" }));
            Assert.That(result.ExploratorySuggestedOptionId, Is.EqualTo("physical"));
            Assert.That(result.InitialSuggestedOptionId, Is.Null);
        });
    }

    [Test]
    public void Retains_all_rejection_reasons_and_does_not_trust_boolean_uncertainty()
    {
        var option = Option("unsafe", 50) with
        {
            PhysicalEndToEndAttested = false, PhysicalP95Milliseconds = 60001,
            IdentityClusterFdrUpper = null, IndependentValidationIdentityGroups = 1,
            EvidenceRejectionReasons = new[] { "ORACLE_NAO_OPERACIONAL" }
        };
        var result = V9BootstrapPareto2D.Evaluate([option], Policy with { MinimumGlobalRecall = .9 });
        Multiple(() =>
        {
            Assert.That(result.InitialSuggestedOptionId, Is.Null);
            Assert.That(result.Table[0].ExclusionReasons, Is.EquivalentTo(new[] {
                "P95_FISICO_NAO_ATESTADO", "P95_ACIMA_DO_LIMITE", "EVIDENCIA_ESTATISTICA_INSUFICIENTE",
                "RECALL_ABAIXO_DA_POLITICA", "ORACLE_NAO_OPERACIONAL" }));
        });
    }

    [Test]
    public void Rejects_actual_upper_bound_over_policy_even_when_point_estimate_is_zero()
    {
        var result = V9BootstrapPareto2D.Evaluate([Option("no-fp", 100) with { IdentityClusterFdrUpper = .11 }], Policy);
        Assert.That(result.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Table[0].ExclusionReasons, Does.Contain("LIMITE_FDR_ACIMA_DA_POLITICA"));
    }

    [Test]
    public void No_acceptances_invalid_denominator_nan_and_overflow_fail_closed_in_table()
    {
        var options = new[] { Option("zero", 0), Option("denominator", 1) with { GlobalEligibleTruth = 0 },
            Option("nan", 2) with { GlobalRecall = double.NaN },
            Option("overflow", 1) with { TrueAccepted = long.MaxValue, FalseAccepted = 1 } };
        var result = V9BootstrapPareto2D.Evaluate(options, Policy);
        Assert.That(result.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Table.All(x => !x.Eligible && x.ExclusionReasons.Count > 0), Is.True);
    }

    [Test]
    public void Rejects_inconsistent_metrics_multiplicity_and_unknown_bound()
    {
        var result = V9BootstrapPareto2D.Evaluate([
            Option("ratio", 90) with { GlobalRecall = .95 },
            Option("multiplicity", 95) with { MultiplicityComparedConfigurations = 1 },
            Option("unknown", 95) with { IdentityClusterFdrUpper = null }], Policy);
        Assert.That(result.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Table.Single(x => x.Option.OptionId == "ratio").ExclusionReasons,
            Does.Contain("METRICAS_INCONSISTENTES"));
    }


    [Test]
    public void Equal_distance_prefers_lower_observed_fdr_before_fingerprint()
    {
        // Two Pareto endpoints have identical normalized distance (= 1).
        // The riskier endpoint deliberately has the earlier fingerprint.
        var safe = Option("safe", 80, hash: 'f');
        var risky = Option("riskier", 100, 4, hash: 'a');
        var forward = V9BootstrapPareto2D.Evaluate([risky, safe], Policy);
        var reverse = V9BootstrapPareto2D.Evaluate([safe, risky], Policy);
        Multiple(() =>
        {
            Assert.That(forward.ExploratoryFrontierOptionIds,
                Is.EquivalentTo(new[] { "safe", "riskier" }));
            Assert.That(forward.ExploratorySuggestedOptionId, Is.EqualTo("safe"));
            Assert.That(forward.InitialSuggestedOptionId, Is.EqualTo("safe"));
            Assert.That(reverse.ExploratorySuggestedOptionId, Is.EqualTo("safe"));
            Assert.That(reverse.InitialSuggestedOptionId, Is.EqualTo("safe"));
        });
    }

    [Test]
    public void Exploratory_frontier_excludes_structurally_non_operational_evidence()
    {
        var fs = Option("fs-blind", 80, hash: 'c') with
        {
            PhysicalP95Milliseconds = null, PhysicalEndToEndAttested = false,
            IdentityClusterUncertaintyAttested = false, IdentityClusterFdrUpper = null
        };
        var raw = Option("raw-sql", 100, hash: 'a') with
        {
            EvidenceRejectionReasons = ["AGRUPAMENTO_NAO_OPERACIONAL"]
        };
        var impure = Option("impure", 99, hash: 'b') with
        {
            EvidenceRejectionReasons = ["AGRUPAMENTO_CEGO_IMPURO"]
        };
        var wrongCohort = Option("mixed-cohort", 98, hash: 'd') with
        {
            EvidenceRejectionReasons = ["TRAIN_VALIDATION_NAO_INDEPENDENTES"]
        };
        var result = V9BootstrapPareto2D.Evaluate(
            [wrongCohort, raw, fs, impure], Policy);
        Multiple(() =>
        {
            Assert.That(result.ExploratoryFrontierOptionIds, Is.EqualTo(new[] { "fs-blind" }));
            Assert.That(result.ExploratorySuggestedOptionId, Is.EqualTo("fs-blind"));
            Assert.That(result.InitialSuggestedOptionId, Is.Null);
            Assert.That(result.ExploratoryStatus, Is.EqualTo("EXPLORATORY_ONLY_NOT_CERTIFIED"));
        });
    }

    [Test]
    public void Legacy_overload_requires_an_explicit_risk_policy_to_suggest()
    {
        var result = V9BootstrapPareto2D.Evaluate([Option("safe", 100)]);
        Assert.That(result.InitialSuggestedOptionId, Is.Null);
        Assert.That(result.Table[0].ExclusionReasons, Does.Contain("POLITICA_DE_RISCO_NAO_DECLARADA"));
    }

    [Test]
    public void Copies_rejection_evidence_and_returns_immutable_tables()
    {
        var reasons = new List<string> { "INDEPENDENCE_NOT_PROVEN" };
        var result = V9BootstrapPareto2D.Evaluate([Option("option", 100) with { EvidenceRejectionReasons = reasons }], Policy);
        reasons.Clear();
        Assert.That(result.Table[0].ExclusionReasons, Does.Contain("INDEPENDENCE_NOT_PROVEN"));
        Assert.That(result.Table[0].Option.EvidenceRejectionReasons, Does.Contain("INDEPENDENCE_NOT_PROVEN"));
        Throws<NotSupportedException>(() => ((IList<V9BootstrapPareto2D.Evaluation>)result.Table).Clear());
    }

    [Test]
    public void Fingerprint_then_option_id_break_exact_ties()
    {
        var result = V9BootstrapPareto2D.Evaluate([
            Option("z", 95, hash: 'a'), Option("a", 95, hash: 'a'), Option("first", 95, hash: 'b')], Policy);
        Assert.That(result.InitialSuggestedOptionId, Is.EqualTo("a"));
    }
}

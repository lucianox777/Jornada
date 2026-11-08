using NUnit.Framework;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public class V9MuEstimationSearchSpaceTests
{
    private static V9MuEstimationMethod Default() => new(V9MuEstimationSearchSpace.Default);
    private static V9MuEstimationMethod HistoricalV3() => new(
        V9MuEstimationSearchSpace.Classwise,
        new V9MuEstimationParameterGrid([.99m], [5], [5], [true]));

    [Test]
    public void Historical_V2_and_V3_are_two_explicit_branches_with_exact_V3_values()
    {
        var methods = new[] { Default(), HistoricalV3() };
        var variants = V9MuEstimationSearchSpace.Enumerate(methods);
        Assert.That(variants, Has.Count.EqualTo(2));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(variants[0].Method, Is.EqualTo(V9MuEstimationSearchSpace.Default));
            Assert.That(variants[0].Policy, Is.Null);
            Assert.That(variants[1].Method, Is.EqualTo(V9MuEstimationSearchSpace.Classwise));
            Assert.That(variants[1].Policy!.ClassMassCoverage, Is.EqualTo(.99m));
            Assert.That(variants[1].Policy!.MinimumM, Is.EqualTo(5));
            Assert.That(variants[1].Policy!.MinimumU, Is.EqualTo(5));
            Assert.That(variants[1].Policy!.SupportedTailRule, Is.EqualTo(V9JointSupportEstimation.SupportedTailRule));
        }));
        var original = new[] { new V9FsAdaptiveOptionConfiguration("ONE_MF", .5m, .95m) };
        var expanded = V9MuEstimationSearchSpace.Expand(original, methods, maximumOptionCount: 2);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(expanded, Has.Count.EqualTo(2));
            Assert.That(expanded[0].JointSupportPolicy, Is.Null);
            Assert.That(expanded[1].JointSupportPolicy, Is.EqualTo(variants[1].Policy));
            Assert.That(expanded[0].SmoothingAlpha, Is.EqualTo(expanded[1].SmoothingAlpha));
            Assert.That(expanded[0].DecisionThreshold, Is.EqualTo(expanded[1].DecisionThreshold));
            Assert.That(expanded[0].FingerprintSha256, Is.Not.EqualTo(expanded[1].FingerprintSha256));
            Assert.That(original[0].JointSupportPolicy, Is.Null);
        }));
    }

    [Test]
    public void V2_permanent_witness_is_physically_present_but_ineligible_while_candidates_remain()
    {
        var option = new V9FsAdaptiveOptionConfiguration("HISTORICAL_TF0", .5m, .95m);
        var original = new[] { option };
        var methods = new[] {
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            HistoricalV3() with { Role = V9MuEstimationSearchSpace.Candidate }
        };
        var variants = V9MuEstimationSearchSpace.Enumerate(methods);
        var expanded = V9MuEstimationSearchSpace.Expand(original, methods, 2);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(variants.Select(x => x.Role), Is.EqualTo(new[] { "WITNESS", "CANDIDATE" }));
            Assert.That(variants[0].Anchor, Is.True);
            Assert.That(variants[1].Anchor, Is.False);
            Assert.That(expanded, Has.Count.EqualTo(2));
            Assert.That(expanded[0].JointSupportPolicy, Is.Null, "V2 witness must preserve original V2 estimator.");
            Assert.That(expanded[0].SmoothingAlpha, Is.EqualTo(option.SmoothingAlpha));
            Assert.That(expanded[0].DecisionThreshold, Is.EqualTo(option.DecisionThreshold));
            Assert.That(expanded[0].EligibleForSelection, Is.False);
            Assert.That(expanded[1].EligibleForSelection, Is.True);
            Assert.That(expanded[0].FingerprintSha256, Is.Not.EqualTo(expanded[1].FingerprintSha256));
            Assert.That(original[0].EligibleForSelection, Is.True, "Original plan cannot be mutated.");
        }));
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Expand(original,
            [Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true }], 1)),
            Throws.TypeOf<InvalidDataException>(), "A witness-only plan has no candidate to select.");
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default() with { Role = "UNKNOWN" }, HistoricalV3()
        ])), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default() with { Role = V9MuEstimationSearchSpace.Witness },
            HistoricalV3() with { Role = V9MuEstimationSearchSpace.Candidate }
        ])), Throws.TypeOf<InvalidDataException>(), "Multiparameter V2 cannot lose ANCHOR.");
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default() with { Role = V9MuEstimationSearchSpace.Candidate, Anchor = true },
            HistoricalV3() with { Role = V9MuEstimationSearchSpace.Candidate }
        ])), Throws.TypeOf<InvalidDataException>(), "ANCHOR must be non-selectable historical V2.");

    }

    [Test]
    public void Predeclared_candidate_CP_upper_bound_subtracts_initial_witness_only()
    {
        Assert.That(V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(40, 10),
            Is.EqualTo(30), "V2 and lambda=0 witness runs do NOT increase CP multiplicity");
        Assert.That(V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(30, 0),
            Is.EqualTo(30), "Adding 10 physical witnesses to unchanged candidates cannot increase CP");
        Assert.That(V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(40, 20),
            Is.EqualTo(20));
        Assert.That((Action)(() => V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(10, 10)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(10, -1)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Lambda_zero_is_physical_U_witness_but_not_a_CP_candidate()
    {
        var option = new V9FsAdaptiveOptionConfiguration("T0", .5m, .95m);
        var methods = new V9MuEstimationMethod[] {
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new V9MuEstimationParameterGrid([.99m], [5], [5], [true],
                    [0m, .25m, .5m, 1m]), V9MuEstimationSearchSpace.Candidate)
        };
        var expanded = V9MuEstimationSearchSpace.Expand([option], methods, 5);
        Assert.That(expanded, Has.Count.EqualTo(5), "All five modes are physically enumerated");
        Assert.That(expanded[0].EligibleForSelection, Is.False, "V2 WITNESS+ANCHOR");
        Assert.That(expanded[1].JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Zero);
        Assert.That(expanded[1].EligibleForSelection, Is.False, "lambda0 witness must be excluded in CP adapter");
        Assert.That(expanded.Skip(2).All(x => x.EligibleForSelection), Is.True);
        Assert.That(V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(5, 2),
            Is.EqualTo(3));
        // New PO decision (08/11): omitted optional Role uses historical
        // candidate default. V2 WITNESS+ANCHOR remains explicit.
        var withOmittedRole = V9MuEstimationSearchSpace.Enumerate([
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            HistoricalV3()
        ]);
        Assert.That(withOmittedRole, Has.Count.EqualTo(2));
        Assert.That(withOmittedRole[0].Role, Is.EqualTo(V9MuEstimationSearchSpace.Witness));
        Assert.That(withOmittedRole[1].Role, Is.Null);
        Assert.That(V9MuEstimationSearchSpace.Expand([option], [
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            HistoricalV3()
        ], 2)[1].EligibleForSelection, Is.True);
    }

    [Test]
    public void Explicit_all_historical_V2_CSharp_defaults_preserve_exact_fit_fingerprint()
    {
        // These are the two original, unexpanded canonical V2 TF controls
        // used by v9-mu-tf0-controlled-plan.py and the one official plan.json.
        // The new JSON must spell out every default without changing any
        // scorer-effective configuration value or its frozen identity.
        var frozenOptionJson = new[]
        {
            """
            {"OptionId":"A0.5_T0.95_TF0","SmoothingAlpha":0.5,"DecisionThreshold":0.95,"TfWeight":0}
            """,
            """
            {"OptionId":"TF_ONE_MF_W1_M0_A0.5_T0.95","SmoothingAlpha":0.5,
             "DecisionThreshold":0.95,"TfWeight":0,
             "UnitTfOptions":[
              {"Side":0,"Unit":2,"Weight":1,"Mode":0},
              {"Side":0,"Unit":3,"Weight":0,"Mode":0},
              {"Side":1,"Unit":2,"Weight":0,"Mode":0},
              {"Side":1,"Unit":3,"Weight":0,"Mode":0}
             ]}
            """
        };
        foreach (var historical in frozenOptionJson)
        {
            var original = System.Text.Json.JsonSerializer.Deserialize<V9FsAdaptiveOptionConfiguration>(historical)!;
            var node = System.Text.Json.Nodes.JsonNode.Parse(historical)!.AsObject();
            node["TfSide"] = 0;
            node["TfMinimumFrequency"] = 0.000001m;
            node["TfMinimumIndependentSupport"] = 1;
            node["JointSupportPolicy"] = null;
            if (!node.ContainsKey("UnitTfOptions")) node["UnitTfOptions"] = null;
            node["SharedSurnameMode"] = 0;
            node["EligibleForSelection"] = true;
            node["UnpublishedTermPolicy"] = 0;
            var explicitV2 = System.Text.Json.JsonSerializer
                .Deserialize<V9FsAdaptiveOptionConfiguration>(node.ToJsonString())!;
            Assert.That(explicitV2.FingerprintSha256, Is.EqualTo(original.FingerprintSha256),
                "Explicit V2 defaults must not modify the canonical frozen fit identity");
            Assert.Multiple((Action)(() =>
            {
                Assert.That(explicitV2.OptionId, Is.EqualTo(original.OptionId));
                Assert.That(explicitV2.SmoothingAlpha, Is.EqualTo(original.SmoothingAlpha));
                Assert.That(explicitV2.DecisionThreshold, Is.EqualTo(original.DecisionThreshold));
                Assert.That(explicitV2.TfWeight, Is.EqualTo(original.TfWeight));
                Assert.That(explicitV2.TfSide, Is.EqualTo(original.TfSide));
                Assert.That(explicitV2.TfMinimumFrequency, Is.EqualTo(original.TfMinimumFrequency));
                Assert.That(explicitV2.TfMinimumIndependentSupport,
                    Is.EqualTo(original.TfMinimumIndependentSupport));
                Assert.That(explicitV2.JointSupportPolicy, Is.EqualTo(original.JointSupportPolicy));
                Assert.That(explicitV2.SharedSurnameMode, Is.EqualTo(original.SharedSurnameMode));
                Assert.That(explicitV2.EligibleForSelection, Is.EqualTo(original.EligibleForSelection));
                Assert.That(explicitV2.UnpublishedTermPolicy, Is.EqualTo(original.UnpublishedTermPolicy));
                Assert.That(explicitV2.UnitTfOptions?.Select(x => (x.Side, x.Unit, x.Weight, x.Mode)),
                    Is.EqualTo(original.UnitTfOptions?.Select(x => (x.Side, x.Unit, x.Weight, x.Mode))));
            }));
            node["UnpublishedTermPolicy"] = 1;
            var retuned = System.Text.Json.JsonSerializer
                .Deserialize<V9FsAdaptiveOptionConfiguration>(node.ToJsonString())!;
            Assert.That(retuned.FingerprintSha256, Is.Not.EqualTo(original.FingerprintSha256),
                "A declared effective model change must not masquerade as historical V2");
        }
    }

    [Test]
    public void Historical_methods_without_explicit_role_stay_eligible_and_serialization_omits_new_field()
    {
        var legacy = Default();
        var option = new V9FsAdaptiveOptionConfiguration("ORIGINAL", .5m, .95m);
        var expanded = V9MuEstimationSearchSpace.Expand([option], [legacy, HistoricalV3()], 2);
        Assert.That(expanded.All(x => x.EligibleForSelection), Is.True);
        var bytes = System.Text.Json.JsonSerializer.Serialize(legacy);
        Assert.That(bytes, Does.Not.Contain("Role"));
        Assert.That(bytes, Does.Not.Contain("WITNESS"));
    }

    [Test]
    public void Lists_generate_only_explicit_cartesian_values_without_implicit_ranges()
    {
        var variants = V9MuEstimationSearchSpace.Enumerate([
            Default(),
            new(V9MuEstimationSearchSpace.Classwise,
                new([.95m, .99m], [3, 5], [3, 5], [true, false]))
        ]);
        Assert.That(variants, Has.Count.EqualTo(17));
        Assert.That(variants.Count(x => x.Policy is not null), Is.EqualTo(16));
        Assert.That(variants.Count(x => x.Policy?.ClassMassCoverage == .95m), Is.EqualTo(8));
        Assert.That(variants.Count(x => x.Policy?.ClassMassCoverage == .99m), Is.EqualTo(8));
        Assert.That(variants.Count(x => x.Policy?.SupportedTailRule is null), Is.EqualTo(9));
    }

    [Test]
    public void Explicit_U_shrinkage_grid_is_finite_and_has_unique_fingerprints()
    {
        var variants = V9MuEstimationSearchSpace.Enumerate([
            new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))
        ]);
        Assert.That(variants, Has.Count.EqualTo(4));
        Assert.That(variants.Select(x => x.Key).Distinct().Count(), Is.EqualTo(4));
        Assert.That(variants.Select(x => x.Policy!.UBackoffJointMixtureWeight),
            Is.EqualTo(new decimal?[] {0m, .25m, .5m, 1m}));
        Assert.That(variants.All(x => x.Key.Contains("_UJW", StringComparison.Ordinal)), Is.True);
        var original = new[] {new V9FsAdaptiveOptionConfiguration("TF0", .5m, .95m)};
        var expanded = V9MuEstimationSearchSpace.Expand(original,
            [new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))], 4);
        Assert.That(expanded.Select(x => x.FingerprintSha256).Distinct().Count(), Is.EqualTo(4));
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Expand(original,
            [new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))], 3)),
            Throws.TypeOf<InvalidDataException>());
        foreach (var weights in new[] {Array.Empty<decimal>(), new[] {.25m, .25m}, new[] {-1m}, new[] {1.1m}})
            Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
                new(V9MuEstimationSearchSpace.Classwise, new([.99m], [5], [5], [true], weights))
            ])), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void V2_historical_V3_and_explicit_U_mixture_coexist_with_distinct_provenance()
    {
        var methods = new V9MuEstimationMethod[]
        {
            Default(),
            HistoricalV3(),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))
        };
        var variants = V9MuEstimationSearchSpace.Enumerate(methods);
        Assert.That(variants, Has.Count.EqualTo(6));
        Assert.That(variants.Select(x => x.Method), Is.EqualTo(new[]
        {
            V9MuEstimationSearchSpace.Default,
            V9MuEstimationSearchSpace.Classwise,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture
        }));
        Assert.That(variants[1].Policy!.UBackoffJointMixtureWeight, Is.Null);
        Assert.That(variants[2].Policy!.UBackoffJointMixtureWeight, Is.Zero);
        Assert.That(variants[1].Key, Does.Not.Contain("_UJW"));
        Assert.That(variants[2].Key, Does.Contain("_UJW0"));
        Assert.That(variants.Select(x => x.Key).Distinct().Count(), Is.EqualTo(6));

        var original = new[] { new V9FsAdaptiveOptionConfiguration("TF0", .5m, .95m) };
        var expanded = V9MuEstimationSearchSpace.Expand(original, methods, 6);
        Assert.That(expanded, Has.Count.EqualTo(6));
        Assert.That(expanded.Select(x => x.FingerprintSha256).Distinct().Count(), Is.EqualTo(6));
        Assert.That(expanded[1].JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Null);
        Assert.That(expanded[2].JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Zero);
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Expand(original, methods, 5)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default(), HistoricalV3(),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true]))
        ])), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default(),
            new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m])),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true], [0m]))
        ])), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Unknown_methods_duplicate_values_and_absent_parameters_fail_closed()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([Default(), Default()])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new("UNKNOWN")])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise)])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Default,
                new([.99m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([.99m, .99m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([1m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [0], [5], [true]))])));
        }));
    }

    [Test]
    public void J_official_selection_excludes_19_pass_diagnostics_without_changing_historical_plans()
    {
        V9MuEstimationMethod[] official =
        [
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            HistoricalV3() with { Role = V9MuEstimationSearchSpace.Candidate }
        ];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9NestedCalibrationCoordinator.IsNineteenPassDiagnostic(official, 19), Is.True);
            Assert.That(V9NestedCalibrationCoordinator.IsNineteenPassDiagnostic(official, 3), Is.False);
            Assert.That(V9NestedCalibrationCoordinator.IsNineteenPassDiagnostic([Default(), HistoricalV3()], 19), Is.False);
            Assert.That(V9NestedCalibrationCoordinator.IsNineteenPassDiagnostic(null, 19), Is.False);
            Assert.That(V9NestedCalibrationCoordinator.CandidateCpMultiplicityUpperBudget(24, 16), Is.EqualTo(8));
        }));
    }

    [Test]
    public void J_refinement_never_reopens_nineteen_pass_or_zero_margin_after_a_selectable_frontier()
    {
        V9MuEstimationMethod[] anchored =
        [
            Default() with { Role = V9MuEstimationSearchSpace.Witness, Anchor = true },
            HistoricalV3() with { Role = V9MuEstimationSearchSpace.Candidate }
        ];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(anchored, 3, .5m), Is.True);
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(anchored, 19, .5m), Is.False);
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(anchored, 3, 0m), Is.False);
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(anchored, 19, 0m), Is.False);
            // #1300 historical plans without a WITNESS+ANCHOR must retain
            // the exact former neighborhood, not retrofit scientific rules.
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(
                [Default(), HistoricalV3()], 19, 0m), Is.True);
            Assert.That(V9NestedCalibrationCoordinator.AllowsRefinement(null, 19, 0m), Is.True);
        }));
    }

    [Test]
    public void Human_report_never_labels_measured_V2_or_nineteen_pass_witness_as_eligible()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(false, null, 0),
                Is.EqualTo("REJEITADA"));
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(true, false, 0),
                Is.EqualTo("DIAGNÓSTICO_NÃO_SELECIONÁVEL"));
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(true, false, 2),
                Is.EqualTo("DIAGNÓSTICO_NÃO_SELECIONÁVEL"));
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(true, true, 1),
                Is.EqualTo("INELEGÍVEL"));
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(true, true, 0),
                Is.EqualTo("ELEGÍVEL"));
            Assert.That(V9NestedCalibrationCli.HumanReadableEvaluationStatus(true, null, 0),
                Is.EqualTo("ELEGÍVEL"), "historical plans retain eligibility");
            Assert.That((Action)(() => V9NestedCalibrationCli.HumanReadableEvaluationStatus(
                true, false, -1)), Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }

    [Test]
    public void Private_source_rank_lists_are_requested_only_for_explicit_paired_V2_V3()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics(null), Is.False);
            Assert.That(V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics([Default()]), Is.False);
            Assert.That(V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics([HistoricalV3()]), Is.False);
            Assert.That(V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics([Default(), HistoricalV3()]), Is.True);
            Assert.That(V9MuEstimationSearchSpace.RequiresPairedSourceRankDiagnostics([HistoricalV3(), Default()]), Is.True);
        }));
        // Only the private source-by-configuration Rank detail is disabled
        // for V3-only. Existing per-identity outcomes for FDR are untouched.
    }

    [Test]
    public void Cannot_ignore_declared_grid_because_computation_budget_is_too_small()
    {
        var original = new[] { new V9FsAdaptiveOptionConfiguration("A", .5m, .95m) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MuEstimationSearchSpace.Expand(original, [Default(), HistoricalV3()], maximumOptionCount: 1)));
    }

    [Test]
    public void A_preexisting_MU_policy_must_not_be_silently_overwritten_by_a_new_method()
    {
        var policy = new V9JointSupportPolicy(.99m, 5, 5, V9JointSupportEstimation.SupportedTailRule);
        var original = new[] { new V9FsAdaptiveOptionConfiguration("A", .5m, .95m, JointSupportPolicy: policy) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MuEstimationSearchSpace.Expand(original, [Default()], maximumOptionCount: 1)));
    }
}

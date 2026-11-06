using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9AdaptiveCalibrationCoordinatorTests
{
    private static readonly System.Text.Json.JsonSerializerOptions GeneratedPlanJsonOptions = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

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
    public void Real_prepare_tf_plan_passes_all_pre_sql_selection_validators_without_pruning_45_arms()
    {
        // Exercise the REAL Python prepare-tf producer and the C# validation
        // boundaries that failed only after costly physical SQL in the 30k run.
        // The small fixture is input to plan GENERATION only, not a physical
        // corpus, IBGE evidence, statistical result, or certified pin.
        DirectoryInfo? solution = new DirectoryInfo(AppContext.BaseDirectory);
        while (solution is not null
            && !File.Exists(Path.Combine(solution.FullName, "scripts", "v9-adaptive-30k-study.py")))
            solution = solution.Parent;
        var solutionPath = solution?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the real prepare-tf producer.");
        var temp = Path.Combine(Path.GetTempPath(), "v9-tf-plan-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var corpus = Path.Combine(temp, "observacoes.csv");
            File.WriteAllText(corpus, "fixture for deterministic plan generation only\n");
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(corpus)));
            File.WriteAllText(Path.Combine(temp, "generation-manifest.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    seed = 42, population_profile = "DEMOGRAPHIC_PRIMARY_V1", error_profile = "clean",
                    generator_version = "JORNADA_SYNTH_CORPUS_CSHARP_V1",
                    observation_multiplicity = new
                    {
                        contract_version = "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                        maximum_observations_per_base_identity = 4,
                        scope = "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"
                    }
                }));
            var pin = Path.Combine(temp, "pinned-ibge.json");
            File.WriteAllText(pin, System.Text.Json.JsonSerializer.Serialize(new
            {
                PinnedIbgeReferenceVersionId = 7,
                PinnedIbgePublishedSha256 = new string('A', 64),
                PinnedIbgeMaterializedFrequencySha256 = new string('B', 64),
                PinnedIbgeBootstrap = new
                {
                    Provenance = "SINTETICO_IBGE", People = 30000, CorpusFingerprint = digest,
                    GeneratorVersion = "JORNADA_SYNTH_CORPUS_CSHARP_V1", GeneratorSeed = 42,
                    GenerationHypothesesFingerprint = new string('C', 64)
                }
            }));
            var destination = Path.Combine(temp, "plan.json");
            var psi = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
            {
                WorkingDirectory = solutionPath,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] {
                Path.Combine(solutionPath, "scripts", "v9-adaptive-30k-study.py"),
                "prepare-tf", corpus, destination, "--pinned-ibge", pin
            })
                psi.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Python prepare-tf did not start.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.That(process.WaitForExit(60_000), Is.True, "prepare-tf timed out.");
            Assert.That(process.ExitCode, Is.Zero, "prepare-tf rejected its small test fixture: " + stderr + stdout);
            var plan = System.Text.Json.JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(
                File.ReadAllBytes(destination), GeneratedPlanJsonOptions)
                ?? throw new InvalidDataException("Generated TF plan missing.");

            Assert.Multiple((Action)(() =>
            {
                Assert.That(plan.Budget.MinimumGlobalAcceptedRecall, Is.EqualTo(.95));
                Assert.That(plan.Budget.MaximumObservedAcceptedFdrPoint, Is.EqualTo(.01));
                Assert.That(plan.SelectionPolicy.MinimumGlobalRecall, Is.EqualTo(.95));
                Assert.That(plan.SelectionPolicy.MaximumAcceptedFdrUpper, Is.EqualTo(.01));
                Assert.That(plan.ModelOptions, Has.Count.EqualTo(45));
                Assert.That(plan.ModelOptions.Count(x => x.EligibleForSelection), Is.EqualTo(41));
                Assert.That(plan.ModelOptions.Count(x => !x.EligibleForSelection), Is.EqualTo(4));
                Assert.That(plan.PreviouslyComparedValidationConfigurations, Is.EqualTo(3726));
            }));
            // All arms are counted, including 4 measured-but-ineligible diagnostics;
            // eligibility filters never prune physical model or conflict-margin work.
            V9AdaptiveCalibrationCoordinator.ValidatePlan(plan);
            Assert.That(V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(plan, 1), Is.EqualTo(3861));
            Assert.That(V9AdaptiveCalibrationCoordinator.CountSelectableConfigurations(plan, 1), Is.EqualTo(3849));

            // Cross the ACTUAL post-Resolve and Pareto contract boundaries with
            // synthetic source metrics and no SQL/TEST. On the broken branch this
            // threw "Orçamento de seleção inválido" after the expensive SQL run.
            var row = new V9PhysicalSourceResolveMetric("T0.95_TF0", .95m, 0m, .5m,
                "BLIND_FS_COMPLETE_LINK_CANDIDATES", 100, 100, 100, 96, 0, 0, 4,
                1, .96, 0d, 1d, 50,
                new Dictionary<string, long> { ["ALL_DECISIONS"] = 100 }, false, false, 0);
            var fanout = new Dictionary<string, double> { [row.OptionId] = 10d };
            var source = V9PostResolveAdaptiveSelector.EvaluateValidation(
                "VALIDATION", false, plan.Budget, [row], fanout);
            Assert.That(source.TestUsed, Is.False);
            Assert.That(source.AutomaticActivationAllowed, Is.False);
            var pareto = V9ParetoSourceSelection.EvaluateValidation("VALIDATION", false,
                plan.Budget, [row], fanout, [], plan.SelectionPolicy);
            Assert.That(pareto.Pareto.Table, Has.Count.EqualTo(1));
            Assert.That(pareto.Pareto.InitialSuggestedOptionId, Is.Null,
                "Missing physical evidence can never certify a synthetic contract fixture.");
            Assert.That((Action)(() => V9PostResolveAdaptiveSelector.EvaluateValidation(
                    "VALIDATION", false,
                    plan.Budget with { MaximumObservedAcceptedFdrPoint = 1d }, [row], fanout)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("Orçamento de seleção inválido"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
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
    public void Optional_CPU_percentage_is_fail_closed_and_default_plan_has_unchanged_serialized_contract()
    {
        var plan = Plan();
        V9AdaptiveCalibrationCoordinator.ValidatePlan(plan);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(plan),
            Does.Not.Contain("ParallelCpuPercent"));
        var automatic = plan with { MaximumParallelRuleSets = 16, ParallelCpuPercent = 80 };
        Assert.DoesNotThrow((Action)(() =>
            V9AdaptiveCalibrationCoordinator.ValidatePlan(automatic)));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(automatic),
            Does.Contain("\"ParallelCpuPercent\":80"));
        foreach (var percent in new[] { -1, 0, 101, 200 })
            Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.ValidatePlan(
                automatic with { ParallelCpuPercent = percent })),
                Throws.TypeOf<InvalidDataException>());
        // The option is a resource control; it must not alter the declared
        // model variants, statistical multiplicity, or selection policy.
        Assert.That(automatic.ModelOptions, Is.EqualTo(plan.ModelOptions));
        Assert.That(automatic.SelectionPolicy, Is.EqualTo(plan.SelectionPolicy));
        Assert.That(automatic.MaximumComparedConfigurations,
            Is.EqualTo(plan.MaximumComparedConfigurations));
    }

    [Test]
    public void Exact_equivalence_and_shards_require_a_complete_catalogue_zero_tf_and_valid_ordinals()
    {
        var plan = Plan();
        V9DeclaredBlockingSearchSpace space = new([new("P", ["name_first"])]);
        var valid = plan with { DeclaredSearchSpace = space, CollapseEquivalentCandidateFrames = true };
        Assert.DoesNotThrow((Action)(() => V9AdaptiveCalibrationCoordinator.ValidatePlan(valid)));
        foreach (var invalid in new[] {
            plan with { CollapseEquivalentCandidateFrames = true },
            plan with { ShardCount = 2 },
            valid with { ShardIndex = 1 }, valid with { ShardCount = 0 },
            valid with { ShardIndex = -1 },
            valid with { ModelOptions = [new("TF", .5m, .95m, TfWeight: 1)] }
        })
            Assert.That((Action)(() => V9AdaptiveCalibrationCoordinator.ValidatePlan(invalid)), Throws.TypeOf<InvalidDataException>());
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

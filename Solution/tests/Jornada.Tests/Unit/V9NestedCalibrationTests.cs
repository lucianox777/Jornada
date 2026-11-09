using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9NestedCalibrationTests
{
    // Reuse frozen options; creation per operation is rejected by CA1869.
    private static readonly JsonSerializerOptions SelectionJson = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    [Test]
    public void Conditional_finalist_choice_preserves_group_risk_cost_and_family_constraints()
    {
        var c = Candidate(180, 0, .95m);
        var m = c.Metric with { ConditionalClusterCpFdrUpper = .005, BonferroniComparedConfigurations = 1 };
        var measured = new V9FrozenHoldoutEvaluation(c.Snapshot.ContentSha256, m, 10, 2, "fixture", []);
        var policy = new V9BootstrapPareto2D.SelectionPolicy(.8, .01, 100, 20);
        Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([measured], policy, 2), Is.SameAs(measured));
        Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([measured], policy with { MinimumIndependentIdentityGroups = 101 }, 2), Is.Null);
        Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([measured], policy with { MaximumAcceptedFdrUpper = .001 }, 2), Is.Null);
        Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([measured], policy with { MaximumP95Milliseconds = 5 }, 2), Is.Null);
        Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([measured], policy, 1), Is.Null);
        Assert.That((Action)(() => V9NestedCalibrationCli.SelectConditionalWinner([measured with { Refitted = true }], policy, 2)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9NestedCalibrationCli.SelectConditionalWinner([measured with { Metric = m with { BonferroniComparedConfigurations = 3 } }], policy, 2)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Scientific_finalist_proposal_ignores_runtime_P95_but_conditional_operability_does_not()
    {
        var candidate = Candidate(180, 0, .95m);
        var metric = candidate.Metric with { ConditionalClusterCpFdrUpper = .005,
            BonferroniComparedConfigurations = 1 };
        var fast = new V9FrozenHoldoutEvaluation(candidate.Snapshot.ContentSha256,
            metric, 10, 2, "fixture", []);
        var slow = fast with { P95Milliseconds = 30000 };
        var unmeasured = fast with { P95Milliseconds = null };
        var policy = new V9BootstrapPareto2D.SelectionPolicy(.8, .01, 100, 20);
        var scientific = V9NestedCalibrationCli.SelectScientificWinner([fast], policy, 2);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(scientific, Is.SameAs(fast));
            Assert.That(V9NestedCalibrationCli.SelectScientificWinner([slow], policy, 2)?.SnapshotSha256,
                Is.EqualTo(scientific?.SnapshotSha256));
            Assert.That(V9NestedCalibrationCli.SelectScientificWinner([unmeasured], policy, 2)?.SnapshotSha256,
                Is.EqualTo(scientific?.SnapshotSha256));
            Assert.That(V9NestedCalibrationCli.SelectScientificWinner([fast], policy with
                { MaximumP95Milliseconds = 1 }, 2)?.SnapshotSha256,
                Is.EqualTo(scientific?.SnapshotSha256));
            Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([fast], policy, 2), Is.SameAs(fast));
            Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([slow], policy, 2), Is.Null);
            Assert.That(V9NestedCalibrationCli.SelectConditionalWinner([unmeasured], policy, 2), Is.Null);
        }));
    }

    [Test]
    public async Task Streaming_selection_envelope_is_byte_and_SHA_equivalent_to_original_serializer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jornada-v9-selection-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var envelope = new
            {
                result = Enumerable.Range(0, 2500).Select(i => new
                {
                    BaseIdentity = "synthetic-" + i,
                    EligibleSources = 3,
                    TrueAccepted = i % 3,
                    FalseAccepted = i % 17 == 0 ? 1 : 0
                }).ToArray(),
                statisticalCertification = false,
                operationalActivation = false
            };
            var original = JsonSerializer.SerializeToUtf8Bytes(envelope, SelectionJson);
            var expected = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant();
            var actual = await V9NestedCalibrationCli.WriteContentAddressedSelectionAsync(directory, envelope);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(Path.Combine(directory, expected + ".selection.json")),
                Is.EqualTo(original));
            Assert.That(Directory.GetFiles(directory, "*.pending.json"), Is.Empty);
            Assert.That((Func<Task>)(async () =>
                await V9NestedCalibrationCli.WriteContentAddressedSelectionAsync(directory, envelope)),
                Throws.TypeOf<IOException>());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void Lambda_zero_U_mix_is_numeric_witness_and_not_an_independent_candidate()
    {
        Assert.That(V9NestedCalibrationCoordinator.IsUZeroWitness(
            "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE", 0m), Is.True);
        Assert.That(V9NestedCalibrationCoordinator.IsUZeroWitness(
            "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE", .25m), Is.False);
        Assert.That(V9NestedCalibrationCoordinator.IsUZeroWitness(
            "CLASSWISE_SUPPORT_BACKOFF", null), Is.False);
        Assert.That(V9NestedCalibrationCoordinator.IsUZeroWitness(
            "DEFAULT", 0m), Is.False);
    }

    [Test]
    public void Calibrador_reserves_real_10KiB_and_rewrites_only_the_last_error_in_place()
    {
        var output = Path.Combine(Path.GetTempPath(), "v9-calibrador-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            using var reservation = V9CalibradorLastErrorLog.Reserve(output);
            var file = Path.Combine(output, V9CalibradorLastErrorLog.FileName);
            Assert.That(new FileInfo(file).Length, Is.EqualTo(10 * 1024));
            V9NestedCalibrationCli.TryWriteLastError(reservation,
                new InvalidDataException("FIRST_ERROR_SENTINEL"));
            Assert.That(File.ReadAllText(file), Does.Contain("V9_CALIBRATOR_FAILED:INVALID_DATA"));
            Assert.That(File.ReadAllText(file), Does.Not.Contain("FIRST_ERROR_SENTINEL"));
            var latest = new IOException("No space left on device");
            Assert.DoesNotThrow((Action)(() => V9NestedCalibrationCli.TryWriteLastError(reservation, latest)));
            var content = File.ReadAllText(file);
            Assert.That(content, Does.Contain("V9_CALIBRATOR_FAILED:IO_ERROR"));
            Assert.That(content, Does.Not.Contain(latest.Message));
            Assert.That(content, Does.Not.Contain("FIRST_ERROR_SENTINEL"));
            Assert.That(new FileInfo(file).Length, Is.EqualTo(V9CalibradorLastErrorLog.ReservedBytes));
            Assert.That(Directory.GetFiles(output, "*.log"), Has.Length.EqualTo(1));
        }
        finally { Directory.Delete(output, true); }
    }

    [Test]
    public void Calibrador_error_larger_than_reserve_never_writes_sensitive_content()
    {
        var output = Path.Combine(Path.GetTempPath(), "v9-calibrador-truncate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            using var reservation = V9CalibradorLastErrorLog.Reserve(output);
            V9NestedCalibrationCli.TryWriteLastError(reservation,
                new IOException("No space left on device " + new string('é', 9000)));
            var file = Path.Combine(output, V9CalibradorLastErrorLog.FileName);
            var bytes = File.ReadAllBytes(file);
            Assert.That(bytes, Has.Length.EqualTo(V9CalibradorLastErrorLog.ReservedBytes));
            var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            Assert.That(text, Does.Contain("V9_CALIBRATOR_FAILED:IO_ERROR"));
            Assert.That(text, Does.Not.Contain("No space left on device"));
            Assert.That(text, Does.Not.Contain("é"));
        }
        finally { Directory.Delete(output, true); }
    }

    [Test]
    [NonParallelizable]
    public void Calibrador_never_emits_secret_or_personal_id_in_public_stderr_or_reserved_file()
    {
        const string canary = "SENHA_CANARY_2026_CPF_11122233344";
        var root = Path.Combine(Path.GetTempPath(), "v9-calibrador-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalStderr = Console.Error;
        using var stderr = new StringWriter();
        try
        {
            using var log = V9CalibradorLastErrorLog.Reserve(root);
            Console.SetError(stderr);
            V9NestedCalibrationCli.TryWriteLastError(log, new IOException(canary));
            Assert.That(stderr.ToString(), Does.Contain("V9_CALIBRATOR_FAILED:IO_ERROR"));
            Assert.That(stderr.ToString(), Does.Not.Contain(canary));
            var content = File.ReadAllText(Path.Combine(root, V9CalibradorLastErrorLog.FileName));
            Assert.That(content, Does.Not.Contain(canary));
            Assert.That(content, Does.Contain("V9_CALIBRATOR_FAILED:IO_ERROR"));
        }
        finally
        {
            Console.SetError(originalStderr);
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Calibrador_log_failure_does_not_mask_original_failure()
    {
        var output = Path.Combine(Path.GetTempPath(), "v9-calibrador-log-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            // Fail closed if the initial 10 KiB reservation cannot be created;
            // never try to start physical calibration without its error space.
            Directory.CreateDirectory(Path.Combine(output, V9CalibradorLastErrorLog.FileName));
            Assert.That((Action)(() => V9CalibradorLastErrorLog.Reserve(output)),
                Throws.TypeOf<IOException>());
            Directory.Delete(Path.Combine(output, V9CalibradorLastErrorLog.FileName));
            var reservation = V9CalibradorLastErrorLog.Reserve(output);
            reservation.Dispose(); // deterministic failure to write, no disk fill
            Assert.DoesNotThrow((Action)(() => V9NestedCalibrationCli.TryWriteLastError(
                reservation, new IOException("There is not enough space on the disk."))));
            Assert.DoesNotThrow((Action)(() => V9NestedCalibrationCli.TryWriteLastError(
                null, new InvalidDataException("bad input"))));
        }
        finally { Directory.Delete(output, true); }
    }

    [Test]
    public void Failed_calibration_does_not_mutate_a_preexisting_result_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "v9-calibrador-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var marker = Path.Combine(root, "historical.txt");
            File.WriteAllText(marker, "IMMUTABLE_PREVIOUS_RUN");
            var missingPlan = Path.Combine(root, "nonexistent-plan.json");
            Assert.That((Func<Task>)(async () => await V9NestedCalibrationCli.RunAsync(
                ["--v9-calibrate", missingPlan, "missing.csv", root])),
                Throws.TypeOf<FileNotFoundException>());
            Assert.That(File.ReadAllText(marker), Is.EqualTo("IMMUTABLE_PREVIOUS_RUN"));
            Assert.That(File.Exists(Path.Combine(root, "calibrador.log")), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Disk_telemetry_records_temporary_growth_and_removal_without_touching_inputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "v9-disk-diagnostic-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "v9-disk-unscoped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            await using var sampler = new V9CalibrationDiskSampler(root, TimeSpan.FromMilliseconds(50));
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            var transient = Path.Combine(nested, "transient.dat");
            File.WriteAllBytes(transient, new byte[65536]);
            sampler.ObserveNow();
            var atPeak = sampler.Snapshot();
            Assert.That(atPeak.Status, Is.EqualTo("MEASURED_SAMPLED"));
            Assert.That(atPeak.PeakLogicalBytes, Is.GreaterThanOrEqualTo(65536));
            File.Delete(transient);
            File.WriteAllBytes(Path.Combine(outside, "not-calibration.dat"), new byte[65536]);
            var after = sampler.Snapshot();
            Assert.That(after.CurrentLogicalBytes, Is.LessThan(after.PeakLogicalBytes));
            Assert.That(after.PeakLogicalBytes, Is.EqualTo(atPeak.PeakLogicalBytes));
            Assert.That(after.Exclusions, Does.Contain("SQL_CONTAINER"));
            Assert.That(after.Exclusions, Does.Contain("PHYSICAL_ALLOCATED_BLOCKS"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public async Task Disk_telemetry_exposes_inaccessible_or_incomplete_samples_as_not_measured()
    {
        var root = Path.Combine(Path.GetTempPath(), "v9-disk-fail-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var bad = new V9CalibrationDiskSampler(root, TimeSpan.FromSeconds(5),
                _ => throw new UnauthorizedAccessException("private path canary"));
            var missing = bad.Snapshot();
            Assert.That(missing.Status, Is.EqualTo("NOT_MEASURED"));
            Assert.That(missing.PeakLogicalBytes, Is.Null);
            Assert.That(missing.FailedSamples, Is.GreaterThan(0));
            var reads = 0;
            await using var partial = new V9CalibrationDiskSampler(root, TimeSpan.FromSeconds(5),
                _ => ++reads == 1 ? 100 : throw new IOException("private path canary"));
            var incomplete = partial.Snapshot();
            Assert.That(incomplete.Status, Is.EqualTo("PARTIAL_SAMPLED"));
            Assert.That(incomplete.PeakLogicalBytes, Is.EqualTo(100));
            Assert.That(incomplete.Note, Does.Not.Contain("private path canary"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Ram_reporting_uses_runtime_GC_pressure_without_inventing_a_memory_limit()
    {
        const long mib = 1024L * 1024;
        var calm = V9NestedCalibrationCli.InspectRamCapacity(
            320 * mib, 700 * mib, 150 * mib, 170 * mib, 240 * mib,
            4096 * mib, 2500 * mib, 3500 * mib);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(calm.GcReportsHighMemoryLoad, Is.False);
            Assert.That(calm.Notice, Is.Null);
            Assert.That(calm.PeakResidentWorkingSetBytes, Is.EqualTo(700 * mib));
            Assert.That(calm.GcTotalAvailableMemoryBytes, Is.EqualTo(4096 * mib));
            Assert.That(calm.BatchTrace, Is.EqualTo("memory-progress.jsonl"));
        }));

        var high = V9NestedCalibrationCli.InspectRamCapacity(
            320 * mib, 700 * mib, 150 * mib, 170 * mib, 240 * mib,
            4096 * mib, 3500 * mib, 3500 * mib);
        Assert.That(high.GcReportsHighMemoryLoad, Is.True);
        Assert.That(high.Notice, Is.EqualTo("RAM_CARGA_ALTA_INFORMADA_PELO_GC"));

        var unknown = V9NestedCalibrationCli.InspectRamCapacity(
            320 * mib, 700 * mib, 150 * mib, 170 * mib, 240 * mib,
            4096 * mib, 0, 0);
        Assert.That(unknown.GcReportsHighMemoryLoad, Is.Null);
        Assert.That(unknown.GcMemoryLoadBytes, Is.Null);
        Assert.That(unknown.Notice, Is.EqualTo("RAM_PRESSAO_NAO_DISPONIVEL_PELA_API_GC"));

        Assert.That((Action)(() => V9NestedCalibrationCli.InspectRamCapacity(
            -1, 0, 0, 0, 0, 0, 0, 0)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Search_reader_skips_external_holdout_attribute_values_and_never_opens_TEST()
    {
        var file = Path.Combine(Path.GetTempPath(), "jornada-train-only-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllLines(file, ["base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf",
                "A,TRAIN,G1,MARIA SILVA,ANA COSTA,1980-01-01,", "A,TRAIN,G2,MARIA SILVA,ANA COSTA,1980-01-01,",
                "B,TRAIN,G1,JOSE COSTA,ANA SILVA,1981-01-01,", "B,TRAIN,G2,JOSE COSTA,ANA SILVA,1981-01-01,",
                "C,VALIDATION,G1,DO NOT PARSE,DO NOT PARSE,INVALID_DATE,", "D,TEST,G1,DO NOT PARSE,DO NOT PARSE,INVALID_DATE,"]);
            var train = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(file, 4, onlyPartition: "TRAIN");
            Assert.That(train, Has.Length.EqualTo(4));
            Assert.That(train.All(x => x.Partition == "TRAIN"), Is.True);
            Assert.That((Action)(() => V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(file, 4)), Throws.TypeOf<FormatException>());
            Assert.That((Action)(() => V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(file, 4, onlyPartition: "TEST")), Throws.TypeOf<InvalidDataException>());
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void Fresh_manifest_binding_rejects_historical_same_seed_and_same_input_without_claiming_IID()
    {
        var csvSha = new string('b', 64);
        byte[] Manifest(ulong seed) => JsonSerializer.SerializeToUtf8Bytes(new {
            seed, generator_version = SyntheticObservationMultiplicityContract.GeneratorVersion,
            error_profile = "clean", population_profile = "DEMOGRAPHIC_PRIMARY_V1", input_fingerprint_sha256 = new string('a', 64),
            observation_multiplicity = new { contract_version = SyntheticObservationMultiplicityContract.Version,
                maximum_observations_per_base_identity = 4, scope = "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES",
                support = SyntheticObservationMultiplicityContract.Counts, weights = SyntheticObservationMultiplicityContract.Weights },
            outputs = new[] { new { path = "observacoes.csv", sha256 = csvSha } } });
        var fresh = V9FreshHoldoutProtocol.Inspect(Manifest(1000), csvSha, new string('c', 64), 42, 4);
        Assert.That(fresh.Scope, Does.Contain("NOT_IID_CERTIFICATION"));
        foreach (var seed in new ulong[] { 42, 43, 44, 1000, 20261019001 })
            Assert.That((Action)(() => V9FreshHoldoutProtocol.Inspect(Manifest(seed), csvSha, new string('c', 64), 1000, 4)),
                Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FreshHoldoutProtocol.Inspect(Manifest(1000), csvSha, csvSha, 42, 4)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FreshHoldoutProtocol.Inspect(Manifest(1000), new string('f', 64), new string('c', 64), 42, 4)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Internal_split_is_exact_reproducible_order_independent_and_never_accepts_external_holdouts()
    {
        var rows = Enumerable.Range(1, 20).SelectMany(i => new[] {
            new V9PhysicalCalibrationObservation(i * 2, "P" + i, "TRAIN", "G1", "MARIA SILVA", "ANA COSTA", new(1980, 1, 1)),
            new V9PhysicalCalibrationObservation(i * 2 + 1, "P" + i, "TRAIN", "G2", "MARIA SILVA", "ANA COSTA", new(1980, 1, 1)) }).ToArray();
        var a = V9InternalTrainingSplit.Create(rows, "seed");
        var b = V9InternalTrainingSplit.Create(rows.Reverse().ToArray(), "seed");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.Adjustment.Select(x => x.BasePersonId).Distinct().Count(), Is.EqualTo(14));
            Assert.That(a.Selection.Select(x => x.BasePersonId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(a.Adjustment.Select(x => x.BasePersonId).Intersect(a.Selection.Select(x => x.BasePersonId)), Is.Empty);
            Assert.That(JsonSerializer.Serialize(a.Manifest), Is.EqualTo(JsonSerializer.Serialize(b.Manifest)));
            Assert.That(a.Manifest.AssignmentSha256, Is.Not.EqualTo(V9InternalTrainingSplit.Create(rows, "new-seed").Manifest.AssignmentSha256));
        }));
        foreach (var partition in new[] { "VALIDATION", "TEST" })
            Assert.That((Action)(() => V9InternalTrainingSplit.Create(rows.Append(rows[0] with { ObservationId = 999, Partition = partition }).ToArray(), "seed")),
                Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Frontier_retains_tradeoffs_excludes_hard_rejections_and_finalists_preserve_epsilon_choice()
    {
        var safe = Candidate(160, 0, .90m);
        var balanced = Candidate(180, 1, .95m);
        var risky = Candidate(190, 5, .99m);
        var dominated = Candidate(150, 1, .80m);
        var rejected = Candidate(199, 0, .70m) with { Rejections = ["P95_ACIMA_DO_LIMITE"] };
        var frontier = V9TrainingParetoSelection.Frontier([safe, balanced, risky, dominated, rejected]);
        Assert.That(frontier.Select(x => x.Snapshot.ContentSha256), Is.EqualTo(new[] { safe, balanced, risky }.Select(x => x.Snapshot.ContentSha256)));
        var finalists = V9TrainingParetoSelection.Finalists(frontier, [.001, .005, .01, .05], .01);
        Assert.That(finalists[0].Snapshot.ContentSha256, Is.EqualTo(balanced.Snapshot.ContentSha256));
        Assert.That(finalists.Select(x => x.Snapshot.ContentSha256).Distinct().Count(), Is.EqualTo(3));
        Assert.That(V9TrainingParetoSelection.Finalists([safe], [.01], .01), Has.Count.EqualTo(1));
        Assert.That(V9TrainingParetoSelection.Finalists([], [.01], .01), Is.Empty);
        var duplicate = safe with { Snapshot = Candidate(160, 0, .91m).Snapshot };
        Assert.That(V9TrainingParetoSelection.Frontier([safe, duplicate]), Has.Count.EqualTo(1));
        Assert.That((Action)(() => V9TrainingParetoSelection.Frontier([safe with { Metric = safe.Metric with { GlobalSourceRecall = .99 } }])),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Zero_margin_is_measured_but_never_enters_frontier_or_finalists()
    {
        var safe = Candidate(160, 0, .90m);
        var zero = Candidate(200, 0, .95m) with
        {
            Metric = Candidate(200, 0, .95m).Metric with { ConflictMargin = 0m }
        };
        var frontier = V9TrainingParetoSelection.Frontier([safe, zero]);
        Assert.That(frontier, Has.Count.EqualTo(1));
        Assert.That(frontier[0].Metric.ConflictMargin, Is.EqualTo(.5m));
        Assert.That(V9TrainingParetoSelection.Finalists(frontier, [.01], .01), Has.Count.EqualTo(1));
        Assert.That((Action)(() => V9TrainingParetoSelection.Finalists([zero], [.01], .01)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Paired_ratio_bootstrap_is_reproducible_uses_all_identity_denominators_and_has_nonzero_resolution_at_zero_errors()
    {
        var a = Candidate(180, 0, .95m);
        var b = Candidate(179, 1, .96m);
        var first = V9TrainingParetoSelection.PairedBootstrapResolution(a, b, 200, 42, .0001, .00001);
        var second = V9TrainingParetoSelection.PairedBootstrapResolution(a, b, 200, 42, .0001, .00001);
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Recall, Is.GreaterThanOrEqualTo(1d / 200));
        Assert.That(first.Fdr, Is.GreaterThanOrEqualTo(1d / 180));
        var wrong = b with { Metric = b.Metric with { IdentityOutcomes = b.Metric.IdentityOutcomes!.Skip(1).ToArray() } };
        Assert.That((Action)(() => V9TrainingParetoSelection.PairedBootstrapResolution(a, wrong, 200, 42, .0001, .00001)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task Exposure_receipt_is_atomic_one_time_and_rejects_reusing_a_cohort_for_another_trio()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jornada-holdout-receipt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var declaration = new V9FreshHoldoutDeclaration(1000, new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64), "cohort:");
            await V9FreshHoldoutProtocol.ReserveOnceAsync(directory, declaration, new string('e', 64), [new string('f', 64)], CancellationToken.None);
            Assert.That((Func<Task>)(async () => await V9FreshHoldoutProtocol.ReserveOnceAsync(directory, declaration,
                new string('1', 64), [new string('2', 64)], CancellationToken.None)), Throws.TypeOf<IOException>());
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }
        finally
        {
            // Only this test's new, absolute, GUID-named temporary directory.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Nested_request_hash_distinguishes_full_option_fingerprint_not_only_alpha_threshold()
    {
        // The old request hash omitted TF, side and support policy, silently
        // collapsing distinct configurations into one physical evaluation.
        var type = typeof(V9NestedCalibrationCoordinator).GetNestedType(
            "Request", System.Reflection.BindingFlags.NonPublic)!;
        var sha = type.GetProperty("Sha",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public)!;
        var original = new V9FsAdaptiveOptionConfiguration("ROOT", .5m, .6m);
        var withTf = original with { TfWeight = .3m, TfSide = CompositeNominalTfSideV9.Mother };
        object Create(V9FsAdaptiveOptionConfiguration option) =>
            Activator.CreateInstance(type,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic, null,
                [Array.Empty<LinkageBlockingPass>(), option, .5m, "ROOT", false], null)!;

        Assert.That(sha.GetValue(Create(original)), Is.Not.EqualTo(sha.GetValue(Create(withTf))));
        Assert.That(sha.GetValue(Create(original)), Is.EqualTo(sha.GetValue(Create(original))));
        // A four-unit option must have a distinct frozen identity even when
        // the legacy TF field remains TF0.
        var withFourUnits = original with
        {
            UnitTfOptions = new[]
            {
                new V9FsUnitTfOption(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName,
                    .7m, CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly),
                new V9FsUnitTfOption(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname,
                    0m, CompositeNominalTfAdjustmentModeV9.Symmetric),
                new V9FsUnitTfOption(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName,
                    .2m, CompositeNominalTfAdjustmentModeV9.Symmetric),
                new V9FsUnitTfOption(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname,
                    0m, CompositeNominalTfAdjustmentModeV9.Symmetric)
            }
        };
        Assert.That(withFourUnits.TfWeight, Is.Zero); // Four independent weights, not legacy TF.
        Assert.That(sha.GetValue(Create(withFourUnits)), Is.Not.EqualTo(sha.GetValue(Create(original))));
        Assert.That(sha.GetValue(Create(withFourUnits)), Is.Not.EqualTo(sha.GetValue(Create(withTf))));

        // A refined ID must not consume a second physical evaluation when
        // the underlying configuration and root option are unchanged.
        var refine = typeof(V9NestedCalibrationCoordinator).GetMethod(
            "RefinedOption", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var sameConfigurationNewId = (V9FsAdaptiveOptionConfiguration)refine.Invoke(
            null, [original, "ROOT", original.DecisionThreshold])!;
        Assert.That(sha.GetValue(Create(original)), Is.EqualTo(sha.GetValue(Create(sameConfigurationNewId))));
    }

    [Test]
    public void Three_estimators_share_exact_SQL_frame_but_keep_independent_model_evaluations()
    {
        // Same TRAIN, blocking passes and margin must materialize SQL only once
        // for V2, V3 historical and experimental U, while each option keeps
        // an independent request SHA and model snapshot.
        var type = typeof(V9NestedCalibrationCoordinator).GetNestedType(
            "Request", System.Reflection.BindingFlags.NonPublic)!;
        var grouping = typeof(V9NestedCalibrationCoordinator).GetMethod(
            "PhysicalFrameKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var sha = type.GetProperty("Sha",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public)!;
        var v2 = new V9FsAdaptiveOptionConfiguration("TF0__MU_DEFAULT", .5m, .95m);
        var standard = new V9JointSupportPolicy(.99m, 5, 5,
            V9JointSupportEstimation.SupportedTailRule);
        var v3 = v2 with { OptionId = "TF0__MU_V3", JointSupportPolicy = standard };
        var mixZero = v2 with
        {
            OptionId = "TF0__MU_MIX0",
            JointSupportPolicy = standard with { UBackoffJointMixtureWeight = 0m }
        };
        var mixHalf = v2 with
        {
            OptionId = "TF0__MU_MIX05",
            JointSupportPolicy = standard with { UBackoffJointMixtureWeight = .5m }
        };

        object Request(V9FsAdaptiveOptionConfiguration option, decimal margin) =>
            Activator.CreateInstance(type,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic, null,
                [Array.Empty<LinkageBlockingPass>(), option, margin, option.OptionId, false], null)!;
        var requests = new[] { v2, v3, mixZero, mixHalf }
            .Select(o => Request(o, .5m)).ToArray();
        Assert.That(requests.Select(r => (string)grouping.Invoke(null, [r])!).Distinct().Count(),
            Is.EqualTo(1), "one SQL candidate materialization per passes + margin");
        Assert.That(requests.Select(r => (string)sha.GetValue(r)!).Distinct().Count(),
            Is.EqualTo(4), "all declared estimators must remain separately scored");
        Assert.That((string)grouping.Invoke(null, [Request(v2, .25m)])!,
            Is.Not.EqualTo((string)grouping.Invoke(null, [requests[0]])!),
            "different margins require different frame groups");
        Assert.That(standard.UBackoffJointMixtureWeight, Is.Null);
        Assert.That(mixZero.JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Zero);
    }

    [Test]
    public void Nested_refinement_preserves_tf_side_floor_and_support_without_hardcoded_TF0()
    {
        var method = typeof(V9NestedCalibrationCoordinator).GetMethod(
            "RefinedOption", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var original = new V9FsAdaptiveOptionConfiguration("ROOT", .5m, .6m,
            TfWeight: .25m, TfSide: CompositeNominalTfSideV9.Mother,
            TfMinimumFrequency: .00001m, TfMinimumIndependentSupport: 5);
        V9FsAdaptiveOptionConfiguration Refine(decimal threshold) =>
            (V9FsAdaptiveOptionConfiguration)method.Invoke(null, [original, "ROOT", threshold])!;

        var refined = Refine(.7m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(refined.DecisionThreshold, Is.EqualTo(.7m));
            Assert.That(refined.SmoothingAlpha, Is.EqualTo(original.SmoothingAlpha));
            Assert.That(refined.TfWeight, Is.EqualTo(original.TfWeight));
            Assert.That(refined.TfSide, Is.EqualTo(original.TfSide));
            Assert.That(refined.TfMinimumFrequency, Is.EqualTo(original.TfMinimumFrequency));
            Assert.That(refined.TfMinimumIndependentSupport, Is.EqualTo(original.TfMinimumIndependentSupport));
            Assert.That(refined.OptionId, Is.Not.EqualTo(original.OptionId));
            Assert.That(refined.OptionId, Is.EqualTo(Refine(.7m).OptionId));
            Assert.That(refined.OptionId, Is.Not.EqualTo(Refine(.8m).OptionId));
        }));
    }

    private static V9TrainingCandidate Candidate(long tp, long fp, decimal threshold)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Solution", "src"))) directory = directory.Parent;
        var bytes = File.ReadAllBytes(Path.Combine(directory!.FullName, "Solution/data/calibration/v9/bootstrap-v0/613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d.fitted.json"));
        var original = V9FittedSnapshot.Load(bytes, "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d");
        var snapshot = V9FittedSnapshot.Create(original.Payload with { Threshold = threshold });
        var support = V9FittedJointSupportAudit.Reconstruct(snapshot, .5m, 19896, 26378);
        var outcomes = new List<V9SourceIdentityOutcome>();
        var remainingTp = tp; var remainingFp = fp;
        for (var i = 0; i < 100; i++)
        {
            var t = Math.Min(2, remainingTp); remainingTp -= t;
            var f = Math.Min(2 - t, remainingFp); remainingFp -= f;
            outcomes.Add(new("P" + i, 2, t, f));
        }
        var metric = new V9PhysicalSourceResolveMetric(snapshot.ContentSha256, threshold, 0, .5m,
            "BLIND_FS_COMPLETE_LINK_CANDIDATES", 200, 200, 200, tp, fp, 0, 200 - tp - fp, 1, tp / 200d,
            fp / (double)(tp + fp), null, 100, new Dictionary<string, long>(), false, false, 0, IdentityOutcomes: outcomes);
        return new(snapshot, metric, support, 1, 0, []);
    }
}

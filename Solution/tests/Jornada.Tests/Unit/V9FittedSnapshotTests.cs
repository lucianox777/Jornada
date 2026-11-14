using System.Security.Cryptography;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FittedSnapshotTests
{
    private static V9FittedSnapshotPayload Payload()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var cells = levels.SelectMany(person => levels.Select(mother =>
            new FellegiSunterJointNominalCellV9(person, mother, 1m / 81m, 1m / 81m))).ToArray();
        return new V9FittedSnapshotPayload(V9FittedSnapshot.SchemaVersion,
            CompositeNominalComparisonV9.ContractVersion, "projection-v1", new string('a', 64),
            new string('b', 64), new string('c', 64), new string('d', 64),
            new string('e', 64), new string('f', 64), "adapter-v1", "test", .99m, .5m, 0m,
            [new LinkageBlockingPass("test", new[] { "NOME" })], cells,
            new Dictionary<string, decimal>
            {
                [LinkageParameterCatalog.PriorMatchProbability] = .01m,
                [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
                [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
                [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
                ["M_DATA_NASCIMENTO_EXACT"] = .9m,
                ["M_DATA_NASCIMENTO_DIFF"] = .1m,
                ["U_DATA_NASCIMENTO_EXACT"] = .1m,
                ["U_DATA_NASCIMENTO_DIFF"] = .9m
            });
    }

    [Test]
    public void Tf0_snapshot_and_four_explicit_baseline_units_have_bit_exact_fs_scores()
    {
        var historical = V9FittedSnapshot.Create(Payload());
        var zeroUnits = new[] { CompositeNominalTfSideV9.Person, CompositeNominalTfSideV9.Mother }
            .SelectMany(side => new[] { NominalFrequencyUnitV9.FirstName,
                NominalFrequencyUnitV9.LastContentSurname }
                .Select(unit => new V9SnapshotUnitTf(
                    side, unit, V9SnapshotTfFitState.BaselineTf0, null)))
            .ToArray();
        var explicitZero = V9FittedSnapshot.Create(
            Payload() with { UnitTfCalibrations = zeroUnits });
        // Compare all relevant nominal paths including absent mother and DOB,
        // without changing the fixed joint 81-cell m/u or the actual birth factor.
        var probes = new[]
        {
            ("MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA", (DateOnly?)new DateOnly(1980, 2, 3), (DateOnly?)new DateOnly(1980, 2, 3)),
            ("MARIA SILVA", "MARIA SOUZA", "ANA COSTA", "ANA LIMA", (DateOnly?)new DateOnly(1980, 2, 3), (DateOnly?)new DateOnly(1980, 2, 4)),
            ("MARIA SILVA", "JOANA SILVA", "ANA COSTA", "EVA COSTA", (DateOnly?)null, (DateOnly?)null),
            ("MARIA SILVA", "JOANA PEREIRA", (string?)null, (string?)null, (DateOnly?)null, (DateOnly?)null)
        };
        foreach (var (left, right, leftMother, rightMother, birthA, birthB) in probes)
        {
            var observation = new IdentityObservation(null, "SEM_CPF", left, birthA, leftMother);
            var candidate = new V9SnapshotCandidate(Guid.NewGuid(), right, birthB, rightMother);
            var a = historical.RankPreview(observation, [candidate]).Single();
            var b = explicitZero.RankPreview(observation, [candidate]).Single();
            Assert.Multiple((Action)(() =>
            {
                Assert.That(BitConverter.DoubleToInt64Bits((double)a.LogOdds),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits((double)b.LogOdds)));
                Assert.That(a.LogOdds, Is.EqualTo(b.LogOdds));
                Assert.That(a.Score, Is.EqualTo(b.Score));
            }));
        }
        Assert.That(V9FittedSnapshot.Load(historical.SerializeUtf8(), historical.ContentSha256).ContentSha256,
            Is.EqualTo(historical.ContentSha256));
        // This is a bit-exact *score* regression. The historical 97.4754%
        // aggregate requires a separate replay on the original physical corpus.
    }

    [Test]
    public void Multiple_frozen_snapshots_do_not_retain_an_extra_serialized_JSON_buffer()
    {
        var fields = typeof(V9FittedSnapshot).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.That(fields.Any(f => f.FieldType == typeof(byte[])), Is.False,
            "Serialização canônica deve ser temporária; os fits TF da busca já mantêm estruturas congeladas na RAM.");

        var payload = Payload();
        var snapshots = Enumerable.Range(0, 12)
            .Select(index => V9FittedSnapshot.Create(
                payload with { Threshold = .80m + index * .01m })).ToArray();
        foreach (var snapshot in snapshots)
        {
            var first = snapshot.SerializeUtf8();
            var second = snapshot.SerializeUtf8();
            Assert.That(second, Is.EqualTo(first));
            Assert.That(Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant(),
                Is.EqualTo(snapshot.ContentSha256));
            second[0] = 0; // defensive-copy contract
            Assert.That(snapshot.SerializeUtf8(), Is.EqualTo(first));
            Assert.That(V9FittedSnapshot.Load(first, snapshot.ContentSha256).SerializeUtf8(),
                Is.EqualTo(first));
        }
    }

    [Test]
    public void Pinned_snapshot_roundtrips_and_runs_same_shared_resolve_without_retraining()
    {
        var snapshot = V9FittedSnapshot.Create(Payload());
        var loaded = V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria Silva", null, null);
        var model = Guid.NewGuid();
        var candidates = new[] { new V9SnapshotCandidate(Guid.NewGuid(), "Maria Silva", null, null) };
        var expected = ProbabilisticLinkageDecisions.ResolveCompositeNominalV9Preview(model,
            snapshot.Payload.Parameters, observation,
            candidates.Select(c => new LinkageCandidate(c.PersonUuid, c.FullName, c.BirthDate, c.MotherName)).ToArray(),
            snapshot.Payload.JointCells, snapshot.Payload.NominalContractVersion,
            snapshot.Payload.Threshold, snapshot.Payload.ConflictMargin);
        Assert.That(loaded.ResolvePreview(model, observation, candidates), Is.EqualTo(expected));
        Assert.That(loaded.ContentSha256, Is.EqualTo(snapshot.ContentSha256));
        Assert.That(loaded.ResolvePreview(model, observation, []).Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
    }

    [Test]
    public void Mutating_caller_owned_inputs_or_exported_bytes_cannot_change_frozen_model()
    {
        var payload = Payload();
        var snapshot = V9FittedSnapshot.Create(payload);
        ((Dictionary<string, decimal>)payload.Parameters)[LinkageParameterCatalog.PriorMatchProbability] = .5m;
        ((string[])payload.BlockingPasses[0].Fields)[0] = "CHANGED";
        var bytes = snapshot.SerializeUtf8();
        bytes[0] = 0;
        Assert.That(snapshot.Payload.Parameters[LinkageParameterCatalog.PriorMatchProbability], Is.EqualTo(.01m));
        Assert.That(snapshot.Payload.BlockingPasses[0].Fields[0], Is.EqualTo("NOME"));
        Assert.DoesNotThrow((Action)(() => V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256)));
    }

    [Test]
    public void Rejects_tampered_bytes_even_when_json_is_valid()
    {
        var snapshot = V9FittedSnapshot.Create(Payload());
        var different = V9FittedSnapshot.Create(Payload() with { Threshold = .98m });
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Load(different.SerializeUtf8(), snapshot.ContentSha256)));
    }

    [Test]
    public void Rejects_positive_tf_unknown_parameters_in_declared_birth_and_partition_reuse()
    {
        var payload = Payload();
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(payload with { TfWeight = 1m })));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(payload with
        {
            ValidationIdentityGroupsSha256 = payload.TrainIdentityGroupsSha256
        })));
        ((Dictionary<string, decimal>)payload.Parameters).Remove("U_DATA_NASCIMENTO_DIFF");
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(payload)));
    }

    [Test]
    public void Rejects_missing_cells_and_unrecognized_schema_even_with_matching_content_hash()
    {
        var payload = Payload();
        Assert.Throws<InvalidOperationException>((Action)(() => V9FittedSnapshot.Create(payload with
        {
            JointCells = payload.JointCells.Take(80).ToArray()
        })));
        var snapshot = V9FittedSnapshot.Create(payload);
        var altered = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(snapshot.SerializeUtf8())
            .Replace(V9FittedSnapshot.SchemaVersion, "future-unknown-schema", StringComparison.Ordinal));
        var sha = Convert.ToHexString(SHA256.HashData(altered));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Load(altered, sha)));
    }

    private static V9FittedSnapshotPayload PositiveTfPayload()
    {
        var p = Payload();
        var reference = new NominalFrequencyReferenceContractV9(NominalFrequencyUnitV9.FirstName,
            NominalFrequencySourceV9.IbgePublishedMarginal,
            "IBGE_PUBLISHED:test:" + new string('a', 64) + ":SP_3550308_TODOS",
            new string('c', 64), 0, false,
            new NominalSyntheticIbgeBootstrapV9(CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                30000, new string('a', 64), "GENERATOR_V1", 42, new string('b', 64)));
        return p with { TfWeight = 1m, BlockingFrameFingerprintSha256 = new string('c', 64),
            TfCalibration = new V9SnapshotNominalUnitTfCalibration(
            CompositeNominalTfSideV9.Person, reference,
            new Dictionary<string, decimal> { ["MARIA"] = .00001m },
            new Dictionary<string, decimal> { ["MARIA"] = .8m },
            new string('a', 64), .000001m, 1, p.TrainCorpusSha256,
            NominalUReference: V9NominalTfUReference.Fit(
                CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName,
                10, 2, .5m, p.TrainCorpusSha256)) };
    }

    private static V9FittedSnapshotPayload FourUnitTfPayload()
    {
        var p = PositiveTfPayload();
        var personFirst = p.TfCalibration! with
        {
            Weight = 1m,
            Frequencies = new Dictionary<string, decimal> { ["MARIA"] = .5m },
            TrainUnitUByValue = new Dictionary<string, decimal> { ["MARIA"] = .1m }
        };
        var motherFirst = personFirst with
        {
            Side = CompositeNominalTfSideV9.Mother,
            Reference = personFirst.Reference with
            {
                PopulationFrameFingerprint = "IBGE_PUBLISHED:test:" + new string('a', 64) + ":BR_FEMININO"
            },
            Frequencies = new Dictionary<string, decimal> { ["ANA"] = .5m },
            TrainUnitUByValue = new Dictionary<string, decimal> { ["ANA"] = .1m },
            NominalUReference = V9NominalTfUReference.Fit(
                CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName,
                10, 2, .5m, p.TrainCorpusSha256)
        };
        var personLast = personFirst with
        {
            Reference = personFirst.Reference with
            {
                Unit = NominalFrequencyUnitV9.LastContentSurname,
                Source = NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy
            },
            Frequencies = new Dictionary<string, decimal> { ["SILVA"] = .5m },
            TrainUnitUByValue = new Dictionary<string, decimal> { ["SILVA"] = .1m },
            NominalUReference = V9NominalTfUReference.Fit(
                CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname,
                10, 2, .5m, p.TrainCorpusSha256)
        };
        var motherLast = motherFirst with
        {
            Reference = motherFirst.Reference with
            {
                Unit = NominalFrequencyUnitV9.LastContentSurname,
                Source = NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy,
                PopulationFrameFingerprint = "IBGE_PUBLISHED:test:" + new string('a', 64) + ":BR_TODOS"
            },
            Frequencies = new Dictionary<string, decimal> { ["COSTA"] = .5m },
            TrainUnitUByValue = new Dictionary<string, decimal> { ["COSTA"] = .1m },
            NominalUReference = V9NominalTfUReference.Fit(
                CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname,
                10, 2, .5m, p.TrainCorpusSha256)
        };
        return p with
        {
            // Four independently active TF slots carry positive own weights;
            // legacy single-side TfWeight must remain zero in this model.
            TfWeight = 0m, TfCalibration = null, MotherTfCalibration = null,
            UnitTfCalibrations =
            [
                new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName,
                    V9SnapshotTfFitState.Active, personFirst),
                new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname,
                    V9SnapshotTfFitState.Active, personLast),
                new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName,
                    V9SnapshotTfFitState.Active, motherFirst),
                new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname,
                    V9SnapshotTfFitState.Active, motherLast)
            ]
        };
    }


    [Test]
    public void Gold_diagnostic_variant_preserves_four_unit_tf_and_original_fit_bytes()
    {
        // Only a V3, TRAIN-fitted, BLIND snapshot is eligible for a Gold
        // diagnostic. No TF, m/u, blocking pass, prior or threshold is refitted.
        var payload = FourUnitTfPayload();
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var observed = levels.SelectMany(person => levels.Select(mother =>
            new V9JointObservedCell(person, mother, 1, 1))).ToArray();
        var estimated = V9JointSupportEstimation.Estimate(observed,
            new V9JointSupportPolicy(.99m, 0, 0), 1m, payload.TrainCorpusSha256);
        var blind = V9FittedSnapshot.Create(payload with
        {
            SchemaVersion = V9FittedSnapshot.SupportSchemaVersion,
            JointCells = estimated.Probabilities,
            JointSupport = estimated.Provenance,
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        });

        var readiness = V9FittedSnapshotArtifact.DescribeGoldTfDiagnosticReadiness(blind);
        Assert.That(readiness.GetProperty("status").GetString(),
            Is.EqualTo("READY_FOR_ISOLATED_GOLD_TF_DIAGNOSTIC_ONLY"));
        Assert.That(readiness.GetProperty("goldVariantPreservesFrozenFit").GetBoolean(), Is.True);
        Assert.That(readiness.GetProperty("activeUnitTfCount").GetInt32(), Is.EqualTo(4));
        Assert.That(readiness.GetProperty("blockingPassCount").GetInt32(),
            Is.EqualTo(blind.Payload.BlockingPasses.Count));
        Assert.That(readiness.GetProperty("certified").GetBoolean(), Is.False);
        Assert.That(readiness.GetProperty("operationallyAuthorized").GetBoolean(), Is.False);
        Assert.That(readiness.GetProperty("physicalGoldExecuted").GetBoolean(), Is.False);

        var gold = blind.CreateGoldHarnessVariant();
        var reloaded = V9FittedSnapshot.Load(gold.SerializeUtf8(), gold.ContentSha256);
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, "ANA COSTA");
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA SILVA", null, "ANA COSTA");
        var before = blind.RankPreview(observation, [candidate]).Single();
        var after = reloaded.RankPreview(observation, [candidate]).Single();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(gold.ContentSha256, Is.Not.EqualTo(blind.ContentSha256));
            Assert.That(gold.Payload.GoldParentSnapshotSha256, Is.EqualTo(blind.ContentSha256));
            Assert.That(gold.Payload.CandidateGrouping!.CandidateGrouping, Is.EqualTo("GOLD"));
            Assert.That(gold.Payload.UnitTfCalibrations!.Count(x =>
                x.State == V9SnapshotTfFitState.Active), Is.EqualTo(4));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(gold.Payload with
            {
                CandidateGrouping = blind.Payload.CandidateGrouping,
                GoldParentSnapshotSha256 = null
            }), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(blind.Payload)));
            Assert.That(after.LogOdds, Is.EqualTo(before.LogOdds));
            Assert.That(after.Score, Is.EqualTo(before.Score));
            Assert.That(reloaded.ContentSha256, Is.EqualTo(gold.ContentSha256));
        }));

        var newUpper = blind.CreateGoldHarnessVariantAtUpper(.85m);
        Assert.That(newUpper.Payload.Threshold, Is.EqualTo(.85m));
        Assert.That(newUpper.Payload.UnitTfCalibrations!.Count(x =>
            x.State == V9SnapshotTfFitState.Active), Is.EqualTo(4));
        // Neither a V2 snapshot nor an already converted Gold fit is an input.
        var legacy = V9FittedSnapshot.Create(payload with
        {
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        });
        Assert.That(V9FittedSnapshotArtifact.DescribeGoldTfDiagnosticReadiness(legacy)
            .GetProperty("status").GetString(), Is.EqualTo("NO_V3_JOINT_SUPPORT"));
        Assert.That(V9FittedSnapshotArtifact.DescribeGoldTfDiagnosticReadiness(gold)
            .GetProperty("status").GetString(), Is.EqualTo("NOT_BLIND_FIT"));
        Assert.Throws<InvalidDataException>((Action)(() => legacy.CreateGoldHarnessVariant()));
        Assert.Throws<InvalidDataException>((Action)(() => gold.CreateGoldHarnessVariant()));
    }

    [Test]
    public void V3_gold_tf_runtime_builds_ordered_sql_candidate_union_without_refit()
    {
        // Pure synthetic V3 contract fixture: proves the physical runner's
        // pinned-fitted-snapshot *code path*, not a certified real Gold outcome.
        // The archived ONE_MF 8bdf08af... is V2 and MUST NOT be substituted.
        var payload = FourUnitTfPayload() with
        {
            ProjectionSchemaVersion = V9CalibrationBlockingProjection.SchemaVersion,
            ProjectionFingerprintSha256 = V9CalibrationBlockingProjection.ComputeSchemaFingerprint(4),
            BlockingPasses =
            [
                new("first-mother", ["name_prefix_3", "mother_name_prefix_3"]),
                new("person", ["name_prefix_4"])
            ],
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        };
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var observed = levels.SelectMany(person => levels.Select(mother =>
            new V9JointObservedCell(person, mother, 1, 1))).ToArray();
        var support = V9JointSupportEstimation.Estimate(observed,
            new V9JointSupportPolicy(.99m, 0, 0), 1m, payload.TrainCorpusSha256);
        var blind = V9FittedSnapshot.Create(payload with
        {
            SchemaVersion = V9FittedSnapshot.SupportSchemaVersion,
            JointCells = support.Probabilities,
            JointSupport = support.Provenance
        });
        var gold = blind.CreateGoldHarnessVariant();
        var runtime = V9FittedSnapshotExecutionRuntime.Load(gold.SerializeUtf8(),
            gold.ContentSha256);
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, "ANA COSTA");
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA SILVA", null, "ANA COSTA");
        var model = Guid.NewGuid();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand();
        var sql = runtime.BuildCandidateUuidQuery(cmd, observation,
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
        var frame = new V9RetrievedSnapshotCandidateFrame(
            gold.ContentSha256, runtime.ProjectionSchemaVersion,
            runtime.ProjectionFingerprintSha256, true, [candidate]);
        var actual = runtime.ResolveRetrievedCandidatesPreview(model, observation, frame);
        var expected = gold.ResolvePreview(model, observation, [candidate]);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(runtime.SnapshotSha256, Is.EqualTo(gold.ContentSha256));
            Assert.That(gold.Payload.GoldParentSnapshotSha256, Is.EqualTo(blind.ContentSha256));
            Assert.That(runtime.Plan(observation).Select(x => x.PassId),
                Is.EqualTo(new[] { "first-mother", "person" }));
            Assert.That(sql, Does.Contain(" INTERSECT ").And.Contain(" UNION "));
            Assert.That(sql, Does.Not.Contain("MARIA").And.Not.Contain("ANA"));
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(gold.Payload.UnitTfCalibrations!.Count(x =>
                x.State == V9SnapshotTfFitState.Active), Is.EqualTo(4));
            Assert.That(gold.Payload.BlockingPasses.Count, Is.EqualTo(2));
        }));

        Assert.Throws<InvalidDataException>((Action)(() =>
            runtime.ResolveRetrievedCandidatesPreview(model, observation,
                frame with { SnapshotSha256 = new string('0', 64) })));
    }

    [Test]
    public void Frozen_V2_TRAIN_support_can_form_distinct_unapproved_V3_trial_without_TF_reoptimization()
    {
        // This unit fixture is not the user's source fit. Physical TRAIN
        // counts must come from a separately SHA-pinned existing study.
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var counts = levels.SelectMany(p => levels.Select(m =>
            new V9JointObservedCell(p, m,
                p == CompositeNominalLevelV9.ExactFull ? 8L : 6L,
                m == CompositeNominalLevelV9.Other ? 9L : 6L))).ToArray();
        const decimal alpha = .5m;
        var totalM = counts.Sum(c => c.M);
        var totalU = counts.Sum(c => c.U);
        var source = FourUnitTfPayload();
        var probabilities = counts.Select(c => new FellegiSunterJointNominalCellV9(
            c.Person, c.Mother,
            (c.M + alpha) / (totalM + 81 * alpha),
            (c.U + alpha) / (totalU + 81 * alpha))).ToArray();
        var parameters = new Dictionary<string, decimal>(source.Parameters)
        {
            [LinkageParameterCatalog.PriorMatchProbability] =
                (totalM + alpha) / (totalM + totalU + 2 * alpha)
        };
        var blind = V9FittedSnapshot.Create(source with
        {
            JointCells = probabilities, Parameters = parameters,
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        });
        var receipt = V9FittedJointSupportAudit.Reconstruct(blind, alpha, totalM, totalU);
        var policy = new V9JointSupportPolicy(.99m, 5, 5,
            V9JointSupportEstimation.SupportedTailRule);
        var derived = V9FrozenJointTrainingDerivationCli.DeriveTrialCandidate(
            blind, receipt, policy);
        var gold = derived.CreateGoldHarnessVariant();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(blind.Payload.SchemaVersion, Is.EqualTo(V9FittedSnapshot.SchemaVersion));
            Assert.That(derived.Payload.SchemaVersion, Is.EqualTo(V9FittedSnapshot.SupportSchemaVersion));
            Assert.That(blind.ContentSha256, Is.Not.EqualTo(derived.ContentSha256));
            Assert.That(blind.Payload.FitFingerprintSha256,
                Is.Not.EqualTo(derived.Payload.FitFingerprintSha256));
            Assert.That(derived.Payload.Threshold, Is.EqualTo(blind.Payload.Threshold));
            Assert.That(derived.Payload.ConflictMargin, Is.EqualTo(blind.Payload.ConflictMargin));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(derived.Payload.BlockingPasses),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(blind.Payload.BlockingPasses)));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(derived.Payload.UnitTfCalibrations),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(blind.Payload.UnitTfCalibrations)));
            Assert.That(derived.Payload.JointSupport!.TotalM, Is.EqualTo(totalM));
            Assert.That(derived.Payload.JointSupport!.TotalU, Is.EqualTo(totalU));
            Assert.That(gold.Payload.GoldParentSnapshotSha256, Is.EqualTo(derived.ContentSha256));
            Assert.That(V9FittedSnapshot.Load(derived.SerializeUtf8(), derived.ContentSha256)
                .ContentSha256, Is.EqualTo(derived.ContentSha256));
        }));

        Assert.Throws<InvalidDataException>((Action)(() =>
            V9FrozenJointTrainingDerivationCli.DeriveTrialCandidate(
                blind, receipt with { TrainUPairs = totalU + 1 }, policy)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9FrozenJointTrainingDerivationCli.DeriveTrialCandidate(
                derived, receipt, policy)));
    }

    [Test]
    public void Frozen_independent_comparison_fails_closed_on_changed_passes_or_tf()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var counts = levels.SelectMany(p => levels.Select(m =>
            new V9JointObservedCell(p, m,
                p == CompositeNominalLevelV9.ExactFull ? 8L : 6L,
                m == CompositeNominalLevelV9.Other ? 9L : 6L))).ToArray();
        const decimal alpha = .5m;
        var nM = counts.Sum(c => c.M);
        var nU = counts.Sum(c => c.U);
        var basis = FourUnitTfPayload();
        var joint = counts.Select(c => new FellegiSunterJointNominalCellV9(
            c.Person, c.Mother, (c.M + alpha) / (nM + 81 * alpha),
            (c.U + alpha) / (nU + 81 * alpha))).ToArray();
        var parameters = new Dictionary<string, decimal>(basis.Parameters)
        {
            [LinkageParameterCatalog.PriorMatchProbability] =
                (nM + alpha) / (nM + nU + 2 * alpha)
        };
        // Pair validator is intentionally more restrictive than generic
        // synthetic four-slot tests: the archived ONE_MF has only one active TF.
        var v2 = V9FittedSnapshot.Create(basis with
        {
            JointCells = joint, Parameters = parameters,
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        });
        var d = V9FrozenJointTrainingDerivationCli.DeriveTrialCandidate(v2,
            V9FittedJointSupportAudit.Reconstruct(v2, alpha, nM, nU),
            new V9JointSupportPolicy(.99m, 5, 5, V9JointSupportEstimation.SupportedTailRule));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9FrozenJointIndependentComparisonCli.ValidateFrozenPair(v2, d)));
    }

    [Test]
    public void Frozen_inspection_does_not_confuse_four_active_tf_units_with_legacy_tf_zero()
    {
        var fit = V9FittedSnapshot.Create(FourUnitTfPayload());
        var info = V9FittedSnapshotArtifact.DescribeFrozenConfiguration(fit);
        var passes = info.GetProperty("blockingPasses");
        var units = info.GetProperty("unitTf").EnumerateArray().ToArray();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(info.GetProperty("ContentSha256").GetString(), Is.EqualTo(fit.ContentSha256));
            Assert.That(info.GetProperty("TfWeight").GetDecimal(), Is.EqualTo(0m));
            Assert.That(info.GetProperty("activeUnitTfCount").GetInt32(), Is.EqualTo(4));
            Assert.That(info.GetProperty("blockingPassCount").GetInt32(), Is.EqualTo(1));
            Assert.That(passes.GetArrayLength(), Is.EqualTo(1));
            Assert.That(passes[0].GetProperty("ordinal").GetInt32(), Is.EqualTo(1));
            Assert.That(passes[0].GetProperty("fields")[0].GetString(), Is.EqualTo("NOME"));
            Assert.That(info.GetProperty("jointCellCount").GetInt32(), Is.EqualTo(81));
            Assert.That(units.Select(x => x.GetProperty("side").GetString()), Is.EquivalentTo(
                new[] { "Person", "Person", "Mother", "Mother" }));
            Assert.That(units.Select(x => x.GetProperty("state").GetString()),
                Is.All.EqualTo("Active"));
            Assert.That(units.Select(x => x.GetProperty("weight").GetDecimal()),
                Is.All.EqualTo(1m));
            Assert.That(info.GetProperty("approved").GetBoolean(), Is.False);
            Assert.That(info.GetProperty("operationalModelCreated").GetBoolean(), Is.False);
        }));
        var zero = V9FittedSnapshotArtifact.DescribeFrozenConfiguration(
            V9FittedSnapshot.Create(Payload()));
        Assert.That(zero.GetProperty("activeUnitTfCount").GetInt32(), Is.Zero);
        Assert.That(zero.GetProperty("unitTf").ValueKind,
            Is.EqualTo(System.Text.Json.JsonValueKind.Null));
    }

    [Test]
    public void Four_unit_tf_reuses_one_frozen_scorer_for_first_full_last_and_both_sides()
    {
        var fitted = V9FittedSnapshot.Create(FourUnitTfPayload());
        var restored = V9FittedSnapshot.Load(fitted.SerializeUtf8(), fitted.ContentSha256);
        var neutral = V9FittedSnapshot.Create(Payload());
        decimal Score(V9FittedSnapshot snapshot, string personLeft, string personRight,
            string? motherLeft = null, string? motherRight = null)
        {
            var observation = new IdentityObservation(null, "SEM_CPF", personLeft, null, motherLeft);
            var candidate = new V9SnapshotCandidate(Guid.NewGuid(), personRight, null, motherRight);
            return snapshot.RankPreview(observation, [candidate]).Single().LogOdds;
        }
        foreach (var pair in new[]
        {
            ("MARIA SILVA", "MARIA SOUZA", (string?)null, (string?)null), // ExactFirst
            ("MARIA SILVA", "MARIA SILVA", (string?)null, (string?)null), // ExactFull proxy
            ("JOANA SILVA", "BEATRIZ SILVA", (string?)null, (string?)null), // ExactLast
            ("JOANA LIMA", "BEATRIZ SOUZA", (string?)"ANA COSTA", (string?)"ANA LIMA"), // Mother ExactFirst
            ("JOANA LIMA", "BEATRIZ SOUZA", (string?)"ANA COSTA", (string?)"BEATRIZ COSTA") // Mother ExactLast
        })
        {
            Assert.That(Score(fitted, pair.Item1, pair.Item2, pair.Item3, pair.Item4),
                Is.LessThan(Score(neutral, pair.Item1, pair.Item2, pair.Item3, pair.Item4)));
            Assert.That(Score(restored, pair.Item1, pair.Item2, pair.Item3, pair.Item4),
                Is.EqualTo(Score(fitted, pair.Item1, pair.Item2, pair.Item3, pair.Item4)));
        }
        Assert.That(restored.ContentSha256, Is.EqualTo(fitted.ContentSha256));
    }

    [Test]
    public void Four_unit_tf_preserves_chosen_zero_and_rejects_missing_active_surname()
    {
        var p = FourUnitTfPayload();
        var units = p.UnitTfCalibrations!.ToArray();
        units[1] = units[1] with
        {
            State = V9SnapshotTfFitState.ZeroChosen,
            Calibration = units[1].Calibration! with { Weight = 0m }
        };
        var zero = V9FittedSnapshot.Create(p with { UnitTfCalibrations = units });
        var neutral = V9FittedSnapshot.Create(Payload());
        var observation = new IdentityObservation(null, "SEM_CPF", "JOANA SILVA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "BEATRIZ SILVA", null, null);
        Assert.That(zero.RankPreview(observation, [candidate]).Single().LogOdds,
            Is.EqualTo(neutral.RankPreview(observation, [candidate]).Single().LogOdds));
        Assert.That(V9FittedSnapshot.Load(zero.SerializeUtf8(), zero.ContentSha256)
            .Payload.UnitTfCalibrations![1].State, Is.EqualTo(V9SnapshotTfFitState.ZeroChosen));
        units[1] = units[1] with
        {
            State = V9SnapshotTfFitState.Active,
            Calibration = units[1].Calibration! with { Weight = 1m }
        };
        var active = V9FittedSnapshot.Create(p with { UnitTfCalibrations = units });
        Assert.Throws<InvalidDataException>((Action)(() => active.RankPreview(
            new IdentityObservation(null, "SEM_CPF", "JOANA COSTA", null, null),
            [candidate with { FullName = "BEATRIZ COSTA" }])));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            UnitTfCalibrations = [units[0], units[0]]
        })));
    }


    [Test]
    public void Shared_last_surname_mode_roundtrips_without_doubling_same_surname_tf()
    {
        var baseline = FourUnitTfPayload();
        var units = baseline.UnitTfCalibrations!.ToArray();
        units[3] = units[3] with
        {
            Calibration = units[3].Calibration! with
            {
                Frequencies = new Dictionary<string, decimal> { ["SILVA"] = .5m },
                TrainUnitUByValue = new Dictionary<string, decimal> { ["SILVA"] = .1m }
            }
        };
        var revised = baseline with { UnitTfCalibrations = units };
        var bilateral = V9FittedSnapshot.Create(revised);
        var single = V9FittedSnapshot.Create(revised with
        {
            SharedSurnameMode = V9SharedSurnameModeV9.PersonSideOnlyWhenShared
        });
        var roundtrip = V9FittedSnapshot.Load(single.SerializeUtf8(), single.ContentSha256);
        var observation = new IdentityObservation(null, "SEM_CPF",
            "JOANA SILVA", null, "ANA SILVA");
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(),
            "BEATRIZ SILVA", null, "EVA SILVA");
        var bilateralScore = bilateral.RankPreview(observation, [candidate]).Single().LogOdds;
        var singleScore = roundtrip.RankPreview(observation, [candidate]).Single().LogOdds;
        Assert.That(roundtrip.Payload.SharedSurnameMode,
            Is.EqualTo(V9SharedSurnameModeV9.PersonSideOnlyWhenShared));
        Assert.That(singleScore, Is.GreaterThan(bilateralScore));
        // Decision 19: the suppressed duplicate is the mother's frozen
        // binary nominal-event U_ref, NOT the obsolete name-specific U=.1.
        // The synthetic fixture has 2 agreeing nonmatches among 10 with
        // alpha=.5, so the independent expectation is (2+.5)/(10+2*.5).
        const double fixtureMotherReferenceU = (2d + .5d) / (10d + 2d * .5d);
        Assert.That(units[3].Calibration!.NominalUReference!.Probability,
            Is.EqualTo((decimal)fixtureMotherReferenceU).Within(.000000000001m));
        Assert.That(singleScore - bilateralScore,
            Is.EqualTo((decimal)Math.Abs(Math.Log(fixtureMotherReferenceU / .5d)))
                .Within(.00000002m));
        Assert.That((Action)(() => V9FittedSnapshot.Create(revised with
        {
            SharedSurnameMode = (V9SharedSurnameModeV9)123
        })), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void External_validation_rejects_positive_tf_with_foreign_published_digest_before_exposure()
    {
        var positive = V9FittedSnapshot.Create(PositiveTfPayload());
        var bootstrap = positive.Payload.TfCalibration!.Reference.SyntheticBootstrap!;
        var plan = new V9NestedCalibrationPlan(null!, "fixture", 1, 1, 100, 42, 1, 1, .1m, .1m,
            PinnedIbgeReferenceVersionId: 7, PinnedIbgePublishedSha256: new string('a', 64),
            PinnedIbgeBootstrap: bootstrap);
        Assert.DoesNotThrow((Action)(() => V9NestedCalibrationCli.RequirePinnedFinalistReference(plan, positive)));
        // The published marginal SHA alone does not pin synthetic generator provenance.
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgeBootstrap = bootstrap with { GeneratorSeed = 43UL } }, positive)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgeBootstrap = null }, positive)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgePublishedSha256 = new string('b', 64) }, positive)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgeReferenceVersionId = null }, positive)));
        Assert.DoesNotThrow((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgePublishedSha256 = null, PinnedIbgeReferenceVersionId = null },
                V9FittedSnapshot.Create(Payload()))));
        var four = V9FittedSnapshot.Create(FourUnitTfPayload());
        Assert.DoesNotThrow((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(plan, four)));
        Assert.That(four.Payload.TfWeight, Is.Zero);
        Assert.That(four.Payload.UnitTfCalibrations!.Count(x =>
            x.State == V9SnapshotTfFitState.Active), Is.EqualTo(4));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgePublishedSha256 = null }, four)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgeBootstrap = bootstrap with { GeneratorSeed = 44UL } }, four)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9NestedCalibrationCli.RequirePinnedFinalistReference(
                plan with { PinnedIbgeReferenceVersionId = null }, four)));
    }

    [Test]
    public void Frozen_tf_asymmetric_penalizes_common_but_never_rewards_rare_first_names()
    {
        var payload = PositiveTfPayload();
        var neutral = V9FittedSnapshot.Create(Payload());
        var symmetric = V9FittedSnapshot.Create(payload);
        var asymmetric = V9FittedSnapshot.Create(payload with
        {
            TfCalibration = payload.TfCalibration! with
            {
                Mode = CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly
            }
        });
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA COSTA", null, null);
        decimal Score(V9FittedSnapshot snapshot) =>
            snapshot.RankPreview(observation, [candidate]).Single().LogOdds;

        // Rare MARIA: the symmetric factor is positive, the asymmetric one is
        // exactly neutral. TF0 and asymmetric must share the scoring kernel.
        Assert.That(Score(symmetric), Is.GreaterThan(Score(neutral)));
        Assert.That(Score(asymmetric), Is.EqualTo(Score(neutral)));
        var restored = V9FittedSnapshot.Load(asymmetric.SerializeUtf8(), asymmetric.ContentSha256);
        Assert.That(Score(restored), Is.EqualTo(Score(asymmetric)));
        Assert.That(restored.ContentSha256, Is.EqualTo(asymmetric.ContentSha256));

        // Common MARIA: both modes apply the same negative adjustment.
        var frequent = payload with
        {
            TfCalibration = payload.TfCalibration! with
            {
                Frequencies = new Dictionary<string, decimal> { ["MARIA"] = .9m }
            }
        };
        var frequentSymmetric = V9FittedSnapshot.Create(frequent);
        var frequentAsymmetric = V9FittedSnapshot.Create(frequent with
        {
            TfCalibration = frequent.TfCalibration! with
            {
                Mode = CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly
            }
        });
        Assert.That(Score(frequentSymmetric), Is.LessThan(Score(neutral)));
        Assert.That(Score(frequentAsymmetric), Is.EqualTo(Score(frequentSymmetric)));

        // Unknown modes are not silently treated as symmetric.
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(payload with
        {
            TfCalibration = payload.TfCalibration! with
            {
                Mode = (CompositeNominalTfAdjustmentModeV9)99
            }
        })));
    }

    [Test]
    public void Frozen_bilateral_tf_uses_independent_person_and_mother_weights_and_roundtrips()
    {
        var p = PositiveTfPayload();
        var motherReference = p.TfCalibration!.Reference with
        {
            PopulationFrameFingerprint = "IBGE_PUBLISHED:test:" + new string('a', 64) + ":BR_FEMININO"
        };
        var mother = p.TfCalibration with
        {
            Side = CompositeNominalTfSideV9.Mother,
            Reference = motherReference,
            Frequencies = new Dictionary<string, decimal> { ["ANA"] = .9m },
            TrainUnitUByValue = new Dictionary<string, decimal> { ["ANA"] = .8m },
            Weight = .75m,
            NominalUReference = V9NominalTfUReference.Fit(
                CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName,
                10, 2, .5m, p.TrainCorpusSha256),
            Mode = CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly
        };
        var single = V9FittedSnapshot.Create(p);
        var dual = V9FittedSnapshot.Create(p with { MotherTfCalibration = mother });
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, "ANA COSTA");
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA SILVA", null, "ANA COSTA");
        decimal Score(V9FittedSnapshot snapshot) =>
            snapshot.RankPreview(observation, [candidate]).Single().LogOdds;
        Assert.That(Score(dual), Is.LessThan(Score(single)));
        var loaded = V9FittedSnapshot.Load(dual.SerializeUtf8(), dual.ContentSha256);
        Assert.That(loaded.ContentSha256, Is.EqualTo(dual.ContentSha256));
        Assert.That(Score(loaded), Is.EqualTo(Score(dual)));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            MotherTfCalibration = mother with { Side = CompositeNominalTfSideV9.Person }
        })));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            MotherTfCalibration = mother with { Weight = null }
        })));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            MotherTfCalibration = mother with { Mode = (CompositeNominalTfAdjustmentModeV9)99 }
        })));
        var unknown = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, "JOANA COSTA");
        var unknownCandidate = candidate with { MotherName = "JOANA COSTA" };
        Assert.Throws<InvalidDataException>((Action)(() =>
            dual.RankPreview(unknown, [unknownCandidate])));
    }

    [Test]
    public void Unseen_tf_is_frozen_and_roundtrips_while_missing_IBGE_remains_rejected()
    {
        var p = PositiveTfPayload();
        var calibration = p.TfCalibration!;
        var published = new Dictionary<string, decimal>(calibration.Frequencies)
        {
            ["JOANA"] = .15m, ["ZULEICA"] = .05m
        };
        var trainU = new Dictionary<string, decimal> { ["MARIA"] = 2.5m / 3m };
        var reference = V9NominalTfUReference.Fit(
            CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName,
            10, 2, .5m, p.TrainCorpusSha256);
        var snap = V9FittedSnapshot.Create(p with
        {
            TfCalibration = calibration with
            {
                Frequencies = published,
                TrainUnitUByValue = trainU,
                NominalUReference = reference
            }
        });
        var loaded = V9FittedSnapshot.Load(snap.SerializeUtf8(), snap.ContentSha256);
        var observation = new IdentityObservation(null, "SEM_CPF", "JOANA SILVA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "JOANA COSTA", null, null);
        var first = snap.RankPreview(observation, [candidate]).Single();
        var second = loaded.RankPreview(observation, [candidate]).Single();
        Assert.That(first.LogOdds, Is.EqualTo(second.LogOdds));
        Assert.That(first.Score, Is.EqualTo(second.Score));
        var missing = new IdentityObservation(null, "SEM_CPF", "NAOFONTE SILVA", null, null);
        var missingCandidate = candidate with { FullName = "NAOFONTE COSTA" };
        Assert.Throws<InvalidDataException>((Action)(() => snap.RankPreview(missing, [missingCandidate])));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            TfCalibration = calibration with
            {
                Frequencies = published,
                TrainUnitUByValue = trainU,
                NominalUReference = reference with { Probability = .8m }
            }
        })));
    }

    [Test]
    public void Frozen_decision19_rejects_legacy_residual_and_incompatible_train_event_contract()
    {
        var payload = PositiveTfPayload();
        var tf = payload.TfCalibration!;
        var reference = tf.NominalUReference!;
        void Reject(V9SnapshotNominalUnitTfCalibration calibration) =>
            Assert.Throws<InvalidDataException>((Action)(() =>
                V9FittedSnapshot.Create(payload with { TfCalibration = calibration })));

        // Even if a caller computes a new whole-snapshot hash, a legacy
        // U(x) or IBGE-proportional residual policy is not a valid U_ref.
        Reject(tf with { NominalUReference = null });
        Reject(tf with { UnseenAllocation = new V9UnseenTfUAllocation(
            V9UnseenTfUAllocation.LegacyPolicyVersion, .1m, .2m) });
        Reject(tf with { NominalUReference = reference with {
            Side = CompositeNominalTfSideV9.Mother } });
        Reject(tf with { NominalUReference = reference with {
            Unit = NominalFrequencyUnitV9.LastContentSurname } });
        Reject(tf with { NominalUReference = reference with {
            TrainCorpusFingerprintSha256 = payload.ValidationCorpusSha256 } });
        Reject(tf with { NominalUReference = reference with {
            MatchingNominalPairs = reference.MatchingNominalPairs + 1 } });
        Reject(tf with { NominalUReference = reference with {
            PolicyVersion = "UNKNOWN_U_POLICY" } });
    }

    [TestCase(0d, CompositeNominalTfAdjustmentModeV9.Symmetric)]
    [TestCase(0d, CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)]
    [TestCase(.5d, CompositeNominalTfAdjustmentModeV9.Symmetric)]
    [TestCase(.5d, CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)]
    [TestCase(1d, CompositeNominalTfAdjustmentModeV9.Symmetric)]
    [TestCase(1d, CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)]
    public void TRAIN_event_U_ref_scores_six_weight_mode_combinations_with_independent_scalar_oracle(
        double weight, CompositeNominalTfAdjustmentModeV9 mode)
    {
        var neutral = V9FittedSnapshot.Create(Payload());
        var baseTf = PositiveTfPayload();
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA COSTA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA LIMA", null, null);
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeCompleto,
            candidate.FullName), Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        var baseScore = neutral.RankPreview(observation, [candidate]).Single().LogOdds;
        foreach (var frequency in new[] { .6m, .01m })
        {
            var tf = baseTf.TfCalibration! with
            {
                Frequencies = new Dictionary<string, decimal> { ["MARIA"] = frequency },
                Mode = mode
            };
            var p = weight == 0d ? Payload() : baseTf with
            {
                TfWeight = (decimal)weight,
                TfCalibration = tf
            };
            var fitted = V9FittedSnapshot.Create(p);
            var roundtrip = V9FittedSnapshot.Load(fitted.SerializeUtf8(), fitted.ContentSha256);
            var scored = roundtrip.RankPreview(observation, [candidate]).Single().LogOdds;
            var expectedRef = (2.5d / 11d); // 2 agreements / 10 U pairs, alpha=.5
            if (weight > 0d)
                Assert.That(roundtrip.Payload.TfCalibration!.NominalUReference!.Probability,
                    Is.EqualTo((decimal)expectedRef).Within(.000000000001m));
            var adjustment = weight * Math.Log(expectedRef / (double)frequency);
            if (mode == CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly)
                adjustment = Math.Min(0d, adjustment);
            var expected = baseScore + (decimal)adjustment;
            Assert.That(scored, Is.EqualTo(expected).Within(.00000005m),
                $"TF score mismatch for weight={weight}, mode={mode}, IBGE={frequency}");
            Assert.That(roundtrip.RankPreview(observation, [candidate]).Single().LogOdds,
                Is.EqualTo(scored));
        }
    }

    [Test]
    public void Positive_tf_rejects_other_train_frame_and_person_mother_reference_swap()
    {
        var p = PositiveTfPayload();
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            TfCalibration = p.TfCalibration! with { TrainCorpusFingerprintSha256 = p.ValidationCorpusSha256 }
        })));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            TfCalibration = p.TfCalibration! with { Side = CompositeNominalTfSideV9.Mother }
        })));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with
        {
            BlockingFrameFingerprintSha256 = new string('f', 64)
        })));
    }

    [Test]
    public void Positive_tf_unknown_applicable_term_fails_closed_for_full_exact_too()
    {
        var snapshot = V9FittedSnapshot.Create(PositiveTfPayload());
        var observation = new IdentityObservation(null, "SEM_CPF", "JOANA SILVA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "JOANA COSTA", null, null);
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeCompleto, candidate.FullName),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        Assert.Throws<InvalidDataException>((Action)(() => snapshot.ResolvePreview(Guid.NewGuid(), observation, [candidate])));
        candidate = candidate with { FullName = observation.NomeCompleto };
        Assert.Throws<InvalidDataException>((Action)(() =>
            snapshot.ResolvePreview(Guid.NewGuid(), observation, [candidate])));
    }

    [Test]
    public void Snapshot_rejects_undeclared_birth_model_before_any_record_is_scored()
    {
        var p = Payload();
        var parameters = p.Parameters.Where(x => !x.Key.Contains("NASCIMENTO", StringComparison.Ordinal)
                && x.Key != LinkageParameterCatalog.BirthSingleEvidenceScoring)
            .ToDictionary(x => x.Key, x => x.Value);
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with { Parameters = parameters })));
        parameters["M_DATA_NASCIMENTO_EXACT"] = .9m;
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Create(p with { Parameters = parameters })));
    }

    [TestCase(false, false, .01)]
    [TestCase(true, false, .01)]
    [TestCase(false, true, .01)]
    [TestCase(true, true, .0833333333333333)]
    public void Configured_birth_model_scores_actual_candidate_and_missing_input_DOB_is_neutral(
        bool sourceHasDob, bool candidateHasDob, double expectedPosterior)
    {
        var snapshot = V9FittedSnapshot.Create(Payload());
        var birth = new DateOnly(1980, 5, 6);
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA",
            sourceHasDob ? birth : null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "MARIA SILVA",
            candidateHasDob ? birth : null, null);
        var decision = snapshot.ResolvePreview(Guid.NewGuid(), observation, [candidate]);
        // All nominal cells in this fixture have M=U, so their Bayes factor
        // is one. Prior .01 changes to 1/12 only when both DOBs are observed
        // and equal, via the calibrated birth Bayes factor .9/.1 = 9.
        Assert.That(decision.MelhorCandidatoUuid, Is.EqualTo(candidate.PersonUuid));
        // The public scorer contract rounds posterior to eight decimal places.
        // Compare that exact published value, not an unrounded posterior with
        // a looser tolerance that could conceal an incorrect birth factor.
        var publishedPosterior = Math.Round((decimal)expectedPosterior, 8, MidpointRounding.AwayFromZero);
        Assert.That(decision.MelhorScore, Is.EqualTo(publishedPosterior));
        Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
    }

    [TestCase("NaN")]
    [TestCase("1e1000")]
    public void Nonfinite_or_unrepresentable_numeric_json_cannot_be_loaded_even_with_matching_pin(string number)
    {
        var snapshot = V9FittedSnapshot.Create(Payload());
        var original = System.Text.Encoding.UTF8.GetString(snapshot.SerializeUtf8());
        var altered = original.Replace("\"Threshold\":0.99", "\"Threshold\":" + number, StringComparison.Ordinal);
        Assert.That(altered, Is.Not.EqualTo(original));
        var bytes = System.Text.Encoding.UTF8.GetBytes(altered);
        var pin = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Throws<InvalidDataException>((Action)(() => V9FittedSnapshot.Load(bytes, pin)));
    }

    [Test]
    public async Task Artifact_create_is_exclusive_and_does_not_replace_existing_bytes()
    {
        using var directory = new TemporaryArtifactDirectory();
        var snapshot = V9FittedSnapshot.Create(Payload());
        var path = Path.Combine(directory.Path, "fit.json");
        await V9FittedSnapshotArtifact.WriteNewAsync(path, snapshot);
        var original = await File.ReadAllBytesAsync(path);
        var other = V9FittedSnapshot.Create(Payload() with { Threshold = .98m });
        Assert.ThrowsAsync<IOException>((Func<Task>)(async () => await V9FittedSnapshotArtifact.WriteNewAsync(path, other)));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        Assert.That((await V9FittedSnapshotArtifact.ReadPinnedAsync(path, snapshot.ContentSha256)).ContentSha256,
            Is.EqualTo(snapshot.ContentSha256));
    }

    [Test]
    public async Task Artifact_read_rejects_wrong_pin_and_modified_content()
    {
        using var directory = new TemporaryArtifactDirectory();
        var snapshot = V9FittedSnapshot.Create(Payload());
        var path = Path.Combine(directory.Path, "fit.json");
        await V9FittedSnapshotArtifact.WriteNewAsync(path, snapshot);
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9FittedSnapshotArtifact.ReadPinnedAsync(path, new string('0', 64))));
        await File.AppendAllTextAsync(path, " ");
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9FittedSnapshotArtifact.ReadPinnedAsync(path, snapshot.ContentSha256)));
    }

    [Test]
    public async Task Concurrent_artifact_creators_have_one_winner_and_complete_pinned_bytes()
    {
        using var directory = new TemporaryArtifactDirectory();
        var path = Path.Combine(directory.Path, "fit.json");
        var first = V9FittedSnapshot.Create(Payload());
        var second = V9FittedSnapshot.Create(Payload() with { Threshold = .98m });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Write(V9FittedSnapshot value)
        {
            await start.Task;
            try { await V9FittedSnapshotArtifact.WriteNewAsync(path, value); return true; }
            catch (IOException) { return false; }
        }
        var writes = new[] { Write(first), Write(second) };
        start.SetResult();
        var outcomes = await Task.WhenAll(writes);
        Assert.That(outcomes.Count(x => x), Is.EqualTo(1));
        var winner = outcomes[0] ? first : second;
        var loaded = await V9FittedSnapshotArtifact.ReadPinnedAsync(path, winner.ContentSha256);
        Assert.That(loaded.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));
    }

    [Test]
    public async Task Checked_in_experimental_v0_reloads_exact_measured_bytes_without_training_or_approval()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(System.IO.Path.Combine(directory.FullName, "Solution", "src")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var root = System.IO.Path.Combine(directory!.FullName, "Solution", "data", "calibration", "v9", "bootstrap-v0");
        const string sha = "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d";
        var loaded = await V9FittedSnapshotArtifact.ReadPinnedAsync(System.IO.Path.Combine(root, sha + ".fitted.json"), sha);
        var support = V9FittedJointSupportAudit.Reconstruct(loaded, .5m, 19896, 26378);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(support.Cells, Has.Count.EqualTo(81));
            Assert.That(support.CellsWithM, Is.EqualTo(31));
            Assert.That(support.CellsWithU, Is.EqualTo(25));
            Assert.That(support.CellsWithBoth, Is.EqualTo(18));
            Assert.That(support.CellsWithNeither, Is.EqualTo(43));
            Assert.That(support.IndependentIdentitySupportEstablished, Is.False);
            Assert.That(support.Cells.Sum(x => x.ObservedMPairs), Is.EqualTo(19896));
            Assert.That(support.Cells.Sum(x => x.ObservedUPairs), Is.EqualTo(26378));
        }));
        Assert.That((Action)(() => V9FittedJointSupportAudit.Reconstruct(loaded, .1m, 19896, 26378)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FittedJointSupportAudit.Reconstruct(loaded, .5m, 19897, 26378)), Throws.TypeOf<InvalidDataException>());
        using var metadata = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(
            System.IO.Path.Combine(root, "experimental-reference.json")));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(loaded.Payload.Threshold, Is.EqualTo(.95m));
            Assert.That(loaded.Payload.ConflictMargin, Is.EqualTo(.5m));
            Assert.That(loaded.Payload.TfWeight, Is.Zero);
            Assert.That(loaded.Payload.JointCells.Count, Is.EqualTo(81));
            Assert.That(loaded.Payload.CandidateGrouping!.MaximumObservationsPerIdentity, Is.EqualTo(16),
                "O cap de população 4 não deve reescrever o agrupamento do fit histórico.");
            Assert.That(metadata.RootElement.GetProperty("snapshot_sha256").GetString(), Is.EqualTo(loaded.ContentSha256));
            Assert.That(metadata.RootElement.GetProperty("source_measurement").GetProperty("TrueAcceptedSourceObservations").GetInt32(), Is.EqualTo(7505));
            Assert.That(metadata.RootElement.GetProperty("operationally_certified").GetBoolean(), Is.False);
            Assert.That(metadata.RootElement.GetProperty("test_used").GetBoolean(), Is.False);
            Assert.That(metadata.RootElement.GetProperty("incremental_runner_gold_evaluated").GetBoolean(), Is.False);
        }));
    }

    private static V9FittedSnapshot CompleteTf4OperationalWinner()
    {
        var payload = FourUnitTfPayload() with
        {
            CandidateGrouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)
        };
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var observed = levels.SelectMany(person => levels.Select(mother =>
            new V9JointObservedCell(person, mother, 1, 1))).ToArray();
        var estimated = V9JointSupportEstimation.Estimate(observed,
            new V9JointSupportPolicy(.99m, 0, 0), 1m, payload.TrainCorpusSha256);
        return V9FittedSnapshot.Create(payload with
        {
            SchemaVersion = V9FittedSnapshot.SupportSchemaVersion,
            JointCells = estimated.Probabilities,
            JointSupport = estimated.Provenance
        });
    }

    [Test]
    public void Operational_SQL_converter_preserves_ALL_four_TF_slots_81_joint_cells_and_original_canonical_bytes()
    {
        var winner = CompleteTf4OperationalWinner();
        var receipt = V9OperationalSqlModelConverter.VerifyConversion(winner);
        var restored = V9FittedSnapshot.Load(winner.SerializeUtf8(), receipt.SourceSnapshotSha256);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(receipt.ContractVersion,
                Is.EqualTo(V9OperationalSqlModelConverter.ContractVersion));
            Assert.That(receipt.ModelId,
                Is.EqualTo(V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256)));
            Assert.That(receipt.SourceSnapshotSha256, Is.EqualTo(winner.ContentSha256));
            Assert.That(receipt.JointCellCount, Is.EqualTo(81));
            Assert.That(receipt.TfSlotCount, Is.EqualTo(4));
            Assert.That(receipt.BlockingPassCount, Is.EqualTo(winner.Payload.BlockingPasses.Count));
            Assert.That(receipt.ByteExactRoundTrip, Is.True);
            Assert.That(receipt.Active, Is.False);
            Assert.That(receipt.Published, Is.False);
            Assert.That(restored.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));
            Assert.That(restored.Payload.UnitTfCalibrations!.Select(s => s.State),
                Is.EquivalentTo(winner.Payload.UnitTfCalibrations!.Select(s => s.State)));
            Assert.That(restored.Payload.JointCells, Is.EquivalentTo(winner.Payload.JointCells));
        }));
    }

    [Test]
    public void Operational_SQL_converter_rejects_legacy_or_partial_winners_and_never_fills_in_missing_TF()
    {
        var winner = CompleteTf4OperationalWinner();
        Assert.That((Action)(() => V9OperationalSqlModelConverter.VerifyConversion(
            V9FittedSnapshot.Create(Payload()))), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9OperationalSqlModelConverter.VerifyConversion(
            V9FittedSnapshot.Create(winner.Payload with { UnitTfCalibrations = null }))),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9OperationalSqlModelConverter.VerifyConversion(
            V9FittedSnapshot.Create(winner.Payload with { UnitTfCalibrations =
                winner.Payload.UnitTfCalibrations!.Take(3).ToArray() }))),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9OperationalSqlModelConverter.VerifyConversion(
            winner.CreateGoldHarnessVariant())),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9OperationalSqlModelConverter.ModelIdFromWinnerSha(
            new string('A', 64))), Throws.TypeOf<InvalidDataException>());
        var alteredThreshold = V9FittedSnapshot.Create(winner.Payload with
        {
            Threshold = .8m
        });
        Assert.That(V9OperationalSqlModelConverter.ModelIdFromWinnerSha(alteredThreshold.ContentSha256),
            Is.Not.EqualTo(V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256)));
    }

    [Test, Category("SQLSmoke")]
    public async Task Operational_SQL_roundtrip_uses_immutable_one_winner_row_and_same_frozen_FS_decisions()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_V9_SQL_MODEL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("SQL físico de smoke somente quando declarado explicitamente.");
            return;
        }
        var sqlConfig = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        if (sqlConfig.InitialCatalog != "JornadaHarness")
            throw new InvalidDataException("SQL smoke exige banco descartável JornadaHarness.");
        var winner = CompleteTf4OperationalWinner();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        var receipt = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        var loaded = await V9OperationalSqlModelConverter.ReadPinnedAsync(
            connection, receipt.ModelId, receipt.SourceSnapshotSha256);
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, "ANA COSTA");
        var candidates = new[]
        {
            new V9SnapshotCandidate(Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "MARIA SILVA", null, "ANA COSTA"),
            new V9SnapshotCandidate(Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "MARIA SOUZA", null, "ANA LIMA")
        };
        var original = winner.RankPreview(observation, candidates).ToArray();
        var converted = loaded.RankPreview(observation, candidates).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(receipt.Active, Is.False);
            Assert.That(receipt.Published, Is.False);
            Assert.That(loaded.ContentSha256, Is.EqualTo(winner.ContentSha256));
            Assert.That(loaded.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));
            Assert.That(converted, Is.EqualTo(original));
        }));
        // PRODUCTION-side and physical TEST paths now invoke the *same*
        // SQL-pinned grouped cohort FS/Resolve implementation. This SQL smoke
        // checks decisions and group identities (not only byte identity).
        var groupedRuntime = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, receipt.ModelId, receipt.SourceSnapshotSha256);
        var cohort = new[]
        {
            new V9PhysicalCalibrationObservation(1, "TRUTH-1", "TEST", "G1",
                "MARIA SILVA", "ANA COSTA", new DateOnly(1980, 5, 6)),
            new V9PhysicalCalibrationObservation(2, "TRUTH-1", "TEST", "G2",
                "MARIA SILVA", "ANA COSTA", new DateOnly(1980, 5, 6)),
            new V9PhysicalCalibrationObservation(3, "TRUTH-2", "TEST", "G3",
                "MARIA SILVA", "ANA COSTA", new DateOnly(1980, 5, 6)),
            new V9PhysicalCalibrationObservation(4, "TRUTH-2", "TEST", "G4",
                "MARIA SILVA", "ANA COSTA", new DateOnly(1980, 5, 6))
        };
        var completeEdges = new (int A, int B)[]
        {
            (1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4)
        };
        var referenceReplay = V9FrozenSourceResolveReplay.ReplayTest(
            winner, cohort, completeEdges);
        var sqlRunnerReplay = groupedRuntime.ReplayTest(cohort, completeEdges);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(groupedRuntime.ModelId, Is.EqualTo(receipt.ModelId));
            Assert.That(groupedRuntime.WinnerSha256, Is.EqualTo(winner.ContentSha256));
            Assert.That(sqlRunnerReplay.SnapshotSha256, Is.EqualTo(winner.ContentSha256));
            Assert.That(sqlRunnerReplay.Decisions, Is.EqualTo(referenceReplay.Decisions));
            Assert.That(sqlRunnerReplay.IdentityByObservation,
                Is.EquivalentTo(referenceReplay.IdentityByObservation));
        }));
        Assert.That((Action)(() => groupedRuntime.ReplayTest(
            cohort.Select(r => r with { Partition = "VALIDATION" }).ToArray(), completeEdges)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => groupedRuntime.ReplayTest(
            cohort, [completeEdges[0], completeEdges[0]])),
            Throws.TypeOf<InvalidDataException>());
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
                connection, receipt.ModelId, new string('a', 64))));

        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner)));
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9OperationalSqlModelConverter.ReadPinnedAsync(
                connection, receipt.ModelId, new string('a', 64))));
    }

    private sealed class TemporaryArtifactDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "jornada-v9-artifact-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryArtifactDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

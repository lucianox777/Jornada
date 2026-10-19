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
            TfCalibration = new V9SnapshotFirstNameTf(
            CompositeNominalTfSideV9.Person, reference,
            new Dictionary<string, decimal> { ["MARIA"] = .00001m },
            new Dictionary<string, decimal> { ["MARIA"] = .8m },
            new string('a', 64), .000001m, 1, p.TrainCorpusSha256) };
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
    public void Positive_tf_unknown_applicable_term_throws_while_nonapplicable_full_exact_keeps_real_score()
    {
        var snapshot = V9FittedSnapshot.Create(PositiveTfPayload());
        var observation = new IdentityObservation(null, "SEM_CPF", "JOANA SILVA", null, null);
        var candidate = new V9SnapshotCandidate(Guid.NewGuid(), "JOANA COSTA", null, null);
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeCompleto, candidate.FullName),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        Assert.Throws<InvalidDataException>((Action)(() => snapshot.ResolvePreview(Guid.NewGuid(), observation, [candidate])));
        var id = Guid.NewGuid();
        candidate = candidate with { FullName = observation.NomeCompleto };
        var actual = snapshot.ResolvePreview(id, observation, [candidate]);
        var expected = ProbabilisticLinkageDecisions.ResolveCompositeNominalV9Preview(id,
            snapshot.Payload.Parameters, observation,
            [new LinkageCandidate(candidate.PersonUuid, candidate.FullName, null, null)], snapshot.Payload.JointCells,
            snapshot.Payload.NominalContractVersion, snapshot.Payload.Threshold, snapshot.Payload.ConflictMargin);
        Assert.That(actual, Is.EqualTo(expected));
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

    private sealed class TemporaryArtifactDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "jornada-v9-artifact-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryArtifactDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

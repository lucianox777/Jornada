using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9NestedCalibrationTests
{
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
        foreach (var seed in new ulong[] { 42, 43, 44, 1000 })
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

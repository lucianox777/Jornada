using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationFreezeServiceTests
{
    private string directory = null!;
    private static readonly DateTimeOffset Created = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static string Hash(char value) => new(value, 64);
    private static void Reject(Func<Task> action) => Assert.ThrowsAsync<InvalidDataException>(action);

    [SetUp]
    public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "jornada-v9-freeze-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        // Only the test's explicitly created temporary directory; no application data.
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static V9FittedSnapshot Snapshot(decimal threshold = .99m)
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        return V9FittedSnapshot.Create(new V9FittedSnapshotPayload(V9FittedSnapshot.SchemaVersion,
            CompositeNominalComparisonV9.ContractVersion, "projection-v1", Hash('a'), Hash('b'),
            Hash('c'), Hash('d'), Hash('e'), Hash('f'), "adapter-v1", "option", threshold, .5m, 0m,
            [new LinkageBlockingPass("pass", ["NOME"])],
            levels.SelectMany(p => levels.Select(m => new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray(),
            new Dictionary<string, decimal>
            {
                [LinkageParameterCatalog.PriorMatchProbability] = .01m,
                [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
                [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
                [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
                ["M_DATA_NASCIMENTO_EXACT"] = .9m, ["M_DATA_NASCIMENTO_DIFF"] = .1m,
                ["U_DATA_NASCIMENTO_EXACT"] = .1m, ["U_DATA_NASCIMENTO_DIFF"] = .9m
            }, CandidateGrouping: new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16)));
    }

    private static string ConfigurationId => V9ParetoSourceSelection.ConfigurationId("option", "BLIND_FS_COMPLETE_LINK_CANDIDATES", .5m);

    [Test]
    public void Effective_grouping_is_hashed_and_legacy_snapshot_cannot_be_frozen()
    {
        var original = Snapshot();
        var posterior = V9FittedSnapshot.Create(original.Payload with
        {
            CandidateGrouping = original.Payload.CandidateGrouping! with { MinimumPairPosterior = .9m }
        });
        var size = V9FittedSnapshot.Create(original.Payload with
        {
            CandidateGrouping = original.Payload.CandidateGrouping! with { MaximumObservationsPerIdentity = 8 }
        });
        Assert.That(new[] { original.ContentSha256, posterior.ContentSha256, size.ContentSha256 }, Is.Unique);
        var legacy = V9FittedSnapshot.Create(original.Payload with { CandidateGrouping = null });
        Assert.That(System.Text.Encoding.UTF8.GetString(legacy.SerializeUtf8()), Does.Not.Contain("CandidateGrouping"));
        Assert.That(V9FittedSnapshot.Load(legacy.SerializeUtf8(), legacy.ContentSha256).Payload.CandidateGrouping, Is.Null);
        Reject(async () => await V9CalibrationFreezeService.FreezeAsync(directory, legacy,
            Metadata(Selection(snapshot: legacy)), Selection(snapshot: legacy)));
        Reject(async () => await V9CalibrationFreezeService.FreezeAsync(directory, original,
            Metadata(Selection()) with { Grouping = new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .9m, 16) }, Selection()));
    }

    private static V9BootstrapPareto2D.Result Selection(bool physical = true, V9FittedSnapshot? snapshot = null) =>
        V9BootstrapPareto2D.Evaluate([new V9BootstrapPareto2D.CandidateOption(
            ConfigurationId, (snapshot ?? Snapshot()).ContentSha256, .9, 0, 12, 900, 0, 1000, 900, physical, true, .004, 1)],
            new V9BootstrapPareto2D.SelectionPolicy(.8, .01, 50, 60000));

    private static V9CalibrationValidationEvidence Evidence() => new(
        900, 0, 1000, 100, 900, .9m, 0m, .004m, .8m, 1m,
        "identity-cluster-upper-bound-fixture", Hash('1'), Hash('2'), Hash('3'), 12m);

    private static V9CalibrationFreezeMetadata Metadata(V9BootstrapPareto2D.Result selection,
        string version = "v1", string? predecessor = null, string origin = V9CalibrationFreezeService.BootstrapOrigin) => new(
        version, predecessor, origin, "unit-test-fixture", Created, selection.Version,
        V9CalibrationFreezeService.FingerprintSelection(selection), PersonResolutionAttributeCatalog.CatalogVersion,
        PersonResolutionAttributeCatalog.All.Select(a => new V9CalibrationCatalogEntry(a.CanonicalCode,
            a.Semantic.ToString(), a.EligibleForResolution, a.MultiValued, a.CompatibilityProfile)).ToArray(),
        new V9CalibrationFreezePolicy("fixture-policy-v1", 60000, .01m, 50, "RankResolve-abstain-on-conflict", .8m), Evidence(),
        ConfigurationId, new("BLIND_FS_COMPLETE_LINK_CANDIDATES", .99m, 16));

    private Task<V9FrozenCalibration?> Freeze(string version = "v1", string? predecessor = null,
        string origin = V9CalibrationFreezeService.BootstrapOrigin, decimal threshold = .99m) =>
        V9CalibrationFreezeService.FreezeAsync(directory, Snapshot(threshold),
            Metadata(Selection(snapshot: Snapshot(threshold)), version, predecessor, origin), Selection(snapshot: Snapshot(threshold)));

    [Test]
    public void Matching_envelope_and_pareto_cannot_relax_the_source_selector_risk_limit()
    {
        var selection = Selection();
        var weaker = V9BootstrapPareto2D.Evaluate(selection.Table.Select(x => x.Option),
            selection.Policy with { MaximumAcceptedFdrUpper = .05 });
        var metadata = Metadata(weaker);
        metadata = metadata with { Policy = metadata.Policy with { MaximumFdrUpperBound = .05m } };
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9CalibrationFreezeService.FreezeAsync(directory, Snapshot(), metadata, weaker)));
        Assert.That(error!.Message, Does.Contain("teto de FDR de 1%"));
        Assert.That(Directory.Exists(directory), Is.False);
    }

    [Test]
    public async Task Bootstrap_is_immutable_idempotent_and_loads_same_shared_fit()
    {
        var first = (await Freeze())!;
        var repeated = (await Freeze())!;
        Assert.That(first.EnvelopeSha256, Is.EqualTo(repeated.EnvelopeSha256));
        var loaded = await V9CalibrationFreezeService.ReadPinnedAsync(directory, "v1", first.EnvelopeSha256);
        Assert.That(loaded.FittedSnapshot.ContentSha256, Is.EqualTo(first.FittedSnapshot.ContentSha256));
        Assert.That(loaded.Envelope.Metadata.Catalog, Has.Count.EqualTo(PersonResolutionAttributeCatalog.All.Count));
        Assert.That(Directory.GetFiles(directory, "*.envelope.json"), Has.Length.EqualTo(1));
        Reject(async () => await Freeze(threshold: .98m));
        Assert.That((await V9CalibrationFreezeService.ReadPinnedAsync(directory, "v1", first.EnvelopeSha256))
            .FittedSnapshot.Payload.Threshold, Is.EqualTo(.99m));
    }

    [Test]
    public async Task Parallel_identical_bootstraps_commit_only_one_snapshot()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Freeze()));
        Assert.That(results.Select(r => r!.EnvelopeSha256).Distinct().Count(), Is.EqualTo(1));
        Assert.That(Directory.GetFiles(directory, "*.envelope.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Later_freeze_requires_manual_selection_and_current_predecessor()
    {
        var initial = (await Freeze())!;
        Reject(async () => await Freeze("v2", initial.EnvelopeSha256));
        Reject(async () => await Freeze("v2", Hash('9'), V9CalibrationFreezeService.ManualOrigin));
        var next = (await Freeze("v2", initial.EnvelopeSha256, V9CalibrationFreezeService.ManualOrigin, .98m))!;
        Assert.That(next.Envelope.Metadata.PredecessorEnvelopeSha256, Is.EqualTo(initial.EnvelopeSha256));
        Assert.That(initial.FittedSnapshot.Payload.Threshold, Is.EqualTo(.99m));
        Reject(async () => await Freeze("v3", initial.EnvelopeSha256, V9CalibrationFreezeService.ManualOrigin));
    }

    [Test]
    public async Task Insufficient_or_fabricated_selection_produces_no_freeze()
    {
        var selection = Selection(false);
        var forged = selection with { InitialSuggestedOptionId = "option", Table = selection.Table.Select(e => e with { Eligible = true, OnFrontier = true }).ToArray() };
        var result = await V9CalibrationFreezeService.FreezeAsync(directory, Snapshot(), Metadata(selection), forged);
        Assert.That(result, Is.Null);
        Assert.That(Directory.Exists(directory), Is.False);
    }

    [Test]
    public void Rejects_mismatched_catalog_metrics_fit_and_policy()
    {
        var selection = Selection();
        var metadata = Metadata(selection);
        foreach (var invalid in new[]
                 {
                     metadata with { Catalog = metadata.Catalog.Skip(1).ToArray() },
                     metadata with { Validation = metadata.Validation with { FalsePositive = 1 } },
                     metadata with { Policy = metadata.Policy with { MaximumP95Milliseconds = 60001 } },
                     metadata with { SelectionReportSha256 = Hash('9') },
                     metadata with { Version = "../unsafe" },
                     metadata with { SelectedConfigurationId = "option" },
                     metadata with { Grouping = metadata.Grouping with { MinimumPairPosterior = 1m } }
                 })
            Reject(async () => await V9CalibrationFreezeService.FreezeAsync(directory, Snapshot(), invalid, selection));
        var different = V9FittedSnapshot.Create(Snapshot().Payload with { FitFingerprintSha256 = Hash('7') });
        Reject(async () => await V9CalibrationFreezeService.FreezeAsync(directory, different, metadata, selection));
        Assert.That(Snapshot(.98m).Payload.FitFingerprintSha256, Is.EqualTo(Snapshot().Payload.FitFingerprintSha256));
        Reject(async () =>
            await V9CalibrationFreezeService.FreezeAsync(directory, Snapshot(.98m), metadata, selection));
    }

    private static V9IndependentTestReservation Reservation(V9FrozenCalibration frozen, int seed = 45) => new(
        "study", "run", frozen.EnvelopeSha256, frozen.FittedSnapshot.ContentSha256,
        seed, Hash('4'), TestEvidence().IdentityGroupsSha256, Hash('1'), Created.AddMinutes(1), new(.05, 2, Hash('6'), Hash('7')));

    [TestCase(42), TestCase(43), TestCase(44)]
    public async Task Historical_test_seeds_cannot_certify_new_snapshot(int seed)
    {
        var frozen = (await Freeze())!;
        Reject(async () =>
            await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, Reservation(frozen, seed)));
    }

    [Test]
    public async Task Test_reservation_prevents_reruns_and_cross_version_test_reuse()
    {
        var frozen = (await Freeze())!;
        var reservation = Reservation(frozen);
        await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation);
        Reject(async () =>
            await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation));
        var next = (await Freeze("v2", frozen.EnvelopeSha256, V9CalibrationFreezeService.ManualOrigin))!;
        var reused = reservation with { StudyId = "different-study", EnvelopeSha256 = next.EnvelopeSha256 };
        Reject(async () =>
            await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, next, reused));
        var reroll = reused with
        {
            StudyId = "reroll", RunId = "reroll", Seed = 46, CorpusSha256 = Hash('8'), IdentityGroupsSha256 = Hash('9')
        };
        Reject(async () =>
            await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, next, reroll));
        var leaked = reservation with { StudyId = "leak", Seed = 46, CorpusSha256 = frozen.FittedSnapshot.Payload.TrainCorpusSha256 };
        Reject(async () =>
            await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, leaked));
    }

    [Test]
    public async Task Failed_test_receipt_is_immutable_and_cannot_be_relabeled_passed()
    {
        var frozen = (await Freeze())!;
        var reservation = Reservation(frozen);
        await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation);
        var evidence = TestEvidence(failure: true);
        var failure = new V9IndependentTestReceipt(reservation, V9CalibrationFreezeService.FingerprintTestEvidence(evidence),
            false, "RISK_BUDGET_FAILED", Created.AddMinutes(2), evidence);
        await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, failure);
        await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, failure);
        Reject(async () =>
            await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, failure with { Passed = true }));
        Reject(async () =>
            await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, failure with { Passed = true, Evidence = TestEvidence() }));
        Assert.That(Directory.GetFiles(Path.Combine(directory, "independent-tests"), "*.receipt.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Test_requires_reserved_dataset_recomputed_bounds_and_pinned_evidence()
    {
        var frozen = (await Freeze())!;
        var reservation = Reservation(frozen);
        await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation);
        var evidence = TestEvidence();
        var receipt = new V9IndependentTestReceipt(reservation, V9CalibrationFreezeService.FingerprintTestEvidence(evidence),
            true, "POLICY_MET_WITH_EXTERNALLY_ATTESTED_ASSUMPTIONS", Created.AddMinutes(2), evidence);
        foreach (var invalid in new[]
        {
            receipt with { EvidenceSha256 = Hash('9') },
            receipt with { Reservation = reservation with { FittedSnapshotSha256 = Hash('9') } },
            receipt with { Evidence = evidence with { CorpusSha256 = Hash('9') } },
            receipt with { Evidence = evidence with { Metrics = evidence.Metrics with { FdrUpperBound = 0m } } }
        })
            Reject(async () =>
                await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, invalid));
        await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, receipt);
    }

    [Test]
    public async Task Test_cannot_claim_pass_without_independent_sampling_and_physical_attestation()
    {
        var frozen = (await Freeze())!;
        var reservation = Reservation(frozen) with { Protocol = new(.05, 2, null, null) };
        await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation);
        var evidence = TestEvidence();
        var receipt = new V9IndependentTestReceipt(reservation, V9CalibrationFreezeService.FingerprintTestEvidence(evidence),
            true, "INSUFFICIENT_INDEPENDENCE", Created.AddMinutes(2), evidence);
        Reject(async () =>
            await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, receipt));
        await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, receipt with { Passed = false });
    }

    [Test]
    public async Task Test_with_no_accepts_is_recorded_inconclusive_without_fabricating_fdr_support()
    {
        var frozen = (await Freeze())!;
        var reservation = Reservation(frozen);
        await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, reservation);
        var original = TestEvidence();
        var evidence = original with
        {
            IdentityGroups = original.IdentityGroups.Select(g => g with { TrueAccepted = 0, FalseAccepted = 0 }).ToArray(),
            Metrics = original.Metrics with
            {
                TruePositive = 0, FalsePositive = 0, Abstentions = 1000,
                RecallGlobal = 0m, RecallLowerBound = 0m, Fdr = 0m, FdrUpperBound = 1m
            }
        };
        var receipt = new V9IndependentTestReceipt(reservation, V9CalibrationFreezeService.FingerprintTestEvidence(evidence),
            false, "INCONCLUSIVE_NO_ACCEPTED_IDENTITIES", Created.AddMinutes(2), evidence);
        await V9CalibrationFreezeService.CompleteIndependentTestAsync(directory, receipt);
    }

    [Test]
    public async Task Pinned_load_detects_tampering_and_preserves_complete_selection_evidence()
    {
        var frozen = (await Freeze())!;
        Assert.That(frozen.Envelope.Selection.Table.Single().Option.ConfigurationFingerprintSha256,
            Is.EqualTo(frozen.FittedSnapshot.ContentSha256));
        Assert.That(frozen.Envelope.Metadata.SelectedConfigurationId, Is.EqualTo(ConfigurationId));
        var path = Path.Combine(directory, "v1.fitted.json");
        await File.AppendAllTextAsync(path, " ");
        Reject(async () =>
            await V9CalibrationFreezeService.ReadPinnedAsync(directory, "v1", frozen.EnvelopeSha256));
    }


    private static V9IndependentTestEvidence TestEvidence(bool failure = false)
    {
        var groups = Enumerable.Range(0, 1000).Select(i => new V9IdentityClusterFdr.IdentityGroup(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"identity-{i}"))).ToLowerInvariant(),
            i < 900 ? 1 : 0, failure && i >= 900 ? 1 : 0)).ToArray();
        var upper = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(groups, 2, 1, .05);
        var fp = failure ? 100 : 0;
        return new(Hash('4'), V9CalibrationFreezeService.FingerprintTestIdentityGroups(groups.Select(g => g.BaseIdentityFingerprint)), new(900, fp, 1000, failure ? 0 : 100, groups.Length,
            .9m, (decimal)fp / (900 + fp), (decimal)(upper.UpperFdrConditional ?? 1), .8m, 1m,
            V9CalibrationFreezeService.IndependentTestStatisticalMethod, Hash('1'), Hash('2'), Hash('3'), 12m), groups);
    }

    [Test]
    public async Task Concurrent_test_attempts_grant_exactly_one_execution()
    {
        var frozen = (await Freeze())!;
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await V9CalibrationFreezeService.ReserveIndependentTestAsync(directory, frozen, Reservation(frozen));
                return true;
            }
            catch (InvalidDataException) { return false; }
        }));
        Assert.That(attempts.Count(a => a), Is.EqualTo(1));
    }
}

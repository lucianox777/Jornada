using Jornada.Linkage.Runner;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldIndependentHoldoutContractTests
{
    private const string LocalDb =
        @"Server=(localdb)\JornadaV9Gold20261025_abcdef12;Database=JornadaHarness;Integrated Security=true;";
    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static GoldTrainThreeOrderProtocol.FrozenGoldValidationCandidate Candidate() => new(
        "GOLD_V6_FROZEN_KNEE_VALIDATION_CANDIDATE_V1", 0.05m,
        Sha(1), Sha(2), Sha(3), Sha(4), 20261025001UL,
        9240, 5003, 28, 5, 6, 0.996, false, false, false, false);

    private static V9GoldIndependentHoldoutContract.CohortManifest Manifest() => new(
        "INDEPENDENT_HOLDOUT", "demographic-primary/clean", 20261025001UL,
        30000, Sha(5), Sha(6), Sha(2), false, false, false, false, false);

    private static void Reject(Action run) =>
        Assert.Throws<InvalidDataException>((System.Action)(() => run()));

    [Test]
    public void Fresh_declared_cohort_produces_six_trial_plan_without_certification()
    {
        var plan = V9GoldIndependentHoldoutContract.Prepare(Candidate(), Manifest(), Sha(5), LocalDb);
        Assert.Multiple((System.Action)(() =>
        {
            Assert.That(plan.EvidenceScope, Is.EqualTo("INDEPENDENT_HOLDOUT"));
            Assert.That(plan.RequiredPhysicalTrials, Is.EqualTo(6));
            Assert.That(plan.Policies, Is.EqualTo(new[] { "CURRENT", "KNEE" }));
            Assert.That(plan.OrderIds, Is.EqualTo(GoldTrainThreeOrderProtocol.OrderIds));
            Assert.That(plan.CandidateLowerThreshold, Is.EqualTo(0.05m));
            Assert.That(plan.IdentityNamespaceSha256, Has.Length.EqualTo(64));
            Assert.That(plan.TestUsed, Is.False);
            Assert.That(plan.IndependentlyCertified, Is.False);
            Assert.That(plan.OperationallyActivated, Is.False);
        }));
    }

    [Test]
    public void Rejects_train_csv_wrong_fit_reused_seed_and_reopened_test()
    {
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(), Manifest(), Sha(7), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { CsvSha256 = Sha(1) }, Sha(1), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { GoldFittedSnapshotSha256 = Sha(8) }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { Seed = 42UL }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { TestUsed = true }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { TrainingSelectionUsed = true }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { Refitted = true }, Sha(5), LocalDb));
    }

    [Test]
    public void Rejects_changed_population_scope_and_operational_sql()
    {
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { DeclaredPeople = 29999 }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { EvidenceScope = "TRAIN_SELECTION" }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { HistoricalValidationUsed = true }, Sha(5), LocalDb));
        Reject(() => V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { OperationallyActivated = true }, Sha(5), LocalDb));
        Assert.Throws<InvalidOperationException>((System.Action)(() =>
            V9GoldIndependentHoldoutContract.Prepare(Candidate(), Manifest(), Sha(5),
                "Server=prod;Database=Jornada;Integrated Security=true;")));
    }

    [Test]
    public void Namespace_depends_on_seed_and_cohort_fingerprint_not_reused_local_person_id()
    {
        var a = V9GoldIndependentHoldoutContract.Prepare(Candidate(), Manifest(), Sha(5), LocalDb);
        var b = V9GoldIndependentHoldoutContract.Prepare(Candidate(),
            Manifest() with { CohortFingerprintSha256 = Sha(9) }, Sha(5), LocalDb);
        Assert.That(a.IdentityNamespaceSha256, Is.Not.EqualTo(b.IdentityNamespaceSha256));
    }
    [Test]
    public async Task External_physical_cli_rejects_invalid_policy_or_operational_connection_before_reading_cohort()
    {
        var parameters = new[] {
            "--v9-gold-holdout-harness", "candidate.json", "new-cohort",
            "selection.json", "validation.json", "fit.json", "output", "BASELINE_G0_G1_G2_G3", "INVALID"
        };
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await V9GoldHarnessCommand.RunIndependentHoldoutAsync(parameters, LocalDb)));

        parameters[8] = "CURRENT";
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () =>
            await V9GoldHarnessCommand.RunIndependentHoldoutAsync(parameters,
                "Server=production;Database=JornadaLocal;Integrated Security=true;")));
    }

    [Test]
    public void Frozen_candidate_manifest_is_both_external_and_non_operational()
    {
        // The actual V6 evidence is a distinct new JSON input pinned under
        // data/calibration. A holdout cannot invent a knee in SQL or reopen TEST.
        var a = Candidate();
        var plan = V9GoldIndependentHoldoutContract.Prepare(a, Manifest(), Sha(5), LocalDb);
        Assert.Multiple((System.Action)(() =>
        {
            Assert.That(plan.ContractVersion, Is.EqualTo(V9GoldIndependentHoldoutContract.Version));
            Assert.That(plan.Seed, Is.EqualTo(20261025001UL));
            Assert.That(plan.EvidenceScope, Is.EqualTo(V9GoldIndependentHoldoutContract.Scope));
            Assert.That(plan.RequiredPhysicalTrials, Is.EqualTo(6));
            Assert.That(plan.CandidateLowerThreshold, Is.EqualTo(a.CandidateLowerThreshold));
            Assert.That(plan.TestUsed || plan.OperationallyActivated || plan.IndependentlyCertified,
                Is.False);
        }));
    }

    [Test]
    public void Identical_physical_candidate_frames_use_value_equality_not_list_reference()
    {
        // A final reevaluation can score two distinct observations whose
        // person/mother/birth values coincide. Every invocation materializes
        // a fresh ReadOnlyCollection even when all FS evidence is identical.
        // It must not be misdiagnosed as an inconsistent scorer.
        var model = Guid.Parse("12345678-1234-4123-8123-123456789abc");
        var candidateId = Guid.Parse("87654321-4321-4123-8123-abcdef123456");
        var left = new[] { new V9SnapshotCandidate(candidateId, "MARIA LIMA",
            new DateOnly(1980, 1, 2), "ANA LIMA") };
        var right = new List<V9SnapshotCandidate> {
            new(candidateId, "MARIA LIMA", new DateOnly(1980, 1, 2), "ANA LIMA")
        }.AsReadOnly();
        var decision = new ProbabilisticLinkageDecision(
            ResolutionStatus.RESOLVIDO, candidateId, candidateId, .95m,
            null, null, null, model);
        var retrieval = new V9LowerNonMatchPreview.VerifiedRetrieval(
            Sha(1), Sha(2), 1, true, true, true);

        Assert.Multiple((System.Action)(() =>
        {
            Assert.That(ReferenceEquals(left, right), Is.False);
            Assert.That(V9GoldHarnessCommand.SameFrozenDiagnosticFrame(
                decision, retrieval, left, decision with { }, retrieval with { }, right), Is.True);
            Assert.That(V9GoldHarnessCommand.SameFrozenDiagnosticFrame(
                decision, retrieval, left, decision with { MelhorScore = .93m }, retrieval, right),
                Is.False, "real FS divergence is still rejected");
            Assert.That(V9GoldHarnessCommand.SameFrozenDiagnosticFrame(
                decision, retrieval, left, decision, retrieval with { CandidateFrameSha256 = Sha(3) },
                right), Is.False, "candidate-retrieval drift is still rejected");
            Assert.That(V9GoldHarnessCommand.SameFrozenDiagnosticFrame(
                decision, retrieval, left, decision, retrieval,
                new[] { right[0] with { MotherName = "BIA LIMA" } }),
                Is.False, "candidate value drift is still rejected");
        }));
    }

}

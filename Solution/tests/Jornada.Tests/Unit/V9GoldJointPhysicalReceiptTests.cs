using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldJointPhysicalReceiptTests
{
    private static readonly string Fit = new('a', 64);
    private static readonly string Gold = new('b', 64);
    private static readonly string Csv = new('c', 64);
    private static readonly string Selection = new('d', 64);
    private static readonly string Rerun = new('e', 64);
    private static readonly string Partition = new('f', 64);

    private static JsonObject Receipt() => JsonSerializer.SerializeToNode(new
    {
        version = "V9_REAL_INCREMENTAL_RUNNER_GOLD_TRAIN_SELECTION_V1",
        selectionEnvelopeSha256 = Selection, fitFingerprintSha256 = Fit, snapshotSha256 = Rerun,
        scope = "TRAIN_SELECTION_POST_SELECTION_DIAGNOSTIC_NOT_EXTERNAL_CERTIFICATION",
        sourceMetricRule = "LAST_ATTEMPT_PER_SOURCE_ELIGIBLE_WHEN_TRUTH_ALREADY_IN_PRIOR_GOLD_ACCEPTED_TARGET_MUST_BE_PURE",
        refitted = false, validationReopened = false, testUsed = false,
        certified = false, activated = false, productionDispatchUsed = false,
        truthUsedForGroupingOrRetrieval = false, actualRunnerExecuted = true,
        canonicalGoldPublishedInDisposableHarness = true,
        independentHoldout = (object?)null,
        observations = 20, identityGroups = 5, trueAccepted = 6, falseAccepted = 1,
        jointTrainPhysicalTrial = new
        {
            contract = "V9_GOLD_JOINT_TRAIN_PHYSICAL_TRIAL_V1",
            orderId = "BASELINE_G0_G1_G2_G3", upperThreshold = .9m, lowerThreshold = .1m,
            capturedTrainSources = 20, sourceCount = 20, trueLinkOpportunities = 8,
            trueLinkOpportunityRule = "PRIOR_CSV_WAVE_TRUTH_PRESENT_IN_EARLIER_WAVE_INDEPENDENT_OF_THRESHOLDS",
            completeCandidateUniverseVerified = true, disposableLocalDbVerified = true,
            goldPreWaveTruthFrozen = true, scope = "TRAIN_SELECTION",
            refitted = false, testUsed = false, certified = false, activated = false
        },
        jointUpperTrain = new
        {
            contract = "V9_GOLD_JOINT_UPPER_PHYSICAL_RERUN_V1",
            canonicalGoldSnapshotSha256 = Gold, upperThreshold = .9m,
            distinctRecomputedSnapshotSha256 = Rerun, scope = "TRAIN_SELECTION",
            refitted = false, testUsed = false, activated = false
        },
        goldThreeOrderTrial = new
        {
            orderId = "BASELINE_G0_G1_G2_G3", evidenceScope = "TRAIN_SELECTION",
            originalCorpusSha256 = Csv, frozenSnapshotSha256 = Rerun,
            isolatedRunFingerprintSha256 = Rerun, stablePartitionSha256 = Partition,
            allFourWavesPublished = true,
            executedWaveOrder = new[] { "G0", "G1", "G2", "G3" },
            sourceCount = 20, truthExported = false, uuidExported = false,
            testUsed = false, independentlyCertified = false, activated = false
        },
        lowerNonMatchExperiment = new
        {
            policy = "EXPERIMENTAL_LOWER_TRAIN_ONLY", threshold = .1m,
            scorerReused = true, operationalActivation = false,
            disposableProcedureSha256 = Partition, finalFalseNewIdentityCreations = 2
        },
        goldBcubed = new
        {
            ContractVersion = GoldPartitionBcubedAudit.Version,
            Sources = 20, TrueIdentityGroups = 5, PendingSources = 4,
            CoverageAwareF1 = .8
        },
        publication = new { pendingSources = 4 }
    })!.AsObject();

    private static byte[] Bytes(JsonObject node) => JsonSerializer.SerializeToUtf8Bytes(node);
    private static void Reject(JsonObject node) =>
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointPhysicalReceipt.Read(Bytes(node), Fit, Gold, Csv, Selection)));

    [Test]
    public void Extracts_real_runner_receipt_into_uncertified_train_measurement()
    {
        var row = V9GoldJointPhysicalReceipt.Read(Bytes(Receipt()), Fit, Gold, Csv, Selection);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(row.OrderId, Is.EqualTo("BASELINE_G0_G1_G2_G3"));
            Assert.That(row.AcceptedLinks, Is.EqualTo(7));
            Assert.That(row.FalseLinks, Is.EqualTo(1));
            Assert.That(row.TrueLinkOpportunities, Is.EqualTo(8));
            Assert.That(row.FalseNewReferences, Is.EqualTo(2));
            Assert.That(row.PendingAfter, Is.EqualTo(4));
            Assert.That(row.BCubedF1, Is.EqualTo(.8m));
            Assert.That(row.IndependentIdentityGroups, Is.EqualTo(5));
            Assert.That(row.CompletePhysicalCandidateSearch, Is.True);
            Assert.That(row.DisposableLocalDb, Is.True);
        }));
    }

    [TestCase("completeCandidateUniverseVerified")]
    [TestCase("disposableLocalDbVerified")]
    [TestCase("goldPreWaveTruthFrozen")]
    public void Rejects_missing_or_false_physical_attestation(string name)
    {
        var receipt = Receipt();
        ((JsonObject)receipt["jointTrainPhysicalTrial"]!)[name] = false;
        Reject(receipt);
        receipt = Receipt();
        ((JsonObject)receipt["jointTrainPhysicalTrial"]!).Remove(name);
        Reject(receipt);
    }

    [Test]
    public void Rejects_cross_sha_holdout_or_forged_wave_order()
    {
        var receipt = Receipt();
        ((JsonObject)receipt["goldThreeOrderTrial"]!)["originalCorpusSha256"] = new string('0', 64);
        Reject(receipt);
        receipt = Receipt();
        receipt["independentHoldout"] = new JsonObject();
        Reject(receipt);
        receipt = Receipt();
        ((JsonObject)receipt["goldThreeOrderTrial"]!)["executedWaveOrder"] =
            new JsonArray("G3", "G2", "G1", "G0");
        Reject(receipt);
    }

    [Test]
    public void Rejects_missing_sources_inconsistent_gold_or_false_denominator()
    {
        var receipt = Receipt();
        ((JsonObject)receipt["jointTrainPhysicalTrial"]!)["capturedTrainSources"] = 19;
        Reject(receipt);
        receipt = Receipt();
        ((JsonObject)receipt["goldBcubed"]!)["PendingSources"] = 3;
        Reject(receipt);
        receipt = Receipt();
        receipt["trueAccepted"] = 9;
        Reject(receipt);
    }

    [Test]
    public void Rejects_certification_and_production_activation_claims()
    {
        var receipt = Receipt();
        receipt["certified"] = true;
        Reject(receipt);
        receipt = Receipt();
        ((JsonObject)receipt["lowerNonMatchExperiment"]!)["operationalActivation"] = true;
        Reject(receipt);
    }
}

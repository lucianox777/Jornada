using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fail-closed bridge from a real, aggregate-only disposable Gold TRAIN receipt
/// to the single V9 C# calibrator. A receipt is self-attested physical evidence,
/// NOT independent certification, TEST, or operational authorization.
/// </summary>
public static class V9GoldJointPhysicalReceipt
{
    private const string Rule = "PRIOR_CSV_WAVE_TRUTH_PRESENT_IN_EARLIER_WAVE_INDEPENDENT_OF_THRESHOLDS";
    private static JsonElement Required(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
            throw new InvalidDataException("Joint TRAIN receipt missing " + name);
        return value;
    }

    private static string String(JsonElement obj, string name)
    {
        var value = Required(obj, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Joint TRAIN receipt invalid " + name);
        return value.GetString()!;
    }

    private static bool IsSha(string value) =>
        value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static void Sha(string value, string name)
    {
        if (!IsSha(value)) throw new InvalidDataException("Joint TRAIN invalid SHA " + name);
    }

    private static void Equal(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Joint TRAIN provenance mismatch: " + name);
    }

    private static void Bool(JsonElement obj, string name, bool expected)
    {
        var value = Required(obj, name);
        if (value.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
            throw new InvalidDataException("Joint TRAIN invalid flag " + name);
    }

    private static long Count(JsonElement obj, string name)
    {
        var value = Required(obj, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0)
            throw new InvalidDataException("Joint TRAIN invalid count " + name);
        return number;
    }

    private static decimal Fraction(JsonElement obj, string name)
    {
        var value = Required(obj, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)
            || number is < 0m or > 1m)
            throw new InvalidDataException("Joint TRAIN invalid fraction " + name);
        return number;
    }

    /// <summary>
    /// Parse a complete physical study JSON produced by V9GoldHarnessCommand.
    /// Every claimed guard must be present; missing flags never default to true.
    /// The caller supplies the frozen TRAIN fit, canonical Gold variant, original
    /// CSV and selection envelope hashes from the preregistered experiment.
    /// </summary>
    public static V9LowerNonMatchTrainCalibrator.JointMeasurement Read(
        byte[] studyBytes, string frozenFitSha256, string canonicalGoldSnapshotSha256,
        string originalTrainCsvSha256, string selectionEnvelopeSha256)
    {
        ArgumentNullException.ThrowIfNull(studyBytes);
        foreach (var (value, name) in new[] {
            (frozenFitSha256, "fit"), (canonicalGoldSnapshotSha256, "snapshot"),
            (originalTrainCsvSha256, "TRAIN CSV"), (selectionEnvelopeSha256, "selection") })
            Sha(value, name);
        using var document = JsonDocument.Parse(studyBytes);
        // A physical receipt must have one unambiguous value per field, at every depth.
        // JsonElement.TryGetProperty otherwise accepts the last duplicate silently.
        static void RequireUniqueKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidDataException("Joint TRAIN receipt has duplicate JSON fields.");
                    RequireUniqueKeys(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) RequireUniqueKeys(item);
        }
        RequireUniqueKeys(document.RootElement);
        var root = document.RootElement;
        Equal(String(root, "version"), "V9_REAL_INCREMENTAL_RUNNER_GOLD_TRAIN_SELECTION_V1", "version");
        Equal(String(root, "selectionEnvelopeSha256"), selectionEnvelopeSha256, "selection");
        Equal(String(root, "fitFingerprintSha256"), frozenFitSha256, "fit");
        Equal(String(root, "scope"),
            "TRAIN_SELECTION_POST_SELECTION_DIAGNOSTIC_NOT_EXTERNAL_CERTIFICATION", "scope");
        Equal(String(root, "sourceMetricRule"),
            "LAST_ATTEMPT_PER_SOURCE_ELIGIBLE_WHEN_TRUTH_ALREADY_IN_PRIOR_GOLD_ACCEPTED_TARGET_MUST_BE_PURE",
            "metric rule");
        foreach (var name in new[] { "refitted", "validationReopened", "testUsed",
            "certified", "activated", "productionDispatchUsed", "truthUsedForGroupingOrRetrieval" })
            Bool(root, name, false);
        Bool(root, "actualRunnerExecuted", true);
        Bool(root, "canonicalGoldPublishedInDisposableHarness", true);
        if (Required(root, "independentHoldout").ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Joint TRAIN cannot consume a holdout receipt.");

        var trial = Required(root, "jointTrainPhysicalTrial");
        Equal(String(trial, "contract"), "V9_GOLD_JOINT_TRAIN_PHYSICAL_TRIAL_V1", "trial");
        Equal(String(trial, "scope"), "TRAIN_SELECTION", "trial scope");
        Equal(String(trial, "trueLinkOpportunityRule"), Rule, "truth rule");
        foreach (var name in new[] { "refitted", "testUsed", "certified", "activated" })
            Bool(trial, name, false);
        foreach (var name in new[] { "completeCandidateUniverseVerified",
            "disposableLocalDbVerified", "goldPreWaveTruthFrozen" })
            Bool(trial, name, true);

        var upper = Fraction(trial, "upperThreshold");
        var lower = Fraction(trial, "lowerThreshold");
        if (upper <= 0 || lower <= 0 || lower >= upper)
            throw new InvalidDataException("Joint TRAIN invalid upper/lower pair.");
        var order = String(trial, "orderId");
        if (!V9LowerNonMatchTrainCalibrator.JointOrders.Contains(order))
            throw new InvalidDataException("Joint TRAIN order is not canonical.");

        var upperTrial = Required(root, "jointUpperTrain");
        Equal(String(upperTrial, "contract"), "V9_GOLD_JOINT_UPPER_PHYSICAL_RERUN_V1", "upper trial");
        Equal(String(upperTrial, "scope"), "TRAIN_SELECTION", "upper scope");
        Equal(String(upperTrial, "canonicalGoldSnapshotSha256"), canonicalGoldSnapshotSha256, "canonical Gold");
        Equal(String(upperTrial, "distinctRecomputedSnapshotSha256"), String(root, "snapshotSha256"), "upper rerun");
        if (Fraction(upperTrial, "upperThreshold") != upper)
            throw new InvalidDataException("Joint TRAIN upper override differs from trial.");
        foreach (var name in new[] { "refitted", "testUsed", "activated" })
            Bool(upperTrial, name, false);

        var threeOrder = Required(root, "goldThreeOrderTrial");
        Equal(String(threeOrder, "protocol"), GoldTrainThreeOrderProtocol.Version, "Gold protocol");
        Equal(String(threeOrder, "sourceKeyContract"),
            GoldTrainThreeOrderProtocol.StableSourceKeyContract, "stable CSV source key");
        Equal(String(threeOrder, "orderId"), order, "Gold order");
        Equal(String(threeOrder, "evidenceScope"), "TRAIN_SELECTION", "Gold scope");
        Equal(String(threeOrder, "originalCorpusSha256"), originalTrainCsvSha256, "CSV");
        Equal(String(threeOrder, "frozenSnapshotSha256"), String(root, "snapshotSha256"), "Gold snapshot");
        Sha(String(threeOrder, "isolatedRunFingerprintSha256"), "isolated run");
        Sha(String(threeOrder, "stablePartitionSha256"), "stable partition");
        Bool(threeOrder, "allFourWavesPublished", true);
        foreach (var name in new[] { "truthExported", "uuidExported", "testUsed",
            "independentlyCertified", "activated" })
            Bool(threeOrder, name, false);
        var executed = Required(threeOrder, "executedWaveOrder");
        var canonical = GoldTrainThreeOrderProtocol.ResolveOrder(order, ["G0", "G1", "G2", "G3"]);
        if (executed.ValueKind != JsonValueKind.Array
            || !executed.EnumerateArray().Select(x => x.GetString()).SequenceEqual(canonical))
            throw new InvalidDataException("Joint TRAIN physical wave order mismatch.");

        var sourceCount = Count(root, "observations");
        if (sourceCount == 0 || Count(trial, "sourceCount") != sourceCount
            || Count(trial, "capturedTrainSources") != sourceCount
            || Count(threeOrder, "sourceCount") != sourceCount)
            throw new InvalidDataException("Joint TRAIN capture does not cover all sources.");
        var groups = Count(root, "identityGroups");
        if (groups < 2 || groups > int.MaxValue)
            throw new InvalidDataException("Joint TRAIN independent group count invalid.");
        var opportunities = Count(trial, "trueLinkOpportunities");
        if (opportunities == 0)
            throw new InvalidDataException("Joint TRAIN no true link opportunities.");

        var lowerTrial = Required(root, "lowerNonMatchExperiment");
        Equal(String(lowerTrial, "policy"), "EXPERIMENTAL_LOWER_TRAIN_ONLY", "lower scope");
        if (Fraction(lowerTrial, "threshold") != lower)
            throw new InvalidDataException("Joint TRAIN lower threshold mismatch.");
        Bool(lowerTrial, "scorerReused", true);
        Bool(lowerTrial, "operationalActivation", false);
        Sha(String(lowerTrial, "disposableProcedureSha256"), "disposable procedure");

        var acceptedTrue = Count(root, "trueAccepted");
        var acceptedFalse = Count(root, "falseAccepted");
        var accepted = checked(acceptedTrue + acceptedFalse);
        if (acceptedTrue > opportunities || accepted > sourceCount)
            throw new InvalidDataException("Joint TRAIN accepted links exceed frozen opportunities/sources.");
        var falseCreations = Count(lowerTrial, "finalFalseNewIdentityCreations");
        if (falseCreations > sourceCount)
            throw new InvalidDataException("Joint TRAIN physical creations exceed sources.");

        var gold = Required(root, "goldBcubed");
        Equal(String(gold, "ContractVersion"), GoldPartitionBcubedAudit.Version, "B-cubed contract");
        if (Count(gold, "Sources") != sourceCount
            || Count(gold, "TrueIdentityGroups") != groups
            || Count(gold, "PendingSources") != Count(Required(root, "publication"), "pendingSources"))
            throw new InvalidDataException("Joint TRAIN B-cubed/Gold denominator mismatch.");
        var pending = Count(gold, "PendingSources");
        var published = Count(threeOrder, "publishedSourceCount");
        var goldPublished = Count(gold, "PublishedSources");
        var goldGroups = Count(gold, "GoldIdentityGroups");
        // A claimed stable partition is not comparable across physical orders
        // unless its complete source universe reconciles with Gold and B-cubed.
        // Never infer missing published sources from the pending count alone.
        if (published != goldPublished || published > sourceCount
            || pending != sourceCount - published
            || goldGroups > published || (goldGroups == 0) != (published == 0))
            throw new InvalidDataException("Joint TRAIN stable Gold/B-cubed publication universe mismatch.");
        var f1 = Fraction(gold, "CoverageAwareF1");
        return new(order, "TRAIN_SELECTION", frozenFitSha256, canonicalGoldSnapshotSha256,
            Convert.ToHexString(SHA256.HashData(studyBytes)).ToLowerInvariant(),
            upper, lower, checked((int)groups), accepted, acceptedFalse,
            opportunities, falseCreations, pending, f1,
            UpperRecomputedByPhysicalRankResolve: true, CompletePhysicalCandidateSearch: true,
            GoldPreWaveTruthFrozen: true, DisposableLocalDb: true);
    }
}

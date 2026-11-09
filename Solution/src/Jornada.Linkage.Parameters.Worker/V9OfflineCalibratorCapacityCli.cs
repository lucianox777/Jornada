using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Offline reproduction of the exact pre-SQL stages of --v9-calibrate.
/// No SQL, connection string, TF reference query, fitting or TEST row access.
/// Only bounded aggregate counts / cryptographic hashes leave this command.
/// </summary>
public static class V9OfflineCalibratorCapacityCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--v9-offline-train-capacity")
            throw new ArgumentException(
                "Usage: --v9-offline-train-capacity <bound-plan.json> <observacoes.csv> <new-receipt.json>");
        var output = Path.GetFullPath(args[3]);
        if (File.Exists(output) || Directory.Exists(output))
            throw new InvalidOperationException("V9_OFFLINE_RECEIPT_EXISTS");
        var input = Path.GetFullPath(args[2]);
        var planBytes = await File.ReadAllBytesAsync(args[1]);
        V9OptionalCompatibilityParameters.ValidateRawInput(planBytes);
        var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(planBytes, Json)
            ?? throw new InvalidDataException("V9_OFFLINE_PLAN_MISSING");
        V9NestedCalibrationCoordinator.ValidatePlan(plan);
        if (plan.Calibration.DeclaredSearchSpace is null)
            throw new InvalidDataException("V9_OFFLINE_REQUIRES_DECLARED_RULESETS");
        var sourceManifest = await File.ReadAllBytesAsync(
            Path.Combine(Path.GetDirectoryName(input)!, "generation-manifest.json"));
        await using var stream = File.OpenRead(input);
        var inputSha = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        var declaration = plan.Calibration.ObservationCapDeclaration
            ?? throw new InvalidDataException("V9_OFFLINE_MANIFEST_DECLARATION_REQUIRED");
        V9SyntheticObservationCapEvidence.ValidateManifest(
            sourceManifest, declaration, inputSha, plan.Calibration.MaximumAcceptedPerBaseIdentity);
        using (var manifest = JsonDocument.Parse(sourceManifest))
        {
            var root = manifest.RootElement;
            if (root.GetProperty("error_profile").GetString() != "clean"
                || root.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
                throw new InvalidDataException("V9_OFFLINE_PRODUCER_PROFILE_INVALID");
        }
        // Same CSV reader and partition boundary used by --v9-calibrate:
        // only TRAIN names/DOB are parsed; TEST identities are metadata-only.
        var train = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(
            input, minimumObservationCount: 4,
            maximumObservationsPerBaseIdentity: declaration.MaximumObservationsPerBaseIdentity,
            onlyPartition: "TRAIN");
        var split = V9InternalTrainingSplit.Create(train, plan.SplitSeed, plan.AdjustmentFraction);
        var frame = split.Adjustment.Concat(split.Selection.Select(
            x => x with { Partition = "VALIDATION" })).ToArray();
        var ruleSets = V9AdaptiveCalibrationCoordinator.CombineCandidateRuleSets(
            plan.Calibration.DeclaredSearchSpace.Enumerate(),
            plan.Calibration.BaselineRuleSets);
        var variants = plan.MuEstimationMethods is null
            ? plan.Calibration.ModelOptions
            : V9MuEstimationSearchSpace.Expand(plan.Calibration.ModelOptions,
                plan.MuEstimationMethods, plan.MaximumSearchConfigurations);
        var physicalHypotheses = checked(ruleSets.Count * variants.Count * plan.Calibration.ConflictMargins.Count);
        if (physicalHypotheses > plan.MaximumSearchConfigurations)
            throw new InvalidDataException("V9_OFFLINE_PHYSICAL_HYPOTHESES_EXCEED_DECLARATION");

        var counts = ruleSets.Select(passes =>
            V9PhysicalSqlAdaptiveCalibrationProbe.InspectOfflinePairCapacity(
                frame, passes, plan.Calibration.Execution)).ToArray();
        var signaturesWithin = counts.All(x => x.WithinDeclaredProjectedSignatureCap);
        var pairsWithin = counts.All(x => x.WithinDeclaredPairIncidenceCap);
        var result = new
        {
            schema = "V9_OFFLINE_100K_PRESQL_CAPACITY_V1",
            status = !signaturesWithin ? "DECLARED_SIGNATURE_CAP_EXCEEDED_NO_FIT"
                : !pairsWithin ? "DECLARED_PAIR_INCIDENCE_CAP_EXCEEDED_NO_FIT"
                : "PRE_SQL_CAPACITY_ADMITTED_NOT_FITTED",
            originalPlanSha256 = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant(),
            observationsSha256 = inputSha,
            manifestSha256 = Convert.ToHexString(SHA256.HashData(sourceManifest)).ToLowerInvariant(),
            trainObservationsWithoutCpf = train.Length,
            trainAdjustmentObservations = split.Adjustment.Count,
            trainSelectionObservations = split.Selection.Count,
            ruleSets = counts,
            physicalHypotheses,
            declaration.MaximumObservationsPerBaseIdentity,
            scope = "OFFLINE_TRAIN_ONLY_NO_SQL_NO_FIT_NO_TEST_NO_GOLD",
            executionModified = false,
            scientificPlanModified = false,
            physicalSqlExecuted = false,
            testUsed = false,
            goldUsed = false,
            activated = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using var receipt = new FileStream(output, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(receipt, result, Json);
        Console.WriteLine("V9_OFFLINE_TRAIN_CAPACITY:" + result.status);
    }
}

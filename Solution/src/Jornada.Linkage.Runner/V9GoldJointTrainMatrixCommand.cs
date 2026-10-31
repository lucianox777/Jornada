using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fail-closed aggregation of preregistered, REAL disposable Gold TRAIN receipts.
/// This command NEVER executes SQL, reads TEST, selects a new corpus or certifies
/// the self-attested physical runs. The Runner remains the only physical engine.
/// </summary>
public static class V9GoldJointTrainMatrixCommand
{
    public const string Flag = "--v9-gold-joint-train-matrix-aggregate";
    public const string PreflightFlag = "--v9-gold-joint-train-plan-preflight";
    public const string Version = "V9_GOLD_JOINT_TRAIN_PREREGISTERED_MATRIX_V1";
    public const string PlanSuffix = ".joint-train-plan.json";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Regex OwnedInstance = new(
        @"^\(localdb\)\\JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record Plan(
        string Version,
        string FrozenGoldFitSha256,
        string FrozenGoldSnapshotSha256,
        string OriginalTrainCsvSha256,
        string SelectionEnvelopeSha256,
        IReadOnlyList<decimal> CandidateUpperThresholds,
        IReadOnlyList<decimal> CandidateLowerThresholds,
        int MinimumIndependentIdentityGroups,
        bool ManualCalibrationRequested,
        bool TestUsed,
        string DeclaredGridSha256);

    public sealed record Matrix(
        string Version, string PreregisteredPlanSha256, string OriginalTrainCsvSha256,
        string SelectionEnvelopeSha256, int PhysicalCellCount,
        IReadOnlyList<V9LowerNonMatchTrainCalibrator.JointMeasurement> Measurements,
        V9LowerNonMatchTrainCalibrator.JointStudy Study,
        bool TestUsed, bool IndependentlyCertified, bool OperationallyActivated);

    private static bool IsSha(string? value) => value is { Length: 64 }
        && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static Plan ReadPlan(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Plan plan;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Joint TRAIN plan must be a JSON object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!seen.Add(property.Name))
                    throw new InvalidDataException("Joint TRAIN plan has ambiguous duplicate JSON properties.");
            plan = JsonSerializer.Deserialize<Plan>(bytes, Json)
                ?? throw new InvalidDataException("Joint TRAIN plan absent.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Joint TRAIN plan malformed.", ex);
        }
        if (plan.Version != Version || !IsSha(plan.FrozenGoldFitSha256)
            || !IsSha(plan.FrozenGoldSnapshotSha256)
            || !IsSha(plan.OriginalTrainCsvSha256)
            || !IsSha(plan.SelectionEnvelopeSha256)
            || !IsSha(plan.DeclaredGridSha256)
            || !plan.ManualCalibrationRequested || plan.TestUsed
            || plan.MinimumIndependentIdentityGroups < 2
            || plan.CandidateUpperThresholds is null || plan.CandidateLowerThresholds is null
            || plan.CandidateUpperThresholds.Count is < 2 or > 64
            || plan.CandidateLowerThresholds.Count is < 2 or > 64
            || (long)plan.CandidateUpperThresholds.Count * plan.CandidateLowerThresholds.Count > 256
            || plan.CandidateUpperThresholds.Any(x => x is <= 0m or > 1m)
            || plan.CandidateLowerThresholds.Any(x => x is <= 0m or >= 1m)
            || plan.CandidateUpperThresholds.Distinct().Count() != plan.CandidateUpperThresholds.Count
            || plan.CandidateLowerThresholds.Distinct().Count() != plan.CandidateLowerThresholds.Count
            || plan.CandidateLowerThresholds.Max() >= plan.CandidateUpperThresholds.Min()
            || V9LowerNonMatchTrainCalibrator.JointGridFingerprint(
                plan.CandidateUpperThresholds, plan.CandidateLowerThresholds)
                != plan.DeclaredGridSha256)
            throw new InvalidDataException("Joint TRAIN plan/grid is not frozen and valid.");
        return plan;
    }

    /// <summary>Used before opening LocalDB to reject unregistered physical cells.</summary>
    public static string ValidateCellPlan(byte[] planBytes, decimal upper, decimal lower, string order)
    {
        var plan = ReadPlan(planBytes);
        if (!plan.CandidateUpperThresholds.Contains(upper)
            || !plan.CandidateLowerThresholds.Contains(lower)
            || !V9LowerNonMatchTrainCalibrator.JointOrders.Contains(order))
            throw new InvalidDataException("Physical Gold TRAIN cell is not preregistered.");
        return Sha(planBytes);
    }

    public static Matrix Aggregate(byte[] planBytes, IReadOnlyList<byte[]> receipts)
    {
        var plan = ReadPlan(planBytes);
        ArgumentNullException.ThrowIfNull(receipts);
        var expected = checked(plan.CandidateUpperThresholds.Count
            * plan.CandidateLowerThresholds.Count * V9LowerNonMatchTrainCalibrator.JointOrders.Count);
        if (receipts.Count != expected || receipts.Any(x => x is null))
            throw new InvalidDataException("Physical Gold TRAIN matrix is incomplete.");
        var planSha = Sha(planBytes);
        var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runs = new HashSet<string>(StringComparer.Ordinal);
        var physicalRuns = new HashSet<string>(StringComparer.Ordinal);
        var measurements = new List<V9LowerNonMatchTrainCalibrator.JointMeasurement>(expected);
        foreach (var bytes in receipts)
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("jointTrainPhysicalTrial", out var trial)
                || trial.ValueKind != JsonValueKind.Object
                || !trial.TryGetProperty("preregisteredPlanSha256", out var preregistered)
                || preregistered.ValueKind != JsonValueKind.String
                || preregistered.GetString() != planSha
                || !trial.TryGetProperty("disposableLocalDbInstance", out var instanceField)
                || instanceField.ValueKind != JsonValueKind.String
                || instanceField.GetString() is not { } instance
                || !OwnedInstance.IsMatch(instance)
                || !instances.Add(instance))
                throw new InvalidDataException("Missing preregistration or independent disposable LocalDB per cell.");
            var row = V9GoldJointPhysicalReceipt.Read(bytes,
                plan.FrozenGoldFitSha256, plan.FrozenGoldSnapshotSha256,
                plan.OriginalTrainCsvSha256, plan.SelectionEnvelopeSha256);
            if (!runs.Add(row.PhysicalRunSha256))
                throw new InvalidDataException("Physical Gold TRAIN receipt reused.");
            // The receipt digest identifies JSON bytes, not the SQL execution.
            // An attacker or accidental copy can change the receipt metadata
            // (including the claimed LocalDB instance) while preserving the
            // underlying physical run. Enforce both independent identities.
            var physicalRun = root.GetProperty("goldThreeOrderTrial")
                .GetProperty("isolatedRunFingerprintSha256").GetString()!;
            if (!physicalRuns.Add(physicalRun))
                throw new InvalidDataException("Physical Gold TRAIN SQL run fingerprint reused across cells.");
            measurements.Add(row);
        }
        var frozen = measurements.AsReadOnly();
        var policy = new V9LowerNonMatchTrainCalibrator.JointPolicy(
            plan.FrozenGoldFitSha256, plan.FrozenGoldSnapshotSha256,
            V9LowerNonMatchTrainCalibrator.JointEvidenceFingerprint(frozen),
            plan.DeclaredGridSha256, planSha,
            plan.CandidateUpperThresholds, plan.CandidateLowerThresholds,
            plan.MinimumIndependentIdentityGroups, plan.ManualCalibrationRequested, plan.TestUsed);
        // SearchJoint independently verifies every upper x lower x 3-order cell,
        // constant denominators, unique physical runs and separate Pareto stages.
        var study = V9LowerNonMatchTrainCalibrator.SearchJoint(policy, frozen);
        return new Matrix(Version, planSha, plan.OriginalTrainCsvSha256,
            plan.SelectionEnvelopeSha256, frozen.Count, frozen, study,
            TestUsed: false, IndependentlyCertified: false, OperationallyActivated: false);
    }

    /// <summary>
    /// Validates the complete content-addressed TRAIN plan before any disposable
    /// LocalDB is created. No SQL connection, output artifact or TEST is touched.
    /// </summary>
    public static async Task<string> ValidatePlanFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A frozen plan file path is required.", nameof(path));
        var bytes = await File.ReadAllBytesAsync(path);
        var sha = Sha(bytes);
        if (!string.Equals(Path.GetFileName(path), sha + PlanSuffix, StringComparison.Ordinal))
            throw new InvalidDataException("Joint TRAIN plan filename must match its SHA-256.");
        _ = ReadPlan(bytes);
        return sha;
    }

    public static async Task RunPlanPreflightAsync(string[] args)
    {
        if (args is null || args.Length != 2 || args[0] != PreflightFlag)
            throw new ArgumentException("Use --v9-gold-joint-train-plan-preflight <SHA.joint-train-plan.json>.");
        Console.WriteLine("Validated frozen Gold TRAIN plan: " + await ValidatePlanFileAsync(args[1]));
    }

    public static async Task<Matrix> RunAsync(string[] args)
    {
        if (args is null || args.Length < 4 || args[0] != Flag)
            throw new ArgumentException("Use --v9-gold-joint-train-matrix-aggregate <SHA.joint-train-plan.json> <all physical gold-runner-study.json> <new-output.json>.");
        var paths = args.Skip(1).Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length
            || paths.Skip(1).SkipLast(1).Any(x => Path.GetFileName(x) != "gold-runner-study.json")
            || paths.Skip(1).SkipLast(1).Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length - 2
            || File.Exists(paths[^1]) || Directory.Exists(paths[^1]))
            throw new InvalidDataException("Gold TRAIN requires distinct physical report directories and new output.");
        var planBytes = await File.ReadAllBytesAsync(paths[0]);
        if (Path.GetFileName(paths[0]) != Sha(planBytes) + PlanSuffix)
            throw new InvalidDataException("Joint TRAIN plan file must be content-addressed before trials.");
        var receipts = await Task.WhenAll(paths.Skip(1).SkipLast(1).Select(path => File.ReadAllBytesAsync(path)));
        var matrix = Aggregate(planBytes, receipts);
        await using var stream = new FileStream(paths[^1], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, matrix, Json);
        return matrix;
    }
}

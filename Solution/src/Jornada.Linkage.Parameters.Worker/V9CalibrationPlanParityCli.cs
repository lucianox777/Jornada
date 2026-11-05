using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// A read-only, no-SQL parity preflight: verifies that the Git-versioned V2
/// explicit user configuration has EXACTLY the semantics of the original
/// frozen plan under the same C# deserializer and serializer as the calibrator.
/// </summary>
public static class V9CalibrationPlanParityCli
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--v9-compare-calibration-plans")
            throw new ArgumentException(
                "Uso: --v9-compare-calibration-plans <plano-original.json> <plano-explicito.json> <SHA256-original-64-hex>");

        var original = await File.ReadAllBytesAsync(args[1]);
        var effective = await File.ReadAllBytesAsync(args[2]);
        if (original.Length is <= 0 or > 10_000_000 || effective.Length is <= 0 or > 10_000_000)
            throw new InvalidDataException("Planos de calibração ausentes ou excessivos.");

        var expected = args[3];
        var fileDigest = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        if (expected.Length != 64 || !string.Equals(fileDigest, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plano V2 original não coincide com SHA-256 congelado.");

        var planOriginal = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(original, Strict)
            ?? throw new InvalidDataException("Plano V2 original inválido.");
        var planEffective = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(effective, Strict)
            ?? throw new InvalidDataException("Plano V2 explícito inválido.");
        V9AdaptiveCalibrationCoordinator.ValidatePlan(planOriginal);
        V9AdaptiveCalibrationCoordinator.ValidatePlan(planEffective);

        // Same semantic serialization used by the C# study's PlanSha256.
        // No new defaults, parameters, thresholds or runner are introduced.
        var originalCanonical = JsonSerializer.Serialize(planOriginal);
        var effectiveCanonical = JsonSerializer.Serialize(planEffective);
        if (!string.Equals(originalCanonical, effectiveCanonical, StringComparison.Ordinal))
            throw new InvalidDataException("Plano explícito altera parâmetros efetivos da V2.");

        var normalizedSha = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(originalCanonical))).ToLowerInvariant();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "V2_EFFECTIVE_PLAN_IDENTICAL",
            frozenOriginalPlanSha256 = fileDigest,
            normalizedEffectivePlanSha256 = normalizedSha,
            originalAndEffectiveCSharpSemanticsEqual = true,
            numberOfModelOptions = planOriginal.ModelOptions.Count,
            numberOfMargins = planOriginal.ConflictMargins.Count,
            testUsed = false, sqlUsed = false, calibrated = false,
            activated = false
        }));
    }
}

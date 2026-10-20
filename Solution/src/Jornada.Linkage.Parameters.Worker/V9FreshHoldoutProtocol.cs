using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9FreshHoldoutDeclaration(ulong Seed, string ManifestSha256, string ObservationsSha256,
    string InputFingerprintSha256, string ExposureKey, string Namespace,
    string Scope = "NEW_SYNTHETIC_VALIDATION_NOT_IID_CERTIFICATION");

public static class V9FreshHoldoutProtocol
{
    public static V9FreshHoldoutDeclaration Inspect(ReadOnlySpan<byte> manifestBytes, string observationsSha256,
        string originalObservationsSha256, ulong originalSeed, int acceptedCap)
    {
        using var document = JsonDocument.Parse(manifestBytes.ToArray());
        var m = document.RootElement;
        var manifestSha = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        var declaration = new V9SyntheticObservationCapDeclaration(
            m.GetProperty("observation_multiplicity").GetProperty("contract_version").GetString()!,
            m.GetProperty("generator_version").GetString()!,
            m.GetProperty("observation_multiplicity").GetProperty("maximum_observations_per_base_identity").GetInt32(),
            manifestSha, observationsSha256);
        V9SyntheticObservationCapEvidence.ValidateManifest(manifestBytes, declaration, observationsSha256, acceptedCap);
        var seed = m.GetProperty("seed").GetUInt64();
        if (seed is 42 or 43 or 44 || seed == originalSeed || string.Equals(observationsSha256, originalObservationsSha256, StringComparison.OrdinalIgnoreCase)
            || m.GetProperty("error_profile").GetString() != "clean"
            || m.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
            throw new InvalidDataException("VALIDATION histórica/exposta ou perfil incompatível; não reiniciar multiplicidade em três.");
        var input = m.GetProperty("input_fingerprint_sha256").GetString()!;
        var exposureKey = V9InternalTrainingSplit.Hash(FormattableString.Invariant($"V9_FRESH_SYNTHETIC_HOLDOUT_V1|{seed}"));
        return new(seed, manifestSha, observationsSha256, input, exposureKey, manifestSha + ":");
    }

    public static async Task ReserveOnceAsync(string registryDirectory, V9FreshHoldoutDeclaration declaration,
        string selectionEnvelopeSha256, IReadOnlyList<string> finalists, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(finalists);
        if (selectionEnvelopeSha256 is not { Length: 64 } || !selectionEnvelopeSha256.All(Uri.IsHexDigit)
            || declaration.ExposureKey is not { Length: 64 } || !declaration.ExposureKey.All(Uri.IsHexDigit)
            || finalists.Count is < 1 or > 3 || finalists.Distinct(StringComparer.Ordinal).Count() != finalists.Count
            || finalists.Any(x => x is not { Length: 64 } || !x.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Reserva exige envelope e até três finalistas distintos já congelados.");
        Directory.CreateDirectory(registryDirectory);
        // Atomic across concurrent invocations. Failure still consumes the receipt:
        // no retries with a different trio or a fresh alpha budget on this cohort.
        await using var stream = new FileStream(Path.Combine(Path.GetFullPath(registryDirectory), declaration.ExposureKey + ".json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, new { declaration, selectionEnvelopeSha256, finalists,
            state = "RESERVED_BEFORE_HOLDOUT_OUTCOMES_ONE_FAMILY_ONLY", testUsed = false }, cancellationToken: cancellationToken);
    }
}

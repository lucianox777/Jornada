using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Content pins for the base generator's population support, never IID evidence.</summary>
public sealed record V9SyntheticObservationCapDeclaration(
    string ContractVersion, string GeneratorVersion, int MaximumObservationsPerBaseIdentity,
    string ManifestSha256, string ObservationsSha256);

public static class V9SyntheticObservationCapEvidence
{
    public static void ValidateDeclaration(V9SyntheticObservationCapDeclaration declaration, int acceptedCap)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.ContractVersion != SyntheticObservationMultiplicityContract.Version
            || declaration.GeneratorVersion != SyntheticObservationMultiplicityContract.GeneratorVersion
            || declaration.MaximumObservationsPerBaseIdentity != SyntheticObservationMultiplicityContract.MaximumObservationsPerBaseIdentity
            || acceptedCap < declaration.MaximumObservationsPerBaseIdentity
            || !IsSha(declaration.ManifestSha256) || !IsSha(declaration.ObservationsSha256))
            throw new InvalidDataException("Cap exige contrato do gerador base e pins SHA completos; não pode ser inferido da amostra.");
    }

    public static void ValidateManifest(ReadOnlySpan<byte> manifestBytes,
        V9SyntheticObservationCapDeclaration declaration, string observationsSha256, int acceptedCap)
    {
        ValidateDeclaration(declaration, acceptedCap);
        if (!Equal(Convert.ToHexString(SHA256.HashData(manifestBytes)), declaration.ManifestSha256)
            || !Equal(observationsSha256, declaration.ObservationsSha256))
            throw new InvalidDataException("Manifesto ou CSV diverge do SHA pré-declarado.");
        using var document = JsonDocument.Parse(manifestBytes.ToArray());
        var manifest = document.RootElement;
        if (!manifest.TryGetProperty("generator_version", out var generator)
            || generator.GetString() != declaration.GeneratorVersion
            || !manifest.TryGetProperty("observation_multiplicity", out var multiplicity)
            || !multiplicity.TryGetProperty("contract_version", out var version)
            || version.GetString() != declaration.ContractVersion
            || !multiplicity.TryGetProperty("maximum_observations_per_base_identity", out var maximum)
            || maximum.GetInt32() != declaration.MaximumObservationsPerBaseIdentity
            || !multiplicity.TryGetProperty("scope", out var scope)
            || scope.GetString() != "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES"
            || !multiplicity.TryGetProperty("support", out var support)
            || !support.EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(SyntheticObservationMultiplicityContract.Counts)
            || !multiplicity.TryGetProperty("weights", out var weights)
            || !weights.EnumerateArray().Select(x => x.GetUInt64()).SequenceEqual(SyntheticObservationMultiplicityContract.Weights)
            || !manifest.TryGetProperty("outputs", out var outputs)
            || outputs.EnumerateArray().Count(x => x.GetProperty("path").GetString() == "observacoes.csv") != 1
            || !Equal(outputs.EnumerateArray().Single(x => x.GetProperty("path").GetString() == "observacoes.csv")
                .GetProperty("sha256").GetString(), observationsSha256))
            throw new InvalidDataException("Manifesto não comprova o suporte populacional do CSV fixado.");
        // Hash binding authenticates identity of these bytes, not their author or
        // independence after candidate competition. No selector gate is opened.
    }

    /// <summary>
    /// The generator's immutable generation-manifest.json has no "people" field.
    /// Its SHA-bound outputs include gabarito.json, which declares aggregate people.
    /// Only that scalar is read; no per-person TEST truth is used for calibration.
    /// Call after ValidateManifest verifies the pinned plan and observation CSV.
    /// </summary>
    public static int ReadPinnedPopulationCount(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> truthBytes)
    {
        using var manifestDocument = JsonDocument.Parse(manifestBytes.ToArray());
        var manifest = manifestDocument.RootElement;
        if (manifest.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifesto não declara saída de gabarito autenticável.");
        var matches = outputs.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object
                && x.TryGetProperty("path", out var path)
                && path.ValueKind == JsonValueKind.String
                && path.GetString() == "gabarito.json").ToArray();
        if (matches.Length != 1
            || !matches[0].TryGetProperty("sha256", out var sha)
            || sha.ValueKind != JsonValueKind.String
            || !Equal(sha.GetString(), Convert.ToHexString(SHA256.HashData(truthBytes))))
            throw new InvalidDataException("Gabarito agregado não corresponde ao SHA do manifesto fixado.");
        using var truthDocument = JsonDocument.Parse(truthBytes.ToArray());
        var truth = truthDocument.RootElement;
        if (truth.ValueKind != JsonValueKind.Object
            || !truth.TryGetProperty("people", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var people) || people <= 0
            || !truth.TryGetProperty("generator_version", out var generator)
            || generator.ValueKind != JsonValueKind.String
            || !manifest.TryGetProperty("generator_version", out var expectedGenerator)
            || expectedGenerator.ValueKind != JsonValueKind.String
            || generator.GetString() != expectedGenerator.GetString()
            || !truth.TryGetProperty("seed", out var seed) || !seed.TryGetUInt64(out var truthSeed)
            || !manifest.TryGetProperty("seed", out var expectedSeed)
            || !expectedSeed.TryGetUInt64(out var manifestSeed) || truthSeed != manifestSeed)
            throw new InvalidDataException("Gabarito agregado não comprova a população e origem declaradas.");
        return people;
    }

    private static bool IsSha(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

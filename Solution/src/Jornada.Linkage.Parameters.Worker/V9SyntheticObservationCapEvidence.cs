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

    private static bool IsSha(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

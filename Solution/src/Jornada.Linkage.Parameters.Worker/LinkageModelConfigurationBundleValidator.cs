using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record LinkageModelConfigurationBundle(
    string BundleVersion,
    string BaseCatalogVersion,
    string BlockingCatalogVersion,
    string FsCatalogVersion,
    string FingerprintSha256);

/// <summary>
/// Fecha os três catálogos simples como uma única identidade de configuração do modelo.
/// A validação é referencial/semântica; os catálogos não precisam conter os mesmos itens.
/// </summary>
public static class LinkageModelConfigurationBundleValidator
{
    public const string FingerprintAlgorithm = "SHA256_CANONICAL_JSON_V1";

    public static LinkageModelConfigurationBundle LoadAndValidate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        using var manifest = Read(directory, "model-config-bundle.json");
        var root = manifest.RootElement;
        Require(root.GetProperty("fingerprintAlgorithm").GetString() == FingerprintAlgorithm,
            "Algoritmo de fingerprint do bundle de modelo não suportado.");

        var components = root.GetProperty("components");
        var baseFile = RequiredFile(components, "base");
        var blockingFile = RequiredFile(components, "blocking");
        var fsFile = RequiredFile(components, "fs");
        using var baseDoc = Read(directory, baseFile);
        using var blockingDoc = Read(directory, blockingFile);
        using var fsDoc = Read(directory, fsFile);

        var baseAttributes = baseDoc.RootElement.GetProperty("attributes").EnumerateArray()
            .Select(static x => x.GetProperty("code").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(baseAttributes.Count > 0, "Catálogo-base do modelo está vazio.");

        foreach (var attribute in blockingDoc.RootElement.GetProperty("sourceAttributes").EnumerateArray())
            Require(baseAttributes.Contains(attribute.GetString()!),
                $"Blocking referencia atributo ausente do catálogo-base: {attribute.GetString()}.");

        foreach (var evidence in fsDoc.RootElement.GetProperty("evidence").EnumerateArray())
        {
            var attribute = evidence.GetProperty("attribute").GetString()!;
            Require(baseAttributes.Contains(attribute),
                $"FS referencia atributo ausente do catálogo-base: {attribute}.");
            var states = evidence.GetProperty("states").EnumerateArray()
                .Select(static x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
            Require(states.Contains("MISSING"), $"FS deve declarar MISSING para {attribute}.");
        }

        Require(fsDoc.RootElement.GetProperty("missingLogLikelihoodRatio").GetDecimal() == 0m,
            "MISSING deve permanecer neutro (LLR=0) no bundle V1.");

        var canonical = string.Join("\n",
            CanonicalJson(baseDoc.RootElement),
            CanonicalJson(blockingDoc.RootElement),
            CanonicalJson(fsDoc.RootElement));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return new LinkageModelConfigurationBundle(
            root.GetProperty("bundleVersion").GetString()!,
            baseDoc.RootElement.GetProperty("catalogVersion").GetString()!,
            blockingDoc.RootElement.GetProperty("catalogVersion").GetString()!,
            fsDoc.RootElement.GetProperty("catalogVersion").GetString()!,
            fingerprint);
    }

    private static string RequiredFile(JsonElement components, string name)
    {
        var file = components.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file)
            throw new InvalidDataException($"Componente inválido no bundle de modelo: {name}.");
        return file;
    }

    private static JsonDocument Read(string directory, string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, file)));

    private static string CanonicalJson(JsonElement element) =>
        JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = false });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

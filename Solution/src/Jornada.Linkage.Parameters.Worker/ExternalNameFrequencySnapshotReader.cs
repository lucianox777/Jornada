using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Leitor fail-closed de snapshots locais de frequências agregadas do IBGE.
/// Não realiza acesso de rede e não conecta o catálogo ao scorer operacional.
/// </summary>
public static class ExternalNameFrequencySnapshotReader
{
    public static ExternalNameFrequencySnapshot LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required.", nameof(path));

        return ParseJson(File.ReadAllText(path));
    }

    public static ExternalNameFrequencySnapshot ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Snapshot JSON is required.", nameof(json));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Snapshot root must be a JSON object.");

        var source = RequiredString(root, "source");
        if (!string.Equals(source, ExternalNameFrequencyCatalog.IbgeSource, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported source: {source}");

        var sourceVersion = RequiredString(root, "source_version");
        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("entries must be a JSON array.");

        var entries = new List<ExternalNameFrequencyEntry>();
        foreach (var item in entriesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Each frequency entry must be a JSON object.");

            var name = RequiredString(item, "name");
            if (!item.TryGetProperty("occurrences", out var occurrencesElement) ||
                occurrencesElement.ValueKind != JsonValueKind.Number ||
                !occurrencesElement.TryGetInt64(out var occurrences))
            {
                throw new InvalidDataException("occurrences must be an Int64 JSON number.");
            }

            entries.Add(new ExternalNameFrequencyEntry(name, occurrences));
        }

        var snapshot = ExternalNameFrequencyCatalog.CreateIbgeSnapshot(sourceVersion, entries);

        if (root.TryGetProperty("fingerprint_sha256", out var fingerprintElement))
        {
            if (fingerprintElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("fingerprint_sha256 must be a string when present.");

            var declared = fingerprintElement.GetString();
            if (string.IsNullOrWhiteSpace(declared) ||
                !string.Equals(declared.Trim(), snapshot.FingerprintSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Snapshot fingerprint does not match canonical content.");
            }
        }

        return snapshot;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{propertyName} must be a string.");

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{propertyName} is required.");

        return value.Trim();
    }
}

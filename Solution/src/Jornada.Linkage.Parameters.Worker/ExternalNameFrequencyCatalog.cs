using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Contrato somente leitura para frequências agregadas externas de nomes.
/// Não altera scorer, m/u, thresholds nem decisões de identidade.
/// </summary>
public sealed record ExternalNameFrequencyEntry(string Name, long Occurrences);

public sealed record ExternalNameFrequencySnapshot(
    string Source,
    string SourceVersion,
    IReadOnlyList<ExternalNameFrequencyEntry> Entries,
    string FingerprintSha256);

public static class ExternalNameFrequencyCatalog
{
    public const string IbgeSource = "IBGE_NOMES_NO_BRASIL";

    public static ExternalNameFrequencySnapshot CreateIbgeSnapshot(
        string sourceVersion,
        IEnumerable<ExternalNameFrequencyEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion))
            throw new ArgumentException("sourceVersion is required.", nameof(sourceVersion));

        ArgumentNullException.ThrowIfNull(entries);

        var normalized = entries
            .Select(static entry => Normalize(entry))
            .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("At least one frequency entry is required.", nameof(entries));

        var duplicate = normalized
            .GroupBy(static entry => entry.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate normalized name: {duplicate.Key}", nameof(entries));

        var canonical = new StringBuilder()
            .Append(IbgeSource).Append('\n')
            .Append(sourceVersion.Trim()).Append('\n');

        foreach (var entry in normalized)
            canonical.Append(entry.Name).Append('\t').Append(entry.Occurrences).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return new ExternalNameFrequencySnapshot(
            IbgeSource,
            sourceVersion.Trim(),
            normalized,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static ExternalNameFrequencyEntry Normalize(ExternalNameFrequencyEntry entry)
    {
        if (entry is null)
            throw new ArgumentException("Frequency entry cannot be null.");
        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("Name is required.");
        if (entry.Occurrences < 0)
            throw new ArgumentOutOfRangeException(nameof(entry), "Occurrences cannot be negative.");

        // Decisão de escopo #31: somente adaptação técnica de consulta.
        // Não há colapso fonético, remoção semântica de letras ou equivalência probabilística.
        var name = entry.Name.Trim().ToUpperInvariant();
        return new ExternalNameFrequencyEntry(name, entry.Occurrences);
    }
}

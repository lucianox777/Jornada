using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public enum IbgeGeographicScope
{
    Brazil,
    State,
    Municipality
}

public sealed record IbgeTypedNameFrequencyEntry(
    IbgeNameStatisticKind StatisticKind,
    string Name,
    long Occurrences);

public sealed record IbgeTypedNameFrequencySnapshot(
    string Source,
    string SourceVersion,
    IbgeGeographicScope GeographicScope,
    string? GeographicCode,
    IReadOnlyList<IbgeTypedNameFrequencyEntry> Entries,
    string FingerprintSha256)
{
    public bool TryGetOccurrences(IbgeNameStatisticKind statisticKind, string name, out long occurrences)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            occurrences = 0;
            return false;
        }

        var normalized = name.Trim().ToUpperInvariant();
        var entry = Entries.FirstOrDefault(candidate =>
            candidate.StatisticKind == statisticKind &&
            string.Equals(candidate.Name, normalized, StringComparison.Ordinal));

        if (entry is null)
        {
            occurrences = 0;
            return false;
        }

        occurrences = entry.Occurrences;
        return true;
    }
}

/// <summary>
/// Snapshot tipado para uso downstream do IBGE no Calibrador/otimizador.
/// Mantém prenomes e sobrenomes em universos semanticamente distintos e registra
/// explicitamente o recorte territorial. Não altera o snapshot oficial nem cria
/// equivalências fonéticas.
/// </summary>
public static class IbgeTypedNameFrequencyCatalog
{
    public const string MethodVersion = "IBGE_TYPED_NAME_FREQUENCY_V1";

    public static IbgeTypedNameFrequencySnapshot Create(
        string sourceVersion,
        IbgeGeographicScope geographicScope,
        string? geographicCode,
        IEnumerable<IbgeTypedNameFrequencyEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion))
            throw new ArgumentException("sourceVersion is required.", nameof(sourceVersion));
        ArgumentNullException.ThrowIfNull(entries);

        var code = NormalizeScopeCode(geographicScope, geographicCode);
        var normalized = entries
            .Select(Normalize)
            .OrderBy(static item => item.StatisticKind)
            .ThenBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("At least one frequency entry is required.", nameof(entries));

        var duplicate = normalized
            .GroupBy(static item => (item.StatisticKind, item.Name))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Duplicate frequency entry: {duplicate.Key.StatisticKind}/{duplicate.Key.Name}",
                nameof(entries));

        var canonical = new StringBuilder()
            .Append(ExternalNameFrequencyCatalog.IbgeSource).Append('\n')
            .Append(sourceVersion.Trim()).Append('\n')
            .Append(geographicScope).Append('\n')
            .Append(code ?? string.Empty).Append('\n');

        foreach (var entry in normalized)
            canonical.Append(entry.StatisticKind).Append('\t')
                .Append(entry.Name).Append('\t')
                .Append(entry.Occurrences).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return new IbgeTypedNameFrequencySnapshot(
            ExternalNameFrequencyCatalog.IbgeSource,
            sourceVersion.Trim(),
            geographicScope,
            code,
            normalized,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static IbgeTypedNameFrequencyEntry Normalize(IbgeTypedNameFrequencyEntry entry)
    {
        if (entry is null)
            throw new ArgumentException("Frequency entry cannot be null.");
        if (!Enum.IsDefined(entry.StatisticKind))
            throw new ArgumentOutOfRangeException(nameof(entry), "Unknown statistic kind.");
        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("Name is required.");
        if (entry.Occurrences < 0)
            throw new ArgumentOutOfRangeException(nameof(entry), "Occurrences cannot be negative.");

        return entry with { Name = entry.Name.Trim().ToUpperInvariant() };
    }

    private static string? NormalizeScopeCode(IbgeGeographicScope scope, string? code)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        if (scope == IbgeGeographicScope.Brazil)
        {
            if (!string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Brazil scope must not declare a geographic code.", nameof(code));
            return null;
        }

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("State and Municipality scopes require a geographic code.", nameof(code));

        return code.Trim().ToUpperInvariant();
    }
}

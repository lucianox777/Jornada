using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticDailyBirthProvenance(
    string SchemaVersion,
    string Source,
    string ReferencePeriod,
    string Geography,
    string Path,
    string Sha256,
    int RowCount);

public sealed class SyntheticDailyBirthDistribution
{
    public const string Schema = "JORNADA_SYNTH_BIRTH_DAILY_V1";

    private readonly DateOnly[] dates;
    private readonly ulong[] cumulative;
    private readonly ulong total;

    private SyntheticDailyBirthDistribution(
        DateOnly[] dates,
        ulong[] cumulative,
        ulong total,
        SyntheticDailyBirthProvenance provenance)
    {
        this.dates = dates;
        this.cumulative = cumulative;
        this.total = total;
        Provenance = provenance;
    }

    public SyntheticDailyBirthProvenance Provenance { get; }

    public DateOnly Draw(Xoshiro256StarStar random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var target = random.NextUInt64(total) + 1;
        var index = Array.BinarySearch(cumulative, target);
        if (index < 0)
            index = ~index;
        return dates[index];
    }

    public static async Task<SyntheticDailyBirthDistribution> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var schema = RequiredString(root, "schema_version");
        var source = RequiredString(root, "source");
        var referencePeriod = RequiredString(root, "reference_period");
        var geography = RequiredString(root, "geography");
        if (!string.Equals(schema, Schema, StringComparison.Ordinal))
            throw new InvalidDataException($"schema_version de nascimento deve ser {Schema}.");
        if (!root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Distribuição diária sem rows.");

        var entries = new List<(DateOnly Date, ulong Births)>();
        var seen = new HashSet<DateOnly>();
        foreach (var row in rows.EnumerateArray())
        {
            var dateText = RequiredString(row, "date");
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                throw new InvalidDataException($"Data diária inválida: {dateText}.");
            if (!row.TryGetProperty("births", out var birthsElement)
                || !birthsElement.TryGetUInt64(out var births)
                || births == 0)
                throw new InvalidDataException($"Frequência diária inválida em {dateText}.");
            if (!seen.Add(date))
                throw new InvalidDataException($"Data diária duplicada: {dateText}.");
            entries.Add((date, births));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("Distribuição diária vazia.");

        entries.Sort(static (a, b) => a.Date.CompareTo(b.Date));
        var dates = new DateOnly[entries.Count];
        var cumulative = new ulong[entries.Count];
        ulong total = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            checked { total += entries[i].Births; }
            dates[i] = entries[i].Date;
            cumulative[i] = total;
        }

        return new SyntheticDailyBirthDistribution(
            dates,
            cumulative,
            total,
            new SyntheticDailyBirthProvenance(
                schema, source, referencePeriod, geography,
                fullPath, sha, entries.Count));
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Campo obrigatório ausente: {property}.");
        return value.GetString()!;
    }
}

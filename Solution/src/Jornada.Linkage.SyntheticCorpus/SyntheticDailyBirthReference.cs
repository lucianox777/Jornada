using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticDailyBirthManifest(
    int SchemaVersion,
    string ReferenceCode,
    string Source,
    string SourceUrl,
    string DataPath,
    string Sha256,
    long RowCount);

public sealed record SyntheticDailyBirthReference(
    string ReferenceCode,
    string Source,
    string SourceUrl,
    string DataPath,
    string PhysicalSha256,
    long RowCount,
    SyntheticDailyBirthSampler Sampler);

/// <summary>
/// Distribuição diária versionada de nascimentos usada apenas no modo demográfico
/// primário. Não existe fallback uniforme: ausência, hash divergente, linha inválida
/// ou referência vazia falham fechado.
/// </summary>
public static class SyntheticDailyBirthReferenceLoader
{
    public const string MethodVersion = "DAILY_BIRTH_FREQUENCY_REFERENCE_V1";

    public static async Task<SyntheticDailyBirthReference> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullManifest = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifest))
            throw new FileNotFoundException(
                "Modo demográfico primário exige manifesto versionado de frequência diária de nascimentos.",
                fullManifest);

        await using var manifestStream = File.OpenRead(fullManifest);
        var manifest = await JsonSerializer.DeserializeAsync<SyntheticDailyBirthManifest>(
            manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken)
            ?? throw new InvalidDataException("Manifesto de nascimentos vazio.");

        if (manifest.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(manifest.ReferenceCode) ||
            string.IsNullOrWhiteSpace(manifest.Source) ||
            string.IsNullOrWhiteSpace(manifest.SourceUrl) ||
            string.IsNullOrWhiteSpace(manifest.DataPath) ||
            manifest.RowCount <= 0 ||
            manifest.Sha256.Length != 64 ||
            manifest.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Manifesto diário de nascimentos inválido.");

        if (Path.IsPathRooted(manifest.DataPath))
            throw new InvalidDataException("dataPath da referência diária deve ser relativo.");

        var root = Path.GetDirectoryName(fullManifest)
            ?? throw new InvalidDataException("Manifesto diário sem diretório.");
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var dataPath = Path.GetFullPath(
            Path.Combine(root, manifest.DataPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!dataPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("dataPath escapou da raiz do manifesto.");
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("Arquivo diário de nascimentos não encontrado.", dataPath);

        string actualHash;
        await using (var source = File.OpenRead(dataPath))
            actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 da distribuição diária de nascimentos diverge do manifesto.");

        var rows = new List<SyntheticDailyBirthValue>();
        using var reader = new StreamReader(dataPath);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (!string.Equals(header, "date,births", StringComparison.Ordinal))
            throw new InvalidDataException("Cabeçalho diário esperado: date,births.");

        long lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException($"Linha diária vazia: {lineNumber}.");
            var parts = line.Split(',');
            if (parts.Length != 2 ||
                !DateOnly.TryParseExact(
                    parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var births) ||
                births <= 0)
                throw new InvalidDataException($"Linha diária inválida: {lineNumber}.");
            rows.Add(new SyntheticDailyBirthValue(date, births));
        }

        if (rows.Count != manifest.RowCount)
            throw new InvalidDataException(
                $"rowCount diário diverge: esperado={manifest.RowCount}; atual={rows.Count}.");
        if (rows.GroupBy(x => x.Date).Any(g => g.Count() > 1))
            throw new InvalidDataException("Distribuição diária contém data duplicada.");

        return new SyntheticDailyBirthReference(
            manifest.ReferenceCode.Trim(),
            manifest.Source.Trim(),
            manifest.SourceUrl.Trim(),
            manifest.DataPath,
            actualHash,
            rows.Count,
            new SyntheticDailyBirthSampler(rows));
    }
}

public sealed record SyntheticDailyBirthValue(DateOnly Date, long Births);

public sealed class SyntheticDailyBirthSampler
{
    private readonly DateOnly[] dates;
    private readonly ulong[] cumulative;
    private readonly ulong total;

    public SyntheticDailyBirthSampler(IEnumerable<SyntheticDailyBirthValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var rows = values.OrderBy(x => x.Date).ToArray();
        if (rows.Length == 0)
            throw new ArgumentException("Distribuição diária vazia.", nameof(values));

        dates = new DateOnly[rows.Length];
        cumulative = new ulong[rows.Length];
        ulong acc = 0;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].Births <= 0)
                throw new ArgumentOutOfRangeException(nameof(values), "Nascimentos devem ser positivos.");
            acc = checked(acc + (ulong)rows[i].Births);
            dates[i] = rows[i].Date;
            cumulative[i] = acc;
        }
        total = acc;
    }

    public DateOnly Draw(Xoshiro256StarStar random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var target = Math.Min(
            total - 1,
            (ulong)Math.Floor(random.NextUnitInterval() * total));
        var wanted = target + 1;
        var index = Array.BinarySearch(cumulative, wanted);
        if (index < 0)
            index = ~index;
        return dates[index];
    }
}

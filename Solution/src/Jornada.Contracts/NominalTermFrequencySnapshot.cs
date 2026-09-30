namespace Jornada.Contracts;

public sealed record NominalTermFrequencyEntry(
    string Attribute,
    string ValueNormalized,
    long Occurrences,
    long PopulationReference,
    decimal Frequency);

public sealed record NominalTermFrequencyValue(
    string ValueNormalized,
    decimal Frequency);

/// <summary>
/// Snapshot imutável das frequências nominais persistidas com o modelo.
/// Não consulta IBGE/ref no hot path e não infere frequência ausente como zero.
/// </summary>
public sealed class NominalTermFrequencySnapshot
{
    public const string PersonFirstNameAttribute = "NOME_PRENOME";
    public const string MotherFirstNameAttribute = "NOME_MAE_PRENOME";
    public const string ContractVersion = "NOMINAL_TERM_FREQUENCY_SNAPSHOT_V1";

    private readonly IReadOnlyDictionary<string, decimal> personFirst;
    private readonly IReadOnlyDictionary<string, decimal> motherFirst;

    private NominalTermFrequencySnapshot(
        IReadOnlyDictionary<string, decimal> personFirst,
        IReadOnlyDictionary<string, decimal> motherFirst)
    {
        this.personFirst = personFirst;
        this.motherFirst = motherFirst;
        MinimumPublishedFrequency = personFirst.Values
            .Concat(motherFirst.Values)
            .DefaultIfEmpty(1m)
            .Min();
    }

    public int PersonFirstNameCount => personFirst.Count;
    public int MotherFirstNameCount => motherFirst.Count;
    public decimal MinimumPublishedFrequency { get; }

    public IReadOnlyList<NominalTermFrequencyValue> PersonFirstNames =>
        personFirst.OrderBy(static x => x.Key, StringComparer.Ordinal)
            .Select(static x => new NominalTermFrequencyValue(x.Key, x.Value))
            .ToArray();

    public IReadOnlyList<NominalTermFrequencyValue> MotherFirstNames =>
        motherFirst.OrderBy(static x => x.Key, StringComparer.Ordinal)
            .Select(static x => new NominalTermFrequencyValue(x.Key, x.Value))
            .ToArray();

    public static NominalTermFrequencySnapshot Create(IEnumerable<NominalTermFrequencyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var person = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var mother = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Occurrences <= 0 || entry.PopulationReference <= 0
                || entry.Occurrences > entry.PopulationReference)
                throw new InvalidDataException("Frequência nominal contém contagens inválidas.");
            if (entry.Frequency <= 0m || entry.Frequency > 1m)
                throw new InvalidDataException("Frequência nominal deve estar em (0,1].");
            var expected = decimal.Divide(entry.Occurrences, entry.PopulationReference);
            if (Math.Abs(expected - entry.Frequency) > 0.000000000001m)
                throw new InvalidDataException("Frequência nominal diverge de ocorrências/população de referência.");

            var normalized = IdentityComparison.NormalizeText(entry.ValueNormalized)
                ?? throw new InvalidDataException("Valor nominal vazio.");
            var target = entry.Attribute switch
            {
                PersonFirstNameAttribute => person,
                MotherFirstNameAttribute => mother,
                _ => throw new InvalidDataException($"Atributo TF não suportado: {entry.Attribute}.")
            };
            if (!target.TryAdd(normalized, entry.Frequency))
                throw new InvalidDataException($"Frequência nominal duplicada: {entry.Attribute}/{normalized}.");
        }

        return new NominalTermFrequencySnapshot(person, mother);
    }

    public bool TryGetPersonFirstName(string? fullName, out decimal frequency)
        => TryGetFirstToken(personFirst, fullName, out frequency);

    public bool TryGetMotherFirstName(string? fullName, out decimal frequency)
        => TryGetFirstToken(motherFirst, fullName, out frequency);

    private static bool TryGetFirstToken(
        IReadOnlyDictionary<string, decimal> source,
        string? fullName,
        out decimal frequency)
    {
        var normalized = IdentityComparison.NormalizeText(fullName);
        if (normalized is null)
        {
            frequency = default;
            return false;
        }

        var separator = normalized.IndexOf(' ');
        var token = separator < 0 ? normalized : normalized[..separator];
        return source.TryGetValue(token, out frequency);
    }
}

using System.Globalization;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticErrorProfile(
    double NameCorruptionProbability,
    double MotherCorruptionProbability,
    double DateCorruptionProbability,
    double CommonCorruptionProbability,
    double MissingMotherProbability,
    double MissingDateProbability);

public sealed record SyntheticTextCorruption(string Value, string? Operation);
public sealed record SyntheticDateCorruption(DateOnly Value, string? Operation);

/// <summary>
/// Tradução normativa das regras de gen_corpus_v2.py.
/// O PRNG é deliberadamente o Xoshiro congelado do gerador C#; equivalência com Python
/// é de regra/distribuição/invariante, não de sequência de draws.
/// </summary>
public static class SyntheticCorpusV2Rules
{
    public const string RulesetVersion = "JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1";
    public const string DateCorruptionVersion = "EFFECTIVE_DATE_CORRUPTION_V2";

    public static readonly IReadOnlyList<string> SurnameParticles =
        new[] { "da", "de", "do", "dos", "das" };

    public static readonly IReadOnlyDictionary<string, SyntheticErrorProfile> Profiles =
        new Dictionary<string, SyntheticErrorProfile>(StringComparer.Ordinal)
        {
            ["clean"] = new(.06, .10, .04, .00, .05, .01),
            ["independent"] = new(.22, .30, .15, .00, .18, .05),
            ["correlated"] = new(.12, .16, .08, .18, .18, .05),
            ["field"] = new(.35, .45, .28, .22, .40, .12)
        };

    public static bool NextBernoulli(Xoshiro256StarStar random, double probability)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (double.IsNaN(probability) || probability < 0 || probability > 1)
            throw new ArgumentOutOfRangeException(nameof(probability), "Probabilidade deve estar em [0,1].");
        return random.NextUnitInterval() < probability;
    }

    public static T Choose<T>(Xoshiro256StarStar random, IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("Coleção vazia.", nameof(values));
        return values[random.NextInt32(values.Count)];
    }

    public static T WeightedChoice<T>(
        Xoshiro256StarStar random,
        IReadOnlyList<T> values,
        IReadOnlyList<ulong> weights)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(weights);
        if (values.Count == 0 || values.Count != weights.Count)
            throw new ArgumentException("Valores/pesos inválidos.");

        ulong total = 0;
        foreach (var weight in weights)
        {
            if (weight == 0)
                throw new ArgumentException("Peso deve ser positivo.", nameof(weights));
            total = checked(total + weight);
        }

        var target = random.NextUInt64(total);
        ulong cumulative = 0;
        for (var i = 0; i < values.Count; i++)
        {
            cumulative += weights[i];
            if (target < cumulative)
                return values[i];
        }

        throw new InvalidOperationException("Amostragem ponderada inconsistente.");
    }

    public static IReadOnlyList<T> SampleWithoutReplacement<T>(
        Xoshiro256StarStar random,
        IReadOnlyList<T> values,
        int count)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (count < 0 || count > values.Count)
            throw new ArgumentOutOfRangeException(nameof(count));

        var copy = values.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = i + random.NextInt32(copy.Length - i);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy.Take(count).ToArray();
    }

    public static string GenerateCpf(Xoshiro256StarStar random)
    {
        while (true)
        {
            var digits = Enumerable.Range(0, 9).Select(_ => random.NextInt32(10)).ToArray();
            if (digits.Distinct().Count() == 1)
                continue;

            var sum1 = 0;
            for (var i = 0; i < 9; i++)
                sum1 += digits[i] * (10 - i);
            var dv1Candidate = 11 - (sum1 % 11);
            var dv1 = dv1Candidate >= 10 ? 0 : dv1Candidate;

            var sum2 = 0;
            for (var i = 0; i < 9; i++)
                sum2 += digits[i] * (11 - i);
            sum2 += dv1 * 2;
            var dv2Candidate = 11 - (sum2 % 11);
            var dv2 = dv2Candidate >= 10 ? 0 : dv2Candidate;

            return string.Concat(digits.Select(x => x.ToString(CultureInfo.InvariantCulture)))
                + dv1.ToString(CultureInfo.InvariantCulture)
                + dv2.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static string GenerateCns(Xoshiro256StarStar random)
    {
        while (true)
        {
            var prefix = new int[14];
            prefix[0] = Choose(random, new[] { 7, 8, 9 });
            for (var i = 1; i < prefix.Length; i++)
                prefix[i] = random.NextInt32(10);

            var partial = 0;
            for (var i = 0; i < prefix.Length; i++)
                partial += prefix[i] * (15 - i);

            var last = ((-partial % 11) + 11) % 11;
            if (last > 9)
                continue;

            return string.Concat(prefix.Select(x => x.ToString(CultureInfo.InvariantCulture)))
                + last.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static string? InvalidateCheckDigit(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (!char.IsAsciiDigit(value[^1]))
            throw new ArgumentException("Identificador deve terminar em dígito.", nameof(value));

        var last = (value[^1] - '0' + 1) % 10;
        return value[..^1] + last.ToString(CultureInfo.InvariantCulture);
    }

    public static SyntheticTextCorruption CorruptText(string value, Xoshiro256StarStar random)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < 4)
            return new(value, null);

        var operations = new[] { "DROP_CHAR", "SWAP_ADJACENT", "DOUBLE_CHAR", "TRUNCATE" };
        var operation = Choose(random, operations);

        if (operation == "DROP_CHAR")
        {
            var i = 1 + random.NextInt32(value.Length - 2);
            return new(value.Remove(i, 1), operation);
        }

        if (operation == "SWAP_ADJACENT" && value.Length >= 5)
        {
            var i = 1 + random.NextInt32(value.Length - 3);
            var chars = value.ToCharArray();
            (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            var changed = new string(chars);
            return changed == value ? new(value, null) : new(changed, operation);
        }

        if (operation == "DOUBLE_CHAR")
        {
            var i = 1 + random.NextInt32(value.Length - 2);
            return new(value.Insert(i, value[i].ToString()), operation);
        }

        if (value.Length >= 8)
        {
            var length = 5 + random.NextInt32(value.Length - 6);
            return new(value[..length], operation);
        }

        return new(value, null);
    }

    public static SyntheticDateCorruption CorruptDate(DateOnly value, Xoshiro256StarStar random)
    {
        // O Python V2 podia escolher DATE_TRANSPOSE para day > 12 e devolver a data original,
        // reduzindo a taxa efetiva de erro. O porte preserva a intenção: operação escolhida
        // sempre deve alterar a data, selecionando apenas candidatos válidos/efetivos.
        var operations = new List<string> { "DATE_YEAR", "DATE_DAY", "DATE_HEAPING" };
        if (value.Day <= 12 && value.Day != value.Month)
            operations.Add("DATE_TRANSPOSE");

        var operation = Choose(random, operations);
        return operation switch
        {
            "DATE_TRANSPOSE" => new(new DateOnly(value.Year, value.Day, value.Month), operation),
            "DATE_YEAR" => CorruptYear(value, random),
            "DATE_DAY" => CorruptDay(value, random),
            "DATE_HEAPING" => CorruptHeaping(value, random),
            _ => throw new InvalidOperationException("Operação de data desconhecida.")
        };
    }

    private static SyntheticDateCorruption CorruptYear(DateOnly value, Xoshiro256StarStar random)
    {
        var candidates = new List<DateOnly>();
        foreach (var delta in new[] { -10, -1, 1, 10 })
        {
            try
            {
                candidates.Add(new DateOnly(value.Year + delta, value.Month, value.Day));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ex.: 29/02 em ano não bissexto. Apenas candidatos efetivos participam.
            }
        }

        // Em 29/02 os deslocamentos padrão (-10, -1, +1, +10) podem ser todos
        // inválidos. Preserve a distribuição original quando houver candidatos;
        // somente nesse caso-limite, tente os anos bissextos mais próximos.
        if (candidates.Count == 0 && value.Month == 2 && value.Day == 29)
        {
            foreach (var delta in new[] { -4, 4 })
            {
                try
                {
                    candidates.Add(new DateOnly(value.Year + delta, value.Month, value.Day));
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Ex.: 2100 não é bissexto; respeite também os limites de DateOnly.
                }
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("Nenhuma corrupção de ano válida.");
        return new(Choose(random, candidates), "DATE_YEAR");
    }

    private static SyntheticDateCorruption CorruptDay(DateOnly value, Xoshiro256StarStar random)
    {
        var candidates = new[] { -1, 1 }
            .Select(delta => Math.Clamp(value.Day + delta, 1, 28))
            .Where(day => day != value.Day)
            .Distinct()
            .Select(day => new DateOnly(value.Year, value.Month, day))
            .ToArray();

        if (candidates.Length == 0)
        {
            var fallback = value.Day == 1 ? 2 : value.Day - 1;
            return new(new DateOnly(value.Year, value.Month, fallback), "DATE_DAY");
        }

        return new(Choose(random, candidates), "DATE_DAY");
    }

    private static SyntheticDateCorruption CorruptHeaping(DateOnly value, Xoshiro256StarStar random)
    {
        var candidates = new[] { 1, 15 }
            .Where(day => day != value.Day)
            .Select(day => new DateOnly(value.Year, value.Month, day))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException("Nenhuma corrupção de heaping válida.");
        return new(Choose(random, candidates), "DATE_HEAPING");
    }
}

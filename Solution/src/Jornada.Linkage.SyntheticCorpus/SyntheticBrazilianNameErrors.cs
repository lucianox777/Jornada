using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Taxonomia experimental inspirada nas classes de problemas do nomesbr/Ipea 0.1.1.
/// Os números são parâmetros sintéticos explícitos, NÃO taxas empíricas do pacote.
/// Nenhuma transformação altera a Pessoa verdadeira ou normaliza o nome operacional.
/// </summary>
public sealed record SyntheticBrazilianNameErrorRates
{
    public double DuplicateLetter { get; init; }
    public double DuplicateParticle { get; init; }
    public double SplitApostrophe { get; init; }
    public double PrefixTitle { get; init; }
    public double AdministrativeMarker { get; init; }
    public double OmitAgnome { get; init; }
    public double AbbreviateAgnome { get; init; }

    public void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(DuplicateLetter), DuplicateLetter),
            (nameof(DuplicateParticle), DuplicateParticle),
            (nameof(SplitApostrophe), SplitApostrophe),
            (nameof(PrefixTitle), PrefixTitle),
            (nameof(AdministrativeMarker), AdministrativeMarker),
            (nameof(OmitAgnome), OmitAgnome),
            (nameof(AbbreviateAgnome), AbbreviateAgnome)
        })
        {
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(name, "Probabilidade sintética deve estar em [0,1].");
        }
    }
}

public sealed record SyntheticBrazilianNameErrorConfig
{
    public const string ExperimentVersion = "SYNTHETIC_BRAZILIAN_NAME_ERRORS_V1";

    public string Version { get; init; } = ExperimentVersion;
    public double AgnomeBasePrevalence { get; init; }
    public SyntheticBrazilianNameErrorRates Default { get; init; } = new();
    public Dictionary<string, SyntheticBrazilianNameErrorRates> ByGestor { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SyntheticBrazilianNameErrorRates> ByCpfStratum { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SyntheticBrazilianNameErrorRates> ByGestorAndCpfStratum { get; init; } = new(StringComparer.Ordinal);

    public void Validate(int gestores)
    {
        if (Version != ExperimentVersion)
            throw new ArgumentException("Versão experimental de taxonomia não reconhecida.", nameof(Version));
        if (!double.IsFinite(AgnomeBasePrevalence) || AgnomeBasePrevalence < 0 || AgnomeBasePrevalence > 1)
            throw new ArgumentOutOfRangeException(nameof(AgnomeBasePrevalence));
        ArgumentNullException.ThrowIfNull(Default);
        ArgumentNullException.ThrowIfNull(ByGestor);
        ArgumentNullException.ThrowIfNull(ByCpfStratum);
        ArgumentNullException.ThrowIfNull(ByGestorAndCpfStratum);
        Default.Validate();
        foreach (var (key, value) in ByGestor)
        {
            ValidateGestor(key, gestores);
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
        }
        foreach (var (key, value) in ByCpfStratum)
        {
            ValidateStratum(key);
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
        }
        foreach (var (key, value) in ByGestorAndCpfStratum)
        {
            var parts = key.Split('/');
            if (parts.Length != 2)
                throw new ArgumentException("Estrato combinado deve ser G<n>/WITH_CPF ou G<n>/WITHOUT_CPF.");
            ValidateGestor(parts[0], gestores);
            ValidateStratum(parts[1]);
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
        }
    }

    private static void ValidateGestor(string key, int gestores)
    {
        if (!key.StartsWith('G') || !int.TryParse(key.AsSpan(1), out var index)
            || index < 0 || index >= gestores || key != $"G{index}")
            throw new ArgumentException($"Gestor sintético desconhecido: {key}.");
    }

    private static void ValidateStratum(string key)
    {
        if (key is not ("WITH_CPF" or "WITHOUT_CPF"))
            throw new ArgumentException($"Estrato sintético desconhecido: {key}.");
    }

    /// <summary>
    /// Prioridade por substituição INTEGRAL das taxas: combinação > gestor > CPF observado > Default.
    /// CPF é o observado após retenção, não o CPF da verdade. Zero em override é zero explícito.
    /// </summary>
    public SyntheticBrazilianNameErrorRates Resolve(string gestor, bool observedCpf)
    {
        var stratum = observedCpf ? "WITH_CPF" : "WITHOUT_CPF";
        if (ByGestorAndCpfStratum.TryGetValue($"{gestor}/{stratum}", out var combined))
            return combined;
        if (ByGestor.TryGetValue(gestor, out var perGestor))
            return perGestor;
        return ByCpfStratum.TryGetValue(stratum, out var perStratum) ? perStratum : Default;
    }

    public string CanonicalJson()
    {
        return JsonSerializer.Serialize(new
        {
            Version,
            AgnomeBasePrevalence,
            Default,
            ByGestor = ByGestor.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { x.Key, x.Value }).ToArray(),
            ByCpfStratum = ByCpfStratum.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { x.Key, x.Value }).ToArray(),
            ByGestorAndCpfStratum = ByGestorAndCpfStratum.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { x.Key, x.Value }).ToArray()
        });
    }

    public string ConfigSha256()
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson())));

    public static SyntheticBrazilianNameErrorConfig ReadFile(string path)
    {
        var config = JsonSerializer.Deserialize<SyntheticBrazilianNameErrorConfig>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return config ?? throw new InvalidDataException("Configuração de erros brasileiros vazia.");
    }
}

public static class SyntheticBrazilianNameErrors
{
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DA", "DAS", "DE", "DO", "DOS", "DI", "DU", "DEL", "D"
    };

    public static string? AppendTrueAgnome(string fullName, Xoshiro256StarStar random, double prevalence)
    {
        if (prevalence <= 0 || !SyntheticCorpusV2Rules.NextBernoulli(random, prevalence))
            return null;
        return fullName + " " + SyntheticCorpusV2Rules.Choose(random, new[] { "FILHO", "JUNIOR", "NETO" });
    }

    public static void Apply(
        SyntheticObservation observation,
        Xoshiro256StarStar random,
        SyntheticBrazilianNameErrorConfig config)
    {
        var rates = config.Resolve(observation.Gestor, observation.Cpf is not null);
        var labels = string.IsNullOrEmpty(observation.Corruptions)
            ? new List<string>()
            : observation.Corruptions.Split('|').ToList();
        observation.Name = ApplyField(observation.Name, "NOME_BR_", rates, random, labels);
        observation.MotherName = ApplyField(observation.MotherName, "MAE_BR_", rates, random, labels);
        observation.Corruptions = string.Join("|", labels);
    }

    private static string? ApplyField(
        string? current,
        string prefix,
        SyntheticBrazilianNameErrorRates rates,
        Xoshiro256StarStar random,
        List<string> labels)
    {
        if (string.IsNullOrEmpty(current))
            return current;

        // A elegibilidade é observada após cada operação; tentativas sem mudança não geram rótulo.
        Try(rates.DuplicateLetter, "DUPLICATE_LETTER", value =>
        {
            var positions = Enumerable.Range(1, Math.Max(0, value.Length - 2))
                .Where(i => char.IsLetter(value[i])).ToArray();
            if (positions.Length == 0) return value;
            var index = positions[random.NextInt32(positions.Length)];
            return value.Insert(index, value[index].ToString());
        });
        Try(rates.DuplicateParticle, "DUPLICATE_PARTICLE", value =>
        {
            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eligible = Enumerable.Range(1, Math.Max(0, words.Length - 1)).Where(i => Particles.Contains(words[i])).ToArray();
            if (eligible.Length == 0) return value;
            var index = eligible[random.NextInt32(eligible.Length)];
            return string.Join(" ", words.Take(index + 1).Concat(new[] { words[index] }).Concat(words.Skip(index + 1)));
        });
        Try(rates.SplitApostrophe, "SPLIT_APOSTROPHE", value =>
        {
            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eligible = Enumerable.Range(0, words.Length)
                .Where(i => words[i].Length >= 4 && words[i].All(char.IsLetter)).ToArray();
            if (eligible.Length == 0) return value;
            var index = eligible[random.NextInt32(eligible.Length)];
            var midpoint = Math.Max(2, words[index].Length / 2);
            words[index] = words[index].Insert(midpoint, "'");
            return string.Join(" ", words);
        });
        Try(rates.PrefixTitle, "PREFIX_TITLE", value =>
            BrazilianNameComponents.Project(value)?.TitlePrefix is not null
                ? value : SyntheticCorpusV2Rules.Choose(random, new[] { "DR", "SGTO" }) + " " + value);
        Try(rates.OmitAgnome, "OMIT_AGNOME", value =>
            BrazilianNameComponents.Project(value)?.Agnome is null
                ? value : value[..value.LastIndexOf(' ')]);
        Try(rates.AbbreviateAgnome, "ABBREVIATE_AGNOME", value =>
        {
            var agnome = BrazilianNameComponents.Project(value)?.Agnome;
            var abbreviation = agnome switch { "FILHO" => "FL", "JUNIOR" => "JR", _ => null };
            return abbreviation is null ? value : value[..value.LastIndexOf(' ')] + " " + abbreviation;
        });
        // Marcadores são terminais: não recebem novas mutações no mesmo campo.
        Try(rates.AdministrativeMarker, "ADMINISTRATIVE_MARKER", _ => "NAO INFORMADO");
        return current;

        void Try(double probability, string label, Func<string, string> change)
        {
            if (probability <= 0 || !SyntheticCorpusV2Rules.NextBernoulli(random, probability))
                return;
            var updated = change(current);
            if (updated != current)
            {
                current = updated;
                labels.Add(prefix + label);
            }
        }
    }
}

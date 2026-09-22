using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Erros ADICIONAIS por estrato de CPF observado, após retenção do CPF.
/// Não muda as probabilidades nem a sequência de PRNG do corpus V2 sem opt-in.
/// Nenhuma taxa deste experimento deve ser interpretada como observação real.
/// </summary>
public sealed record SyntheticStratifiedErrorRates
{
    public double NameCorruption { get; init; }
    public double MotherCorruption { get; init; }
    public double DateCorruption { get; init; }
    public double MissingMother { get; init; }
    public double MissingDate { get; init; }

    public void Validate()
    {
        foreach (var (name, rate) in new[]
        {
            (nameof(NameCorruption), NameCorruption),
            (nameof(MotherCorruption), MotherCorruption),
            (nameof(DateCorruption), DateCorruption),
            (nameof(MissingMother), MissingMother),
            (nameof(MissingDate), MissingDate)
        })
            if (!double.IsFinite(rate) || rate < 0 || rate > 1)
                throw new ArgumentOutOfRangeException(name, "Taxa adicional sintética deve estar em [0,1].");
    }
}

public sealed record SyntheticStratifiedErrorConfig
{
    public const string ExperimentVersion = "SYNTHETIC_CPF_STRATIFIED_ERROR_OVERLAY_V1";
    public string Version { get; init; } = ExperimentVersion;
    public SyntheticStratifiedErrorRates Default { get; init; } = new();
    public Dictionary<string, SyntheticStratifiedErrorRates> ByGestor { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SyntheticStratifiedErrorRates> ByCpfStratum { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SyntheticStratifiedErrorRates> ByGestorAndCpfStratum { get; init; } = new(StringComparer.Ordinal);

    public void Validate(int gestores)
    {
        if (Version != ExperimentVersion) throw new ArgumentException("Versão de overlay desconhecida.", nameof(Version));
        ArgumentNullException.ThrowIfNull(Default);
        ArgumentNullException.ThrowIfNull(ByGestor);
        ArgumentNullException.ThrowIfNull(ByCpfStratum);
        ArgumentNullException.ThrowIfNull(ByGestorAndCpfStratum);
        Default.Validate();
        foreach (var (key, rate) in ByGestor) { CheckGestor(key, gestores); ArgumentNullException.ThrowIfNull(rate); rate.Validate(); }
        foreach (var (key, rate) in ByCpfStratum) { CheckStratum(key); ArgumentNullException.ThrowIfNull(rate); rate.Validate(); }
        foreach (var (key, rate) in ByGestorAndCpfStratum)
        {
            var parts = key.Split('/');
            if (parts.Length != 2) throw new ArgumentException($"Estrato combinado inválido: {key}.");
            CheckGestor(parts[0], gestores);
            CheckStratum(parts[1]);
            ArgumentNullException.ThrowIfNull(rate);
            rate.Validate();
        }
    }

    public SyntheticStratifiedErrorRates Resolve(string gestor, bool hasObservedCpf)
    {
        var stratum = hasObservedCpf ? "WITH_CPF" : "WITHOUT_CPF";
        if (ByGestorAndCpfStratum.TryGetValue($"{gestor}/{stratum}", out var combined)) return combined;
        if (ByGestor.TryGetValue(gestor, out var gestorRates)) return gestorRates;
        return ByCpfStratum.TryGetValue(stratum, out var rates) ? rates : Default;
    }

    public string CanonicalJson() => JsonSerializer.Serialize(new
    {
        Version,
        Default,
        ByGestor = ByGestor.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { x.Key, x.Value }).ToArray(),
        ByCpfStratum = ByCpfStratum.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { x.Key, x.Value }).ToArray(),
        ByGestorAndCpfStratum = ByGestorAndCpfStratum.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { x.Key, x.Value }).ToArray()
    });

    public string ConfigSha256()
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(CanonicalJson())));

    public static SyntheticStratifiedErrorConfig ReadFile(string path)
        => JsonSerializer.Deserialize<SyntheticStratifiedErrorConfig>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Configuração de estratos vazia.");

    private static void CheckGestor(string key, int gestores)
    {
        if (!key.StartsWith('G') || !int.TryParse(key.AsSpan(1), out var n)
            || n < 0 || n >= gestores || key != $"G{n}")
            throw new ArgumentException($"Gestor sintético inválido: {key}.");
    }

    private static void CheckStratum(string key)
    {
        if (key is not ("WITH_CPF" or "WITHOUT_CPF"))
            throw new ArgumentException($"Estrato de CPF inválido: {key}.");
    }
}

public static class SyntheticStratifiedErrorOverlay
{
    public static void Apply(
        SyntheticObservation observation,
        Xoshiro256StarStar random,
        SyntheticStratifiedErrorConfig config)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var rates = config.Resolve(observation.Gestor, observation.Cpf is not null);
        var labels = string.IsNullOrEmpty(observation.Corruptions)
            ? new List<string>() : observation.Corruptions.Split('|').ToList();
        if (observation.Name is not null
            && SyntheticCorpusV2Rules.NextBernoulli(random, rates.NameCorruption))
        {
            var changed = SyntheticCorpusV2Rules.CorruptText(observation.Name, random);
            if (changed.Operation is not null && changed.Value != observation.Name)
            {
                observation.Name = changed.Value;
                labels.Add("NOME_STRAT_" + changed.Operation);
            }
        }

        if (observation.MotherName is not null
            && SyntheticCorpusV2Rules.NextBernoulli(random, rates.MotherCorruption))
        {
            var changed = SyntheticCorpusV2Rules.CorruptText(observation.MotherName, random);
            if (changed.Operation is not null && changed.Value != observation.MotherName)
            {
                observation.MotherName = changed.Value;
                labels.Add("MAE_STRAT_" + changed.Operation);
            }
        }

        if (observation.BirthDate is { } date
            && SyntheticCorpusV2Rules.NextBernoulli(random, rates.DateCorruption))
        {
            var changed = SyntheticCorpusV2Rules.CorruptDate(date, random);
            observation.BirthDate = changed.Value;
            labels.Add("DATE_STRAT_" + changed.Operation);
        }

        if (observation.MotherName is not null
            && SyntheticCorpusV2Rules.NextBernoulli(random, rates.MissingMother))
        {
            observation.MotherName = null;
            labels.Add("MAE_STRAT_MISSING");
        }

        if (observation.BirthDate is not null
            && SyntheticCorpusV2Rules.NextBernoulli(random, rates.MissingDate))
        {
            observation.BirthDate = null;
            labels.Add("DATE_STRAT_MISSING");
        }

        observation.Corruptions = string.Join("|", labels);
    }
}

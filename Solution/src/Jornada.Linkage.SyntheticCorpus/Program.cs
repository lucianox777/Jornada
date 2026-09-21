using System.Globalization;
using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

if (args.Length == 0)
{
    PrintUsage();
    return 64;
}

if (string.Equals(args[0], "fingerprint-inputs", StringComparison.Ordinal))
{
    var referenceRoot = args.Length >= 2
        ? args[1]
        : Path.Combine(AppContext.BaseDirectory, "data", "reference", "ibge-nomes-2022");
    var seed = args.Length >= 3
        ? ulong.Parse(args[2], NumberStyles.None, CultureInfo.InvariantCulture)
        : 42UL;

    var manifest = await IbgeProjectionReader.ReadManifestAsync(referenceRoot);
    var fingerprint = SyntheticCorpusInputIdentity.ComputeFingerprint(seed, manifest.Files);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        generatorVersion = SyntheticCorpusInputIdentity.GeneratorVersion,
        rulesetVersion = SyntheticCorpusV2Rules.RulesetVersion,
        rngVersion = Xoshiro256StarStar.AlgorithmVersion,
        seed,
        referenceCode = manifest.ReferenceCode,
        inputFingerprintSha256 = fingerprint,
        files = manifest.Files
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => new
            {
                x.Path,
                x.Kind,
                x.Required,
                sha256 = x.Sha256.ToUpperInvariant(),
                canonicalContentSha256 = x.CanonicalContentSha256.ToUpperInvariant(),
                x.RowCount
            })
    }));
    return 0;
}

if (string.Equals(args[0], "generate", StringComparison.Ordinal))
{
    var values = ParseNamedArguments(args.Skip(1).ToArray());
    var referenceRoot = Get(values, "reference-root")
        ?? Path.Combine(AppContext.BaseDirectory, "data", "reference", "ibge-nomes-2022");
    var output = Get(values, "out") ?? Path.Combine(Environment.CurrentDirectory, "corpus-v2-csharp");

    var options = new SyntheticCorpusOptions(
        People: GetInt(values, "people", 20_000),
        Seed: GetUlong(values, "seed", 42),
        ErrorProfile: Get(values, "error-profile") ?? "correlated",
        MinFrequency: GetLong(values, "min-freq", 20),
        TailOversample: GetDouble(values, "tail-oversample", 1.0),
        CpfBasePrevalence: GetDouble(values, "cpf-base-prevalence", .22),
        CnsBasePrevalence: GetDouble(values, "cns-base-prevalence", .55),
        CpfObservationRetention: GetDouble(values, "cpf-observation-retention", .55),
        CnsObservationRetention: GetDouble(values, "cns-observation-retention", .70),
        CnsInvalidRate: GetDouble(values, "cns-invalid-rate", .01),
        CnsReuseRate: GetDouble(values, "cns-reuse-rate", .01),
        CnsDobConflictRate: GetDouble(values, "cns-dob-conflict-rate", .01),
        Gestores: GetInt(values, "gestores", 4));
    options.Validate();

    var manifest = await IbgeProjectionReader.ReadManifestAsync(referenceRoot);
    var inputFingerprint = SyntheticCorpusInputIdentity.ComputeFingerprint(options.Seed, manifest.Files);
    var nominal = await SyntheticCorpusSourceLoader.LoadBrasilTotalAsync(referenceRoot, options);
    var generator = new SyntheticCorpusGenerator(nominal.FirstNames, nominal.Surnames);
    var generation = generator.Generate(options);
    var materialized = await SyntheticCorpusMaterializer.WriteAsync(
        output,
        generation,
        nominal,
        inputFingerprint);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        generatorVersion = SyntheticCorpusInputIdentity.GeneratorVersion,
        rulesetVersion = SyntheticCorpusV2Rules.RulesetVersion,
        rngVersion = Xoshiro256StarStar.AlgorithmVersion,
        options.Seed,
        people = generation.People.Count,
        observations = generation.Observations.Count,
        inputFingerprintSha256 = inputFingerprint,
        outputDirectory = Path.GetFullPath(output),
        outputs = materialized.OutputSha256,
        empiricalMExact = generation.EmpiricalMExact
    }, SyntheticCorpusCliJson.Indented));
    return 0;
}

PrintUsage();
return 64;

static Dictionary<string, string> ParseNamedArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < values.Length; i++)
    {
        var token = values[i];
        if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length <= 2)
            throw new ArgumentException($"Argumento inválido: {token}.");
        if (i + 1 >= values.Length || values[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Valor ausente para {token}.");

        var key = token[2..];
        if (!result.TryAdd(key, values[++i]))
            throw new ArgumentException($"Argumento duplicado: --{key}.");
    }
    return result;
}

static string? Get(IReadOnlyDictionary<string, string> values, string key)
    => values.TryGetValue(key, out var value) ? value : null;

static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    => values.TryGetValue(key, out var value)
        ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
        : fallback;

static long GetLong(IReadOnlyDictionary<string, string> values, string key, long fallback)
    => values.TryGetValue(key, out var value)
        ? long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
        : fallback;

static ulong GetUlong(IReadOnlyDictionary<string, string> values, string key, ulong fallback)
    => values.TryGetValue(key, out var value)
        ? ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
        : fallback;

static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
    => values.TryGetValue(key, out var value)
        ? double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
        : fallback;

static void PrintUsage()
{
    Console.Error.WriteLine(
        """
        uso:
          Jornada.Linkage.SyntheticCorpus fingerprint-inputs [referenceRoot] [seed]

          Jornada.Linkage.SyntheticCorpus generate
            [--reference-root <dir>]
            [--out <dir>]
            [--people <N>]
            [--seed <N>]
            [--error-profile clean|independent|correlated|field]
            [--min-freq <N>]
            [--tail-oversample <N>]
            [--cpf-base-prevalence <0..1>]
            [--cns-base-prevalence <0..1>]
            [--cpf-observation-retention <0..1>]
            [--cns-observation-retention <0..1>]
            [--cns-invalid-rate <0..1>]
            [--cns-reuse-rate <0..1>]
            [--cns-dob-conflict-rate <0..1>]
            [--gestores <N>]
        """);
}


internal static class SyntheticCorpusCliJson
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

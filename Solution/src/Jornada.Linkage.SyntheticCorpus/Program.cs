using System.Globalization;
using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

if (args.Length < 1 || !string.Equals(args[0], "fingerprint-inputs", StringComparison.Ordinal))
{
    Console.Error.WriteLine("uso: Jornada.Linkage.SyntheticCorpus fingerprint-inputs [referenceRoot] [seed]");
    return 64;
}

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

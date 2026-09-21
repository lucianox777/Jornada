using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticIngestionBridgeMaterialization(
    string OutputDirectory,
    string ManifestPath,
    string TruthPath,
    IReadOnlyList<string> PackagePaths,
    string ManifestSha256,
    string TruthSha256);

public static class SyntheticIngestionBridgeMaterializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly byte[] NewLine = [(byte)'\n'];
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJson = new();

    public static async Task<SyntheticIngestionBridgeMaterialization> WriteAsync(
        string outputDirectory,
        SyntheticIngestionBridgeResult result,
        SyntheticIngestionBridgeOptions options,
        string corpusInputFingerprintSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusInputFingerprintSha256);

        Directory.CreateDirectory(outputDirectory);
        var packagePaths = new List<string>();
        foreach (var package in result.Packages.OrderBy(x => x.FileName, StringComparer.Ordinal))
        {
            var path = Path.Combine(outputDirectory, package.FileName);
            await File.WriteAllBytesAsync(path, package.Bytes, cancellationToken);
            packagePaths.Add(path);
        }

        var truthPath = Path.Combine(outputDirectory, "bridge-truth.jsonl");
        await using (var truth = new FileStream(
                         truthPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         useAsync: true))
        {
            foreach (var row in result.TruthRows)
            {
                await JsonSerializer.SerializeAsync(truth, row, CompactJson, cancellationToken);
                await truth.WriteAsync(NewLine, cancellationToken);
            }
        }

        var truthSha = await HashFileAsync(truthPath, cancellationToken);
        var manifest = new
        {
            schemaVersion = 1,
            bridgeVersion = result.BridgeVersion,
            generatorVersion = SyntheticCorpusInputIdentity.GeneratorVersion,
            rulesetVersion = SyntheticCorpusV2Rules.RulesetVersion,
            rngVersion = Xoshiro256StarStar.AlgorithmVersion,
            sourceObservationCount = result.SourceObservationCount,
            materializedObservationCount = result.MaterializedObservationCount,
            excludedObservationCount = result.ExcludedObservationCount,
            pessoaSchemaVersao = options.PessoaSchemaVersao,
            dataReferencia = options.DataReferencia,
            corpusInputFingerprintSha256 = corpusInputFingerprintSha256.ToUpperInvariant(),
            pseudonymizationKeySha256 = result.PseudonymizationKeySha256.ToUpperInvariant(),
            truthSidecar = new
            {
                path = "bridge-truth.jsonl",
                sha256 = truthSha.ToUpperInvariant(),
                containsSyntheticGroundTruth = true,
                allowedForScoring = false
            },
            exclusionPolicy = new
            {
                activeContractMissingBirthDate = SyntheticIngestionBridge.MissingBirthDateReason,
                fabricationForbidden = true
            },
            routes = options.EffectiveRoutes
                .OrderBy(x => x.SyntheticGestor, StringComparer.Ordinal)
                .Select(x => new
                {
                    syntheticGestor = x.SyntheticGestor,
                    gestorCodigo = x.GestorCodigo,
                    codigoSistemaOrigem = x.CodigoSistemaOrigem
                })
                .ToArray(),
            packages = result.Packages
                .OrderBy(x => x.FileName, StringComparer.Ordinal)
                .Select(x => new
                {
                    x.SyntheticGestor,
                    x.GestorCodigo,
                    x.CodigoSistemaOrigem,
                    x.PessoaSchemaVersao,
                    x.FileName,
                    sha256 = x.Sha256.ToUpperInvariant(),
                    x.PeopleCount
                })
                .ToArray(),
            materializedEmpiricalMExact = result.MaterializedEmpiricalM
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => new
                    {
                        eligiblePairs = x.Value.EligiblePairs,
                        exactPairs = x.Value.ExactPairs,
                        mExactEmpirical = x.Value.MExactEmpirical,
                        mExactEmpiricalReweighted = x.Value.MExactEmpiricalReweighted
                    },
                    StringComparer.Ordinal)
        };

        var manifestPath = Path.Combine(outputDirectory, "bridge-manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, IndentedJson) + "\n";
        await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken);
        var manifestSha = await HashFileAsync(manifestPath, cancellationToken);

        return new SyntheticIngestionBridgeMaterialization(
            Path.GetFullPath(outputDirectory),
            manifestPath,
            truthPath,
            packagePaths,
            manifestSha,
            truthSha);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}

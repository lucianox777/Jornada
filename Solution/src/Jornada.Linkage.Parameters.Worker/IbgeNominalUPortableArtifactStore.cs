using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IbgeNominalUPortableArtifactEntry(
    string FirstNameSex,
    IbgeNominalUBootstrapEstimate Estimate);

public sealed record IbgeNominalUPortableArtifact(
    string SchemaVersion,
    string ReferenceCode,
    string SourceContentSha256,
    string ComparisonContract,
    IbgeNominalUPortableArtifactEntry PersonName,
    IbgeNominalUPortableArtifactEntry MotherName,
    string ArtifactSha256);

/// <summary>
/// Artefato portátil do derivado Monte Carlo IBGE. A referência pública e o algoritmo
/// são imutáveis/versionados, portanto o resultado pode ser calculado uma vez e importado
/// em bancos DEV novos sem repetir milhões de comparações. O hash do artefato usa somente
/// identificadores estáveis da fonte e do método; o hash persistido em SQL continua sendo
/// recalculado com o ID local da referência pelo IbgeNominalUReferenceStore.
/// </summary>
public static class IbgeNominalUPortableArtifactStore
{
    public const string SchemaVersion = "JORNADA_IBGE_NOMINAL_U_PORTABLE_V1";
    public const string ExportOperation = "EXPORT_IBGE_NOMINAL_U_ARTIFACT";
    public const string DefaultRelativePath =
        "data/reference/ibge-nomes-2022/derived/ibge-nominal-u-portable-v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ResolveDefaultPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DefaultRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static async Task<bool> TryImportAsync(
        SqlConnection connection,
        IbgeNominalUReferenceInfo source,
        int seed,
        int pairCount,
        string? path,
        CancellationToken cancellationToken)
    {
        var fullPath = string.IsNullOrWhiteSpace(path) ? ResolveDefaultPath() : Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return false;

        var artifact = await LoadAndValidateAsync(fullPath, source, seed, pairCount, cancellationToken);
        _ = await IbgeNominalUReferenceStore.ImportAsync(
            connection, source, artifact.PersonName.FirstNameSex, artifact.PersonName.Estimate, cancellationToken);
        _ = await IbgeNominalUReferenceStore.ImportAsync(
            connection, source, artifact.MotherName.FirstNameSex, artifact.MotherName.Estimate, cancellationToken);
        return true;
    }

    public static async Task<string> ExportPersistedAsync(
        SqlConnection connection,
        IbgeNominalUReferenceInfo source,
        int seed,
        int pairCount,
        string path,
        CancellationToken cancellationToken)
    {
        var person = await IbgeNominalUReferenceStore.RequireAsync(
            connection, source, "TODOS",
            new IbgeNominalUBootstrapOptions(seed, pairCount), cancellationToken);
        var mother = await IbgeNominalUReferenceStore.RequireAsync(
            connection, source, "FEMININO",
            new IbgeNominalUBootstrapOptions(unchecked(seed + 1), pairCount), cancellationToken);

        var artifact = Create(
            source,
            new("TODOS", person.Estimate),
            new("FEMININO", mother.Estimate));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(artifact, JsonOptions) + Environment.NewLine,
            Encoding.UTF8,
            cancellationToken);
        return fullPath;
    }

    internal static IbgeNominalUPortableArtifact Create(
        IbgeNominalUReferenceInfo source,
        IbgeNominalUPortableArtifactEntry person,
        IbgeNominalUPortableArtifactEntry mother)
    {
        ValidateEntry(person, "TODOS");
        ValidateEntry(mother, "FEMININO");
        var provisional = new IbgeNominalUPortableArtifact(
            SchemaVersion,
            source.Code,
            source.ContentSha256.ToLowerInvariant(),
            NameComparisonContract.WholeNameJaroWinklerV1.ToString(),
            person with { Estimate = IbgeNominalUReferenceStore.Normalize(person.Estimate) },
            mother with { Estimate = IbgeNominalUReferenceStore.Normalize(mother.Estimate) },
            string.Empty);
        return provisional with { ArtifactSha256 = ComputeArtifactFingerprint(provisional) };
    }

    internal static async Task<IbgeNominalUPortableArtifact> LoadAndValidateAsync(
        string path,
        IbgeNominalUReferenceInfo source,
        int seed,
        int pairCount,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var artifact = await JsonSerializer.DeserializeAsync<IbgeNominalUPortableArtifact>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Artefato portátil IBGE nominal u vazio.");

        if (!string.Equals(artifact.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(artifact.ReferenceCode, source.Code, StringComparison.Ordinal)
            || !string.Equals(artifact.SourceContentSha256, source.ContentSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.ComparisonContract, NameComparisonContract.WholeNameJaroWinklerV1.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("Artefato portátil IBGE nominal u não corresponde à referência/método corrente.");

        ValidateEntry(artifact.PersonName, "TODOS");
        ValidateEntry(artifact.MotherName, "FEMININO");
        if (artifact.PersonName.Estimate.Seed != seed
            || artifact.MotherName.Estimate.Seed != unchecked(seed + 1)
            || artifact.PersonName.Estimate.PairCount != pairCount
            || artifact.MotherName.Estimate.PairCount != pairCount)
            throw new InvalidDataException("Artefato portátil IBGE nominal u diverge de seed/PairCount correntes.");

        var normalized = artifact with
        {
            SourceContentSha256 = artifact.SourceContentSha256.ToLowerInvariant(),
            PersonName = artifact.PersonName with
            {
                Estimate = IbgeNominalUReferenceStore.Normalize(artifact.PersonName.Estimate)
            },
            MotherName = artifact.MotherName with
            {
                Estimate = IbgeNominalUReferenceStore.Normalize(artifact.MotherName.Estimate)
            }
        };
        var computed = ComputeArtifactFingerprint(normalized);
        if (!string.Equals(computed, artifact.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 do artefato portátil IBGE nominal u não confere.");

        return normalized with { ArtifactSha256 = computed };
    }

    internal static string ComputeArtifactFingerprint(IbgeNominalUPortableArtifact artifact)
    {
        var lines = new List<string>
        {
            artifact.SchemaVersion,
            artifact.ReferenceCode,
            artifact.SourceContentSha256.ToUpperInvariant(),
            artifact.ComparisonContract
        };
        AppendEntry(lines, artifact.PersonName);
        AppendEntry(lines, artifact.MotherName);
        var canonical = string.Join("\n", lines) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void ValidateEntry(IbgeNominalUPortableArtifactEntry entry, string expectedSex)
    {
        if (!string.Equals(entry.FirstNameSex, expectedSex, StringComparison.Ordinal))
            throw new InvalidDataException($"Artefato portátil IBGE nominal u exige recorte {expectedSex}.");
        IbgeNominalUReferenceStore.ValidateEstimateContract(entry.Estimate);
    }

    private static void AppendEntry(ICollection<string> lines, IbgeNominalUPortableArtifactEntry entry)
    {
        var estimate = IbgeNominalUReferenceStore.Normalize(entry.Estimate);
        lines.Add(entry.FirstNameSex);
        lines.Add(estimate.MethodVersion);
        lines.Add(estimate.JointConstructionVersion);
        lines.Add(estimate.ObservationChannelVersion);
        lines.Add(estimate.Seed.ToString(CultureInfo.InvariantCulture));
        lines.Add(estimate.PairCount.ToString(CultureInfo.InvariantCulture));
        lines.Add(estimate.FirstNamePublishedOccurrences.ToString(CultureInfo.InvariantCulture));
        lines.Add(estimate.SurnamePublishedOccurrences.ToString(CultureInfo.InvariantCulture));
        lines.Add(estimate.FirstNameVocabularySize.ToString(CultureInfo.InvariantCulture));
        lines.Add(estimate.SurnameVocabularySize.ToString(CultureInfo.InvariantCulture));
        lines.Add(Num(estimate.AnalyticExactFirstNameProbability));
        lines.Add(Num(estimate.AnalyticExactSurnameProbability));
        lines.Add(Num(estimate.AnalyticExactSyntheticFullNameProbability));
        foreach (var state in estimate.States.OrderBy(x => x.State, StringComparer.Ordinal))
            lines.Add(string.Join("|",
                state.State,
                state.Support.ToString(CultureInfo.InvariantCulture),
                Num(state.Probability),
                Num(state.StandardError)));
    }

    private static string Num(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);
}

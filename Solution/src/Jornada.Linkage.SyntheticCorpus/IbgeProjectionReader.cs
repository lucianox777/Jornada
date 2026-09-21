using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record IbgeProjectionManifest(
    int SchemaVersion,
    string ReferenceCode,
    string Format,
    string GeneratedFrom,
    IReadOnlyList<IbgeProjectionFile> Files);

public sealed record IbgeProjectionFile(
    string Path,
    string Kind,
    bool Required,
    string Sha256,
    string CanonicalContentSha256,
    long RowCount);

public sealed record IbgeFrequencyRow(
    string? Tipo,
    string? Valor,
    long Frequencia,
    string? Sexo = null,
    string? PeriodoNascimento = null,
    string? EscopoGeografico = null,
    string? UfCodigo = null,
    string? MunicipioCodigo = null);

public sealed record IbgeProjectionReadResult(
    IReadOnlyList<IbgeFrequencyRow> Rows,
    string PhysicalSha256,
    string CanonicalContentSha256,
    long RowCount);

public static class IbgeProjectionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IbgeProjectionManifest> ReadManifestAsync(
        string referenceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceRoot);
        var path = Path.Combine(Path.GetFullPath(referenceRoot), "projection-manifest.json");
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<IbgeProjectionManifest>(
            stream,
            JsonOptions,
            cancellationToken);
        if (manifest is null || manifest.SchemaVersion <= 0 || manifest.Files.Count == 0)
            throw new InvalidDataException("projection-manifest.json inválido ou vazio.");

        var duplicate = manifest.Files
            .GroupBy(x => x.Path, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"projection-manifest contém caminho duplicado: {duplicate.Key}.");

        foreach (var file in manifest.Files)
            ValidateMetadata(file);

        return manifest;
    }

    public static async Task<IbgeProjectionReadResult> ReadFilteredAsync(
        string referenceRoot,
        IbgeProjectionFile file,
        Func<IbgeFrequencyRow, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceRoot);
        ValidateMetadata(file);

        var root = Path.GetFullPath(referenceRoot);
        var path = ResolveUnderRoot(root, file.Path);

        await using (var physical = File.OpenRead(path))
        {
            var hash = await SHA256.HashDataAsync(physical, cancellationToken);
            var actual = Convert.ToHexString(hash);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 físico diverge para {file.Path}.");
        }

        var selected = new List<IbgeFrequencyRow>();
        using var canonicalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;

        await using var source = File.OpenRead(path);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows++;
            canonicalHash.AppendData(Encoding.UTF8.GetBytes(line + "
"));

            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException($"Linha vazia em {file.Path}:{rows}.");

            IbgeFrequencyRow? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<IbgeFrequencyRow>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"JSON inválido em {file.Path}:{rows}.", ex);
            }

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Tipo) || string.IsNullOrWhiteSpace(parsed.Valor) || parsed.Frequencia <= 0)
                throw new InvalidDataException($"Linha semanticamente inválida em {file.Path}:{rows}.");

            if (predicate is null || predicate(parsed))
                selected.Add(parsed);
        }

        var canonical = Convert.ToHexString(canonicalHash.GetHashAndReset());
        if (rows != file.RowCount)
            throw new InvalidDataException($"rowCount diverge para {file.Path}: esperado={file.RowCount}; atual={rows}.");
        if (!string.Equals(canonical, file.CanonicalContentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 canônico diverge para {file.Path}.");

        return new IbgeProjectionReadResult(selected, file.Sha256.ToUpperInvariant(), canonical, rows);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Caminho de projeção deve ser relativo.");

        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Caminho de projeção escapou da raiz declarada.");

        return full;
    }

    private static void ValidateMetadata(IbgeProjectionFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Kind))
            throw new InvalidDataException("Arquivo de projeção sem path/kind.");
        ValidateSha(file.Sha256, $"sha256 de {file.Path}");
        ValidateSha(file.CanonicalContentSha256, $"canonicalContentSha256 de {file.Path}");
        if (file.RowCount <= 0)
            throw new InvalidDataException($"rowCount inválido em {file.Path}.");
    }

    private static void ValidateSha(string value, string description)
    {
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"{description} inválido.");
    }
}

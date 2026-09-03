using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jornada.Contracts;

namespace Jornada.Ingestion;

public static partial class IngestionPackageInspector
{
    public const int CurrentFormatVersion = 2;
    public const long MaxCompressedBytes = 250L * 1024 * 1024;
    public const long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxManifestBytes = 64L * 1024;

    private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

    private static JsonSerializerOptions CreateManifestJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static IngestionPackageManifest ParseAndValidate(byte[] bytes)
    {
        if (bytes.Length == 0) throw new InvalidDataException("ZIP vazio.");
        using var ms = new MemoryStream(bytes, writable: false);
        return ParseAndValidate(ms, bytes.LongLength);
    }

    /// <summary>
    /// Leitura mínima do manifest para autorização do recurso. Não expande pessoas/registros nem percorre o ZIP inteiro.
    /// Deve ser chamada somente sobre stream seekable já limitado pelo teto compactado.
    /// </summary>
    public static IngestionPackageManifest ParseManifestForAuthorization(Stream stream, long compressedLength)
    {
        ValidateCompressedStream(stream, compressedLength);
        using var zip = OpenAndValidateShape(stream);
        return ReadAndValidateManifest(zip, validatePayloadPresence: false);
    }


    /// <summary>
    /// Preflight do Processor: valida envelope/manifest e presença lógica das entradas, sem varrer o payload
    /// descompactado. O limite real de 2 GiB é aplicado durante o parse por DecompressedLimitStream.
    /// </summary>
    public static IngestionPackageManifest ParseManifestForProcessing(Stream stream, long compressedLength)
    {
        ValidateCompressedStream(stream, compressedLength);
        using var zip = OpenAndValidateShape(stream);
        return ReadAndValidateManifest(zip, validatePayloadPresence: true);
    }

    public static IngestionPackageManifest ParseAndValidate(Stream stream, long compressedLength)
    {
        ValidateCompressedStream(stream, compressedLength);
        using var zip = OpenAndValidateShape(stream);
        ValidateActualUncompressedSize(zip, MaxUncompressedBytes);
        return ReadAndValidateManifest(zip, validatePayloadPresence: true);
    }

    private static void ValidateCompressedStream(Stream stream, long compressedLength)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new InvalidDataException("Stream do ZIP deve ser legível e seekable.");
        if (compressedLength <= 0) throw new InvalidDataException("ZIP vazio.");
        if (compressedLength > MaxCompressedBytes) throw new InvalidDataException("ZIP excede 250 MB compactados.");
        stream.Position = 0;
    }

    private static ZipArchive OpenAndValidateShape(Stream stream)
    {
        stream.Position = 0;
        var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        try
        {
            if (zip.Entries.Count != 3)
                throw new InvalidDataException("O ZIP deve conter exatamente manifest.json, pessoas.jsonl e registros.jsonl.");
            if (zip.Entries.Any(e => e.FullName.Contains('/', StringComparison.Ordinal) || e.FullName.Contains('\\', StringComparison.Ordinal)))
                throw new InvalidDataException("Subdiretórios não são permitidos no pacote de ingestão.");
            var duplicate = zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null) throw new InvalidDataException($"Entrada duplicada não é permitida: {duplicate.Key}.");
            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
            if (!names.SetEquals(new[] { "manifest.json", "pessoas.jsonl", "registros.jsonl" }))
                throw new InvalidDataException("Os nomes canônicos obrigatórios são manifest.json, pessoas.jsonl e registros.jsonl, na raiz do ZIP.");
            return zip;
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    private static IngestionPackageManifest ReadAndValidateManifest(ZipArchive zip, bool validatePayloadPresence)
    {
        var manifestEntry = zip.GetEntry("manifest.json")!;
        var pessoasEntry = zip.GetEntry("pessoas.jsonl")!;
        var registrosEntry = zip.GetEntry("registros.jsonl")!;
        if (manifestEntry.Length > MaxManifestBytes) throw new InvalidDataException("manifest.json excede 64 KB.");

        using var reader = new StreamReader(manifestEntry.Open());
        IngestionPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<IngestionPackageManifest>(reader.ReadToEnd(), ManifestJsonOptions)
                ?? throw new InvalidDataException("manifest.json inválido.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("manifest.json inválido.", ex);
        }

        if (manifest.FormatoVersao != CurrentFormatVersion) throw new InvalidDataException($"formatoVersao deve ser {CurrentFormatVersion}.");
        if (manifest.PessoaSchemaVersao < 1) throw new InvalidDataException("pessoaSchemaVersao deve ser >= 1.");
        if (string.IsNullOrWhiteSpace(manifest.CodigoSistemaOrigem) || !SistemaOrigemCodeRegex().IsMatch(manifest.CodigoSistemaOrigem))
            throw new InvalidDataException("codigoSistemaOrigem é obrigatório e deve conter 1 a 80 caracteres A-Z/0-9/_/-.");
        if (validatePayloadPresence && pessoasEntry.Length == 0) throw new InvalidDataException("pessoas.jsonl não pode estar vazio.");

        var factualFields = new object?[] { manifest.Natureza, manifest.CodigoTipo, manifest.TipoVersao };
        var factualCount = factualFields.Count(v => v is not null);
        if (factualCount is > 0 and < 3)
            throw new InvalidDataException("natureza, codigoTipo e tipoVersao devem ser informados em conjunto ou todos omitidos.");
        if (manifest.TipoVersao.HasValue && manifest.TipoVersao.Value < 1)
            throw new InvalidDataException("tipoVersao deve ser >= 1 quando informado.");
        if (manifest.CodigoTipo is not null && !TipoCodeRegex().IsMatch(manifest.CodigoTipo))
            throw new InvalidDataException("codigoTipo deve conter exatamente 4 caracteres A-Z/0-9.");
        if (validatePayloadPresence && registrosEntry.Length > 0 && factualCount != 3)
            throw new InvalidDataException("registros.jsonl com conteúdo exige natureza, codigoTipo e tipoVersao no manifest.json.");
        return manifest;
    }

    public static void ValidateActualUncompressedSize(ZipArchive zip, long maxUncompressedBytes)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > maxUncompressedBytes - total)
                throw new InvalidDataException("Conteúdo descompactado excede 2 GB.");
            using var source = entry.Open();
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > maxUncompressedBytes)
                    throw new InvalidDataException("Conteúdo descompactado excede 2 GB.");
            }
        }
    }

    public static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string ComputeSha256(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new InvalidDataException("Stream do ZIP deve ser legível e seekable.");
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        return hash;
    }

    public static string GetCanonicalFileName(IngestionPackageManifest manifest, AccessContext context, string sha256)
    {
        if (!ShaRegex().IsMatch(sha256)) throw new InvalidDataException("SHA-256 calculado inválido.");
        return $"ENTREGA_{context.GestorCodigo.ToUpperInvariant()}_{manifest.CodigoSistemaOrigem}_v{manifest.FormatoVersao}_{sha256}.zip";
    }

    public static void ValidateCanonicalFileName(string fileName, IngestionPackageManifest manifest, AccessContext context, string sha256)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidDataException("filename do ZIP inválido.");
        var expected = GetCanonicalFileName(manifest, context, sha256);
        if (!string.Equals(fileName, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Nome canônico do ZIP inválido.");
    }

    [GeneratedRegex("^[A-Z0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex TipoCodeRegex();
    [GeneratedRegex("^[A-Z0-9_-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex SistemaOrigemCodeRegex();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();
}


/// <summary>Orçamento compartilhado dos bytes efetivamente descompactados de um pacote.</summary>
public sealed class DecompressedByteBudget
{
    private long _total;
    public long MaximumBytes { get; }
    public long TotalBytes => Interlocked.Read(ref _total);

    public DecompressedByteBudget(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    internal void Add(int bytes)
    {
        if (bytes <= 0) return;
        var total = Interlocked.Add(ref _total, bytes);
        if (total > MaximumBytes)
            throw new InvalidDataException("Conteúdo descompactado excede 2 GB.");
    }
}

/// <summary>Stream que contabiliza bytes descompactados na mesma passagem usada pelo parser.</summary>
public sealed class DecompressedLimitStream(Stream inner, DecompressedByteBudget budget) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        budget.Add(read);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        budget.Add(read);
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        budget.Add(read);
        return read;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await inner.DisposeAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

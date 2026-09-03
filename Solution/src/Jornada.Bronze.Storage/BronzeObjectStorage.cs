using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jornada.Bronze.Storage;

public sealed record BronzeStorageOptions
{
    public string Provider { get; init; } = "FileSystem";
    public string RootPath { get; init; } = "data/bronze";
}

public sealed record BronzeObjectReference(string ObjectKey, string Sha256, long Length);
public sealed record BronzeStoredObject(string ObjectKey, long Length, DateTimeOffset LastWriteUtc);

public sealed class BronzeObjectNotFoundException(string objectKey, string path)
    : FileNotFoundException($"Objeto Bronze não encontrado: {objectKey}", path);

public sealed class BronzeObjectIntegrityException(string objectKey, string message, Exception? innerException = null)
    : IOException($"Integridade da Bronze divergente para {objectKey}: {message}", innerException)
{
    public string ObjectKey { get; } = objectKey;
}

public sealed class BronzeStorageUnavailableException(string operation, Exception innerException)
    : IOException($"Armazenamento Bronze indisponível durante {operation}.", innerException)
{
    public string Operation { get; } = operation;
}

public static partial class BronzeObjectCoordination
{
    public static string LockResourceForSha256(string sha256)
    {
        var hash = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (!ShaRegex().IsMatch(hash)) throw new InvalidDataException("SHA-256 inválido para coordenação da Bronze.");
        return "Jornada.Bronze.Object." + hash;
    }

    public static string Sha256FromObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new InvalidDataException("objeto_chave Bronze inválida.");
        var file = objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        var hash = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (!ShaRegex().IsMatch(hash)) throw new InvalidDataException("objeto_chave Bronze sem SHA-256 válido.");
        return hash;
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();
}

public interface IBronzeObjectStore
{
    string BuildObjectKey(string sha256);
    Task<BronzeObjectReference> PutIfAbsentAsync(string sha256, Stream content, long length, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);
    Task VerifyAsync(string objectKey, string expectedSha256, long expectedLength, CancellationToken ct);
}

/// <summary>
/// Operações administrativas deliberadamente separadas da interface de ingestão/leitura.
/// O processo operacional de GC é o único consumidor previsto na Fase 1.
/// </summary>
public interface IBronzeObjectMaintenanceStore
{
    IAsyncEnumerable<BronzeStoredObject> EnumerateCanonicalObjectsInBucketAsync(int bucket, string? afterObjectKey, DateTimeOffset olderThanUtc, CancellationToken ct);
    Task<bool> DeleteIfExistsAsync(string objectKey, CancellationToken ct);
    Task<int> DeleteStaleTemporaryFilesAsync(DateTimeOffset olderThanUtc, CancellationToken ct);
}

/// <summary>
/// Bronze content-addressed da Fase 1. A chave é relativa e independente da raiz física:
/// sha256/ab/cd/&lt;hash&gt;.zip. O SQL persiste a chave, nunca caminho absoluto.
/// Escrita é idempotente, objeto-primeiro e publicação no destino final é atômica no mesmo volume.
/// </summary>
public sealed partial class FileSystemBronzeObjectStore : IBronzeObjectStore, IBronzeObjectMaintenanceStore
{
    private readonly string rootPath;

    public FileSystemBronzeObjectStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("BronzeStorage:RootPath é obrigatório.", nameof(rootPath));
        this.rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(this.rootPath);
    }

    public string BuildObjectKey(string sha256)
    {
        var hash = NormalizeHash(sha256);
        return $"sha256/{hash[..2]}/{hash.Substring(2, 2)}/{hash}.zip";
    }

    public async Task<BronzeObjectReference> PutIfAbsentAsync(
        string sha256, Stream content, long length, CancellationToken ct)
    {
        var hash = NormalizeHash(sha256);
        if (!content.CanRead) throw new InvalidDataException("Stream da Bronze deve ser legível.");
        if (length <= 0) throw new InvalidDataException("Objeto Bronze vazio.");
        if (content.CanSeek) content.Position = 0;

        var key = BuildObjectKey(hash);
        var destination = ResolvePath(key);
        var directory = Path.GetDirectoryName(destination)!;

        try
        {
            Directory.CreateDirectory(directory);

            if (File.Exists(destination))
            {
                await VerifyCanonicalObjectAsync(destination, key, hash, length, ct);
                return new BronzeObjectReference(key, hash, length);
            }

            var temp = Path.Combine(directory, $".{hash}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var target = new FileStream(
                    temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                    long written = 0;
                    try
                    {
                        while (true)
                        {
                            var read = await content.ReadAsync(buffer.AsMemory(0, 1024 * 1024), ct);
                            if (read == 0) break;
                            await target.WriteAsync(buffer.AsMemory(0, read), ct);
                            incremental.AppendData(buffer, 0, read);
                            written += read;
                        }
                        await target.FlushAsync(ct);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    }

                    if (written != length)
                        throw new InvalidDataException($"Objeto Bronze gravado com {written} bytes; esperado={length}.");
                    var actual = Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(actual, hash, StringComparison.Ordinal))
                        throw new InvalidDataException("SHA-256 do objeto Bronze diverge do hash calculado na recepção.");
                }

                try
                {
                    File.Move(temp, destination, overwrite: false);
                }
                catch (IOException) when (File.Exists(destination))
                {
                    // Outro escritor venceu a corrida. Não basta conferir o tamanho: o objeto canônico
                    // existente é re-hasheado antes de ser aceito como idempotente.
                    await VerifyCanonicalObjectAsync(destination, key, hash, length, ct);
                }

                return new BronzeObjectReference(key, hash, length);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Falha best-effort ao remover temporário Bronze; tipo={0}", ex.GetType().Name); }
                if (content.CanSeek) content.Position = 0;
            }
        }
        catch (BronzeObjectIntegrityException) { throw; }
        catch (BronzeStorageUnavailableException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("gravação", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("gravação", ex); }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = ResolvePath(objectKey);
        try
        {
            if (!File.Exists(path))
                throw new BronzeObjectNotFoundException(objectKey, path);
            Stream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (BronzeObjectNotFoundException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("leitura", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("leitura", ex); }
    }

    public async Task VerifyAsync(string objectKey, string expectedSha256, long expectedLength, CancellationToken ct)
    {
        var hash = NormalizeHash(expectedSha256);
        var path = ResolvePath(objectKey);
        try
        {
            if (!File.Exists(path)) throw new BronzeObjectNotFoundException(objectKey, path);
            await VerifyCanonicalObjectAsync(path, objectKey, hash, expectedLength, ct);
        }
        catch (BronzeObjectNotFoundException) { throw; }
        catch (BronzeObjectIntegrityException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("verificação", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("verificação", ex); }
    }

    public async IAsyncEnumerable<BronzeStoredObject> EnumerateCanonicalObjectsInBucketAsync(
        int bucket,
        string? afterObjectKey,
        DateTimeOffset olderThanUtc,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (bucket is < 0 or > 0xffff) throw new ArgumentOutOfRangeException(nameof(bucket));
        var hex = bucket.ToString("x4", System.Globalization.CultureInfo.InvariantCulture);
        var directory = Path.Combine(rootPath, "sha256", hex[..2], hex[2..]);
        if (!Directory.Exists(directory)) yield break;

        IEnumerable<string> files;
        try
        {
            // Ordenação explícita torna o cursor (bucket + última objeto_chave) determinístico e reiniciável.
            files = Directory.EnumerateFiles(directory, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("listagem", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("listagem", ex); }

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!string.IsNullOrWhiteSpace(afterObjectKey)
                && string.Compare(relative, afterObjectKey, StringComparison.OrdinalIgnoreCase) <= 0)
                continue;

            FileInfo info;
            try { info = new FileInfo(path); }
            catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("listagem", ex); }
            catch (IOException ex) { throw new BronzeStorageUnavailableException("listagem", ex); }

            var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (lastWrite > olderThanUtc) continue;
            _ = ResolvePath(relative);
            yield return new BronzeStoredObject(relative, info.Length, lastWrite);
            await Task.Yield();
        }
    }

    public Task<bool> DeleteIfExistsAsync(string objectKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = ResolvePath(objectKey);
        try
        {
            if (!File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("expurgo", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("expurgo", ex); }
    }

    public Task<int> DeleteStaleTemporaryFilesAsync(DateTimeOffset olderThanUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var shaRoot = Path.Combine(rootPath, "sha256");
        if (!Directory.Exists(shaRoot)) return Task.FromResult(0);
        var deleted = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(shaRoot, ".*.tmp", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (lastWrite > olderThanUtc) continue;
                File.Delete(path);
                deleted++;
            }
            return Task.FromResult(deleted);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("limpeza de temporários", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("limpeza de temporários", ex); }
    }

    private async Task VerifyCanonicalObjectAsync(
        string path, string objectKey, string expectedHash, long expectedLength, CancellationToken ct)
    {
        try
        {
            var existing = new FileInfo(path);
            if (existing.Length != expectedLength)
                throw new BronzeObjectIntegrityException(objectKey, $"tamanho existente={existing.Length}; esperado={expectedLength}.");

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, 1024 * 1024), ct);
                    if (read == 0) break;
                    incremental.AppendData(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
            var actual = Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, expectedHash, StringComparison.Ordinal))
                throw new BronzeObjectIntegrityException(objectKey, $"SHA-256 físico={actual}; esperado={expectedHash}.");
        }
        catch (BronzeObjectIntegrityException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new BronzeStorageUnavailableException("verificação de integridade", ex); }
        catch (IOException ex) { throw new BronzeStorageUnavailableException("verificação de integridade", ex); }
    }

    private string ResolvePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || objectKey.Contains('\\', StringComparison.Ordinal))
            throw new InvalidDataException("objeto_chave Bronze inválida.");
        var parts = objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !string.Equals(parts[0], "sha256", StringComparison.Ordinal))
            throw new InvalidDataException("objeto_chave Bronze fora do formato content-addressed esperado.");
        var hash = Path.GetFileNameWithoutExtension(parts[3]);
        hash = NormalizeHash(hash);
        if (parts[1] != hash[..2] || parts[2] != hash.Substring(2,2) || parts[3] != hash + ".zip")
            throw new InvalidDataException("objeto_chave Bronze incompatível com o SHA-256 embutido.");

        var full = Path.GetFullPath(Path.Combine(rootPath, parts[0], parts[1], parts[2], parts[3]));
        var prefix = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("objeto_chave Bronze escapou da raiz configurada.");
        return full;
    }

    private static string NormalizeHash(string sha256)
    {
        var hash = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (!Sha256Regex().IsMatch(hash))
            throw new InvalidDataException("SHA-256 inválido para chave Bronze.");
        return hash;
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

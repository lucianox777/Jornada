using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Jornada.Api;

public sealed record IngestionStagingOptions
{
    public string RootPath { get; init; } = "data/staging";
    public int CleanupIntervalMinutes { get; init; } = 30;
    public int MaxAgeHours { get; init; } = 6;
}

public sealed class IngestionStagingUnavailableException(string operation, Exception innerException)
    : IOException($"Staging temporário da ingestão indisponível durante {operation}.", innerException)
{
    public string Operation { get; } = operation;
}

public sealed record StagedPackage(string Path, long Length, string Sha256);

public sealed class IngestionStagingStore
{
    private readonly string rootPath;

    public IngestionStagingStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("IngestionStaging:RootPath é obrigatório.", nameof(rootPath));
        this.rootPath = Path.GetFullPath(rootPath);
        EnsureRoot();
    }

    public async Task<StagedPackage> ReceiveAsync(Stream body, long maxBytes, CancellationToken ct)
    {
        EnsureRoot();
        var path = Path.Combine(rootPath, $"jornada-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.zip.part");
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                total += read;
                if (total > maxBytes) throw new InvalidDataException("ZIP excede o limite compactado.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await output.FlushAsync(ct);
            if (total == 0) throw new InvalidDataException("ZIP vazio.");
            return new StagedPackage(path, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (InvalidDataException) { TryDelete(path); throw; }
        catch (OperationCanceledException) { TryDelete(path); throw; }
        catch (UnauthorizedAccessException ex) { TryDelete(path); throw new IngestionStagingUnavailableException("recepção", ex); }
        catch (IOException ex) { TryDelete(path); throw new IngestionStagingUnavailableException("recepção", ex); }
    }

    public void TryDelete(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var prefix = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath : rootPath + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex)
        {
            // Falha de cleanup não mascara o erro original; o worker periódico é a rede de segurança.
            System.Diagnostics.Trace.TraceWarning("Falha best-effort ao remover staging temporário; tipo={0}", ex.GetType().Name);
        }
    }

    public int DeleteOlderThan(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        EnsureRoot();
        var deleted = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(rootPath, "jornada-*.zip.part", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (lastWrite > cutoffUtc) continue;
                File.Delete(path);
                deleted++;
            }
            return deleted;
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex) { throw new IngestionStagingUnavailableException("limpeza", ex); }
        catch (IOException ex) { throw new IngestionStagingUnavailableException("limpeza", ex); }
    }

    private void EnsureRoot()
    {
        try { Directory.CreateDirectory(rootPath); }
        catch (UnauthorizedAccessException ex) { throw new IngestionStagingUnavailableException("preparação", ex); }
        catch (IOException ex) { throw new IngestionStagingUnavailableException("preparação", ex); }
    }
}

public sealed class IngestionStagingCleanupWorker(
    IngestionStagingStore store,
    IOptions<IngestionStagingOptions> options,
    ILogger<IngestionStagingCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cfg = options.Value;
                var deleted = store.DeleteOlderThan(DateTimeOffset.UtcNow.AddHours(-Math.Max(1, cfg.MaxAgeHours)), stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Staging de ingestão: {Quantidade} temporário(s) abandonado(s) removido(s).", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na limpeza do staging de ingestão; recepção normal continuará tentando remover temporários no caminho feliz.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}

using System.Net.Http.Headers;

namespace Jornada.Ensaio;

public sealed class IngestionStageRunner(
    EnsaioRuntimeOptions options,
    IPipelineDrainProbe drainProbe,
    ICollection<string> log)
{
    public async Task RunAsync(EnsaioEtapa stage, CancellationToken cancellationToken)
    {
        var packages = Directory
            .GetFiles(options.PackagesDirectory, $"{stage.PrefixoArquivo}*.zip")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (packages.Length == 0)
            throw new FileNotFoundException(
                $"Nenhum pacote {stage.PrefixoArquivo}*.zip encontrado em {options.PackagesDirectory}.");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);

        var accepted = 0;
        foreach (var path in packages)
        {
            var name = Path.GetFileName(path);
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            using var request = new HttpRequestMessage(HttpMethod.Post, options.IngestionEndpoint)
            {
                Content = content
            };
            request.Headers.Add("Idempotency-Key", $"ensaio-{stage.Codigo}-{name}");

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                accepted++;
                continue;
            }

            log.Add($"{stage.Codigo}: {name} -> {(int)response.StatusCode} {body}");
        }

        log.Add($"{stage.Codigo}: {accepted}/{packages.Length} pacotes aceitos");
        if (accepted != packages.Length)
            throw new InvalidOperationException($"A API aceitou apenas {accepted}/{packages.Length} pacotes.");

        await WaitForDrainAsync(cancellationToken);
    }

    private async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now + options.DrainTimeout;
        var stableReads = 0;

        while (DateTimeOffset.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            var pending = await drainProbe.CountPendingAsync(cancellationToken);
            if (pending == 0)
            {
                if (++stableReads >= 3)
                    return;
            }
            else
            {
                stableReads = 0;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }

        log.Add("drenagem: timeout");
    }
}

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

return await JornadaIntegrator.MainAsync(args);

internal static partial class JornadaIntegrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> MainAsync(string[] args)
    {
        try
        {
            var parsed = ParseArgs(args);
            var config = await LoadConfigAsync(parsed.ConfigPath);
            ValidateConfig(config);

            using var client = new HttpClient();
            if (parsed.Mode == "--enviar")
            {
                await SendAsync(client, config, parsed.Value);
                return 0;
            }

            if (parsed.Mode == "--resultado")
            {
                await QueryResultAsync(client, config, parsed.Value, parsed.OutputPath);
                return 0;
            }

            PrintUsage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    internal static ParsedArgs ParseArgs(string[] args)
    {
        if (args.Length < 2) return new ParsedArgs(string.Empty, string.Empty, "integrador.config.json", null);
        var mode = args[0];
        var value = args[1];
        var configPath = "integrador.config.json";
        string? outputPath = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length) { configPath = args[++i]; continue; }
            if (args[i] == "--saida" && i + 1 < args.Length) { outputPath = args[++i]; continue; }
            throw new ArgumentException($"Parâmetro desconhecido: {args[i]}");
        }
        return new ParsedArgs(mode, value, configPath, outputPath);
    }

    private static async Task<IntegratorConfig> LoadConfigAsync(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo de configuração não encontrado.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<IntegratorConfig>(stream, JsonOptions)
            ?? throw new InvalidDataException("Arquivo de configuração inválido.");
    }

    internal static void ValidateConfig(IntegratorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Gestor)) throw new InvalidDataException("gestor é obrigatório no arquivo de configuração.");
        if (string.IsNullOrWhiteSpace(config.AccessKey) || string.Equals(config.AccessKey, "CHANGE_ME", StringComparison.Ordinal))
            throw new InvalidDataException("accessKey deve ser configurada.");
        if (config.Endpoints is null || !Uri.TryCreate(config.Endpoints.Envio, UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(config.Endpoints.Resultado) || !config.Endpoints.Resultado.Contains("{nomeArquivo}", StringComparison.Ordinal))
            throw new InvalidDataException("endpoints.envio deve ser URL absoluta e endpoints.resultado deve conter {nomeArquivo}.");
        if (config.Polling.IntervalSeconds < 1 || config.Polling.TimeoutSeconds < config.Polling.IntervalSeconds)
            throw new InvalidDataException("Configuração de polling inválida.");
    }

    private static async Task SendAsync(HttpClient client, IntegratorConfig config, string inputPath)
    {
        var path = Path.GetFullPath(inputPath);
        if (!File.Exists(path)) throw new FileNotFoundException("ZIP para envio não encontrado.", path);

        var sha = await ComputeSha256Async(path);
        var manifest = ReadManifest(path);
        var canonicalName = $"ENTREGA_{config.Gestor.ToUpperInvariant()}_{manifest.CodigoSistemaOrigem}_v{manifest.FormatoVersao}_{sha}.zip";

        await using var file = File.OpenRead(path);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoints.Envio);
        request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", config.Gestor);
        request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", config.AccessKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"sha256:{sha}");
        request.Content = new StreamContent(file);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = canonicalName };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Envio rejeitado ({(int)response.StatusCode}): {body}");

        Console.WriteLine(body);
        Console.WriteLine($"SHA-256: {sha}");
        Console.WriteLine($"Nome enviado: {canonicalName}");
    }

    private static async Task QueryResultAsync(HttpClient client, IntegratorConfig config, string rawFileName, string? outputPath)
    {
        var fileName = ValidateZipFileName(rawFileName);
        var endpoint = BuildResultEndpoint(config, fileName);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(config.Polling.TimeoutSeconds);

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", config.Gestor);
            request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", config.AccessKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Consulta rejeitada ({(int)response.StatusCode}): {body}");

            using var document = JsonDocument.Parse(body);
            var finalizado = document.RootElement.TryGetProperty("finalizado", out var terminal) && terminal.ValueKind == JsonValueKind.True;
            if (finalizado)
            {
                var pretty = JsonSerializer.Serialize(document.RootElement, JsonOptions);
                var destination = outputPath ?? BuildDefaultOutput(config, fileName);
                var fullDestination = Path.GetFullPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(fullDestination) ?? Directory.GetCurrentDirectory());
                await File.WriteAllTextAsync(fullDestination, pretty + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine(fullDestination);
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("O processamento não chegou a estado final dentro do tempo configurado.");

            await Task.Delay(TimeSpan.FromSeconds(config.Polling.IntervalSeconds));
        }
    }

    internal static string ValidateZipFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("--resultado exige somente o nome exato do ZIP enviado, sem caminho.");

        var value = raw.Trim();
        if (value.Length > 260
            || !value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':'))
            throw new ArgumentException("--resultado exige somente o nome exato do ZIP enviado, sem caminho.");

        return value;
    }

    internal static string BuildResultEndpoint(IntegratorConfig config, string rawFileName)
    {
        var fileName = ValidateZipFileName(rawFileName);
        return config.Endpoints.Resultado.Replace("{nomeArquivo}", Uri.EscapeDataString(fileName), StringComparison.Ordinal);
    }

    private static string BuildDefaultOutput(IntegratorConfig config, string fileName)
    {
        var safe = InvalidFileNameRegex().Replace(fileName, "_");
        var directory = string.IsNullOrWhiteSpace(config.DiretorioSaida) ? "." : config.DiretorioSaida;
        return Path.Combine(directory, $"resultado_{safe}.json");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ManifestInfo ReadManifest(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("ZIP não contém manifest.json.");
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var format = root.GetProperty("formatoVersao").GetInt32();
        var source = root.GetProperty("codigoSistemaOrigem").GetString();
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("manifest.json sem codigoSistemaOrigem.");
        return new ManifestInfo(format, source);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Jornada.Integrador --enviar <arquivo.zip> [--config integrador.config.json]");
        Console.WriteLine("Jornada.Integrador --resultado <nome.zip> [--config integrador.config.json] [--saida resultado.json]");
    }

    [GeneratedRegex("[^A-Za-z0-9._-]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFileNameRegex();
}

internal sealed record ParsedArgs(string Mode, string Value, string ConfigPath, string? OutputPath);
internal sealed record ManifestInfo(int FormatoVersao, string CodigoSistemaOrigem);

internal sealed class IntegratorConfig
{
    public string Gestor { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public EndpointConfig Endpoints { get; init; } = new();
    public PollingConfig Polling { get; init; } = new();
    public string DiretorioSaida { get; init; } = "resultados";
}

internal sealed class EndpointConfig
{
    public string Envio { get; init; } = string.Empty;
    public string Resultado { get; init; } = string.Empty;
}

internal sealed class PollingConfig
{
    public int IntervalSeconds { get; init; } = 5;
    public int TimeoutSeconds { get; init; } = 3600;
}

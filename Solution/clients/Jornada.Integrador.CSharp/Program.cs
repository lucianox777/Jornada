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
            if (IsHelpRequest(args))
            {
                PrintUsage();
                return 0;
            }

            var parsed = ParseArgs(args);
            if (parsed.Mode is not ("--enviar" or "--enviar-todos" or "--resultado"))
            {
                PrintUsage();
                return 2;
            }

            var config = await LoadConfigAsync(parsed.ConfigPath);
            ValidateConfig(config);

            using var client = new HttpClient();
            if (parsed.Mode == "--enviar")
            {
                await SendAsync(client, config, parsed.Value);
                return 0;
            }

            if (parsed.Mode == "--enviar-todos")
            {
                var result = await SendAllFromDirectoryAsync(
                    AppContext.BaseDirectory,
                    path => SendAsync(client, config, path));

                Console.WriteLine($"Lote concluído: {result.Enviados} enviado(s), {result.Falhas} falha(s).");
                return result.Falhas == 0 ? 0 : 1;
            }

            await QueryResultAsync(client, config, parsed.Value, parsed.OutputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    internal static bool IsHelpRequest(string[] args) =>
        args.Length == 1 && args[0] is "/help" or "--help" or "-h";

    internal static ParsedArgs ParseArgs(string[] args)
    {
        if (args.Length == 0)
            return new ParsedArgs(string.Empty, string.Empty, "integrador.config.json", null);

        var mode = args[0];
        var value = string.Empty;
        var optionStart = 1;

        if (mode is "--enviar" or "--resultado")
        {
            if (args.Length < 2)
                throw new ArgumentException($"{mode} exige um valor.");

            value = args[1];
            optionStart = 2;
        }
        else if (mode != "--enviar-todos")
        {
            return new ParsedArgs(mode, value, "integrador.config.json", null);
        }

        var configPath = "integrador.config.json";
        string? outputPath = null;
        for (var i = optionStart; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length)
            {
                configPath = args[++i];
                continue;
            }

            if (args[i] == "--saida" && i + 1 < args.Length)
            {
                if (mode != "--resultado")
                    throw new ArgumentException("--saida só pode ser usado com --resultado.");

                outputPath = args[++i];
                continue;
            }

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

    internal static string[] EnumerateZipFilesForBatch(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
            throw new DirectoryNotFoundException($"Pasta do CLI não encontrada: {fullDirectory}");

        return Directory.EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static async Task<BatchSendResult> SendAllFromDirectoryAsync(
        string directory,
        Func<string, Task> sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        var fullDirectory = Path.GetFullPath(directory);
        var zipFiles = EnumerateZipFilesForBatch(fullDirectory);
        if (zipFiles.Length == 0)
        {
            Console.WriteLine($"Nenhum ZIP encontrado em: {fullDirectory}");
            return new BatchSendResult(0, 0);
        }

        var sentDirectory = Path.Combine(fullDirectory, "Enviados");
        var sent = 0;
        var failures = 0;

        foreach (var zipPath in zipFiles)
        {
            var destination = Path.Combine(sentDirectory, Path.GetFileName(zipPath));
            if (File.Exists(destination))
            {
                failures++;
                Console.Error.WriteLine($"ERRO [{Path.GetFileName(zipPath)}]: já existe arquivo com o mesmo nome em Enviados; o ZIP não foi enviado.");
                continue;
            }

            try
            {
                Console.WriteLine($"Enviando: {Path.GetFileName(zipPath)}");
                await sender(zipPath);
                Directory.CreateDirectory(sentDirectory);
                File.Move(zipPath, destination);
                sent++;
                Console.WriteLine($"Movido para: {destination}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"ERRO [{Path.GetFileName(zipPath)}]: {ex.Message}");
            }
        }

        return new BatchSendResult(sent, failures);
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
        Console.WriteLine("Jornada.Integrador");
        Console.WriteLine();
        Console.WriteLine("Uso:");
        Console.WriteLine("  Jornada.Integrador --enviar <arquivo.zip> [--config integrador.config.json]");
        Console.WriteLine("      Envia um único ZIP.");
        Console.WriteLine();
        Console.WriteLine("  Jornada.Integrador --enviar-todos [--config integrador.config.json]");
        Console.WriteLine("      Envia todos os ZIPs da pasta do executável.");
        Console.WriteLine("      Após sucesso, move cada arquivo para .\\Enviados\\.");
        Console.WriteLine();
        Console.WriteLine("  Jornada.Integrador --resultado <nome.zip> [--config integrador.config.json] [--saida resultado.json]");
        Console.WriteLine("      Consulta o resultado detalhado do processamento pelo nome exato do ZIP enviado.");
        Console.WriteLine();
        Console.WriteLine("Ajuda:");
        Console.WriteLine("  Jornada.Integrador /help");
        Console.WriteLine("  Jornada.Integrador --help");
        Console.WriteLine("  Jornada.Integrador -h");
    }

    [GeneratedRegex("[^A-Za-z0-9._-]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFileNameRegex();
}

internal sealed record ParsedArgs(string Mode, string Value, string ConfigPath, string? OutputPath);
internal sealed record ManifestInfo(int FormatoVersao, string CodigoSistemaOrigem);
internal sealed record BatchSendResult(int Enviados, int Falhas);

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

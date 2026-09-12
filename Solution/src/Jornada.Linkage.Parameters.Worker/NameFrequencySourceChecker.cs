using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Verificação leve da fonte remota. Não escreve em banco nem altera snapshot.
/// Seu objetivo é apenas detectar indícios de revisão que justifiquem uma nova captura deliberada.
/// </summary>
public sealed class NameFrequencySourceChecker(
    ILogger<NameFrequencySourceChecker> logger,
    IConfiguration configuration,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    public const string Operation = "CHECK_NAME_FREQUENCY_SOURCE";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var endpoint = configuration.GetValue(
                "NameFrequencyCheck:BaseUrl",
                "https://servicodados.ibge.gov.br/api/v3/nomes/2022")!;
            var timeoutSeconds = Math.Max(10, configuration.GetValue("NameFrequencyCheck:HttpTimeoutSeconds", 30));

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Jornada-do-Cidadao/1.0 (+light-check-referencia-estatistica)");
            http.DefaultRequestHeaders.Referrer = new Uri("https://censo2022.ibge.gov.br/nomes/");

            var nameSignal = await ReadSignalAsync(http, endpoint, "nome", stoppingToken);
            var surnameSignal = await ReadSignalAsync(http, endpoint, "sobrenome", stoppingToken);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"NOME|{nameSignal.TotalPages}|{nameSignal.FirstPageHash}\nSOBRENOME|{surnameSignal.TotalPages}|{surnameSignal.FirstPageHash}\n")));

            logger.LogInformation(
                "Light check IBGE concluído sem mutação. NomePages={NamePages}; SobrenomePages={SurnamePages}; Fingerprint={Fingerprint}. " +
                "Mudança neste sinal apenas exige revisão humana/nova captura; nunca atualiza automaticamente a referência ativa.",
                nameSignal.TotalPages,
                surnameSignal.TotalPages,
                fingerprint);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Light check da fonte IBGE falhou. Snapshot local permanece inalterado e continua sendo a fonte operacional.");
            Environment.ExitCode = 1;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    internal static async Task<RemoteSignal> ReadSignalAsync(
        HttpClient http,
        string endpoint,
        string apiType,
        CancellationToken cancellationToken)
    {
        var url = $"{endpoint.TrimEnd('/')}/localidade/0/ranking/{apiType}?page=1";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("totalPages", out var pagesNode) ||
            !pagesNode.TryGetInt32(out var totalPages) || totalPages <= 0)
            throw new InvalidDataException($"Light check {apiType}: resposta sem totalPages válido.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(payload);
        return new RemoteSignal(totalPages, Convert.ToHexString(hash.GetHashAndReset()));
    }

    public sealed record RemoteSignal(int TotalPages, string FirstPageHash);
}

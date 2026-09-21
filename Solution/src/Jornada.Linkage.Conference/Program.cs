using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Linkage.Evaluation;

namespace Jornada.Linkage.Conference;

internal static class Program
{
    private static readonly JsonSerializerOptions SummaryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ConferenceOptions.Parse(args);
            if (options.Help)
            {
                Console.WriteLine(ConferenceOptions.Usage);
                return 0;
            }

            var config = ImplementationConferenceToleranceConfiguration.Load(
                options.ToleranceConfigPath!);
            var tolerance = config.ToContract();
            if (!tolerance.TryGetFrozen(out _, out var toleranceReason))
                throw new ConferencePreconditionException(toleranceReason);

            var connectionString = options.ConnectionString
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
                ?? throw new InvalidOperationException(
                    "Informe --connection-string ou ConnectionStrings__Jornada.");

            var summary = await GovernedImplementationConferenceCommand.ExecuteAsync(
                connectionString,
                options.ModelId!.Value,
                tolerance,
                options.CommandTimeoutSeconds,
                options.SourceRevision);

            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    evidenceId = summary.EvidenceId,
                    status = summary.Status.ToString(),
                    modelId = summary.ModelId,
                    modelVersion = summary.ModelVersion,
                    scenarios = summary.ScenarioCount,
                    candidatesEvaluated = summary.CandidatesEvaluated,
                    maxObservedPairLlrDifference = summary.MaxObservedPairLlrDifference,
                    maxObservedLogOddsDifference = summary.MaxObservedLogOddsDifference,
                    sameFinalDecision = summary.SameFinalDecision,
                    sameTop1 = summary.SameTop1,
                    spearman = summary.Spearman,
                    reason = summary.Reason,
                    requestSha256 = summary.RequestSha256,
                    reportSha256 = summary.ReportSha256,
                    statisticalValidation = "NOT_ASSESSED_ISSUE_31"
                },
                SummaryJson));

            return summary.Status switch
            {
                ImplementationConferenceStatus.CONFORME => 0,
                ImplementationConferenceStatus.DIVERGENTE => 2,
                _ => 3
            };
        }
        catch (ConferencePreconditionException ex)
        {
            Console.Error.WriteLine(ex.Code);
            return 4;
        }
    }
}

internal sealed record ConferenceOptions(
    Guid? ModelId,
    string? ConnectionString,
    string? ToleranceConfigPath,
    string? SourceRevision,
    int CommandTimeoutSeconds,
    bool Help)
{
    internal static ConferenceOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (raw is "--help" or "-h")
                return new(null, null, null, null, 0, true);
            if (!raw.StartsWith("--", StringComparison.Ordinal))
                continue;

            raw = raw[2..];
            var eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
                values[raw[..eq]] = raw[(eq + 1)..];
            else
                values[raw] = i + 1 < args.Length
                    && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : null;
        }

        string? Get(string name) =>
            values.TryGetValue(name, out var value) ? value : null;

        var modelRaw = Get("model-id");
        if (!Guid.TryParse(modelRaw, out var modelId) || modelId == Guid.Empty)
            throw new ArgumentException("--model-id é obrigatório e deve ser UUID válido.");

        var timeout = 900;
        var timeoutRaw = Get("command-timeout-seconds");
        if (timeoutRaw is not null
            && (!int.TryParse(
                    timeoutRaw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out timeout)
                || timeout is < 1 or > 3600))
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--command-timeout-seconds deve estar entre 1 e 3600.");

        return new ConferenceOptions(
            modelId,
            Get("connection-string"),
            Get("tolerance-config")
                ?? Path.Combine(
                    "config",
                    "linkage",
                    "implementation-conference-tolerance.json"),
            Get("source-revision"),
            timeout,
            false);
    }

    internal const string Usage =
        """
        Jornada.Linkage.Conference — conferência governada da implementação

        Obrigatório:
          --model-id <uuid>                   modelo RASCUNHO a conferir

        Opções:
          --connection-string <sql>           ou ConnectionStrings__Jornada
          --tolerance-config <arquivo.json>   padrão config/linkage/implementation-conference-tolerance.json
          --source-revision <sha>             opcional; proveniência técnica
          --command-timeout-seconds <1..3600> padrão 900

        O comando recusa execução governada enquanto a tolerância estiver UNFROZEN.
        Não lê PII para compor o corpus: usa somente modelo/parâmetros e vetores sintéticos
        determinísticos de estados. Persiste somente evidência agregada append-only.
        Validação estatística representativa permanece separada na issue #31.
        """;
}


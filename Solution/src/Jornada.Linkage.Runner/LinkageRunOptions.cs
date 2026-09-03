using System.Globalization;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Parâmetros imutáveis de uma execução do linkage probabilístico.
/// O executável é run-once e pode ser chamado por scheduler, SQL Agent,
/// Kubernetes Job ou mecanismo corporativo equivalente.
/// </summary>
public sealed record LinkageRunOptions(
    LinkageRunType Mode,
    int? ModelVersion,
    long? PessoaObservacaoId,
    string? GestorCodigo,
    DateTimeOffset? Since,
    int BatchSize,
    int MaxParallelism,
    long? MaxRecords,
    string? RequestedBy,
    string? Reason,
    Guid CorrelationId,
    bool Publish)
{
    public static LinkageRunOptions Parse(string[] args)
    {
        var values = ParsePairs(args);
        var mode = ParseEnum(values, "mode", LinkageRunType.ON_DEMAND);
        var modelVersion = ParseNullableInt(values, "model-version");
        var observationId = ParseNullableLong(values, "pessoa-observacao-id");
        var gestor = Get(values, "gestor");
        var since = ParseNullableDate(values, "since");
        var batchSize = Math.Clamp(ParseInt(values, "batch-size", 10_000), 100, 100_000);
        var maxParallelism = Math.Clamp(ParseInt(values, "max-parallelism", 1), 1, 32);
        var maxRecords = ParseNullableLong(values, "max-records");
        if (maxRecords is <= 0) throw new ArgumentOutOfRangeException(nameof(args), "--max-records deve ser maior que zero.");
        var requestedBy = Get(values, "requested-by") ?? Environment.UserName;
        var reason = Get(values, "reason");
        var correlation = ParseNullableGuid(values, "correlation-id") ?? Guid.NewGuid();

        // MODEL_VALIDATION nunca publica vínculos operacionais. Nos demais modos,
        // --publish=false pode ser usado para ensaio controlado.
        var publishDefault = mode != LinkageRunType.MODEL_VALIDATION;
        var publish = ParseBool(values, "publish", publishDefault);
        if (mode == LinkageRunType.MODEL_VALIDATION && publish)
            throw new InvalidOperationException("MODEL_VALIDATION não pode publicar vínculos correntes.");

        return new LinkageRunOptions(
            mode, modelVersion, observationId, gestor, since, batchSize,
            maxParallelism, maxRecords, requestedBy, reason, correlation, publish);
    }

    public string ToScopeJson() => System.Text.Json.JsonSerializer.Serialize(new
    {
        mode = Mode.ToString(),
        pessoaObservacaoId = PessoaObservacaoId,
        gestorCodigo = GestorCodigo,
        since = Since,
        maxRecords = MaxRecords,
        publish = Publish
    });

    public static string Usage =>
        """
        Jornada.Linkage.Runner — execução única e schedulável

          --mode ON_DEMAND|INCREMENTAL|REPLAY|FULL|MODEL_VALIDATION
          --model-version <N>             opcional; ausente = modelo ATIVO
          --pessoa-observacao-id <ID>     opcional
          --gestor <CODIGO>               opcional
          --since <ISO-8601>              opcional
          --batch-size <100..100000>      padrão 10000
          --max-parallelism <1..32>       padrão 1
          --max-records <N>               opcional
          --requested-by <identificador>  opcional
          --reason <texto>                opcional
          --correlation-id <UUID>         opcional
          --publish true|false            padrão true; MODEL_VALIDATION exige false

        Exemplos:
          --mode INCREMENTAL --batch-size 20000 --max-parallelism 4
          --mode REPLAY --model-version 12 --gestor SMADS --reason "recalibração v12"
          --mode FULL --model-version 13 --batch-size 50000
          --mode MODEL_VALIDATION --model-version 13 --max-records 100000 --publish false
        """;

    private static Dictionary<string, string?> ParsePairs(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (!raw.StartsWith("--", StringComparison.Ordinal)) continue;
            raw = raw[2..];
            var eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                result[raw[..eq]] = raw[(eq + 1)..];
                continue;
            }

            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                value = args[++i];
            result[raw] = value;
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static int ParseInt(IReadOnlyDictionary<string, string?> v, string n, int d) =>
        Get(v, n) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : d;

    private static int? ParseNullableInt(IReadOnlyDictionary<string, string?> v, string n) =>
        Get(v, n) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : null;

    private static long? ParseNullableLong(IReadOnlyDictionary<string, string?> v, string n) =>
        Get(v, n) is { } s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : null;

    private static DateTimeOffset? ParseNullableDate(IReadOnlyDictionary<string, string?> v, string n) =>
        Get(v, n) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var x) ? x : null;

    private static Guid? ParseNullableGuid(IReadOnlyDictionary<string, string?> v, string n) =>
        Get(v, n) is { } s && Guid.TryParse(s, out var x) ? x : null;

    private static bool ParseBool(IReadOnlyDictionary<string, string?> v, string n, bool d) =>
        Get(v, n) is { } s && bool.TryParse(s, out var x) ? x : d;

    private static T ParseEnum<T>(IReadOnlyDictionary<string, string?> v, string n, T d) where T : struct, Enum =>
        Get(v, n) is { } s && Enum.TryParse<T>(s, true, out var x) ? x : d;
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Contracts;

// Contrato histórico de dados V1: apenas fixtures compiladas, nunca linhas lidas do SQL.
public sealed record SplinkSyntheticRecord(
    string UniqueId,
    string SourceDataset,
    string BasePersonId,
    string FirstName,
    string Surname,
    string? Dob);

public sealed record SplinkSyntheticLabel(
    [property: JsonPropertyName("source_dataset_l")] string SourceDatasetLeft,
    [property: JsonPropertyName("unique_id_l")] string UniqueIdLeft,
    [property: JsonPropertyName("source_dataset_r")] string SourceDatasetRight,
    [property: JsonPropertyName("unique_id_r")] string UniqueIdRight,
    decimal ClericalMatchScore);

public sealed record SplinkSyntheticPackage(
    string SchemaVersion,
    string GeneratorVersion,
    string IbgeSourceVersion,
    string IbgeFingerprintSha256,
    int Seed,
    int Partition,
    IReadOnlyList<SplinkSyntheticRecord> Records,
    IReadOnlyList<SplinkSyntheticLabel> Labels);

public sealed record SplinkExternalLevelEstimate(
    string Feature,
    string Level,
    decimal MProbability,
    decimal UProbability);

public sealed record SplinkExternalEstimates(
    string SchemaVersion,
    string SourceSchemaVersion,
    string SplinkVersion,
    string Runner,
    string Scope,
    string NominalSemanticsVersion,
    string GeneratorVersion,
    string IbgeSourceVersion,
    string IbgeFingerprintSha256,
    int Partition,
    int Seed,
    long MaxPairs,
    IReadOnlyList<decimal> NameThresholds,
    int UPopulationRecords,
    int MPositivePairs,
    IReadOnlyList<SplinkExternalLevelEstimate> Estimates);

public sealed record SplinkExternalDiagnosticLevel(
    string Level,
    decimal LocalM,
    decimal ExternalM,
    decimal LocalU,
    decimal ExternalU,
    double LocalLlrNatural,
    double ExternalLlrNatural,
    double AbsoluteLlrDifference);

public sealed record SplinkExternalDiagnostic(
    string Method,
    string Status,
    string Scope,
    string Explanation,
    string InputSchema,
    string ResultSchema,
    string InputSha256,
    string ResultSha256,
    string SplinkVersion,
    int Seed,
    int Partition,
    int PositiveLabels,
    int CanonicalPeople,
    int ExactUnconditionedUPairs,
    decimal MTotalVariation,
    decimal UTotalVariation,
    double MaxAbsoluteLlrDifference,
    IReadOnlyList<SplinkExternalDiagnosticLevel> Levels);

/// <summary>
/// Intercâmbio de diagnóstico externo. O único exportador V1 é a fixture literal
/// compilada abaixo: não há argumento para SQL, pasta, cidadão ou corpus arbitrário.
/// Em especial, uma string "synthetic=true" informada por terceiro NÃO libera exportação.
/// </summary>
public static class SplinkSyntheticConformanceExchange
{
    public const string InputSchema = "JORNADA_SPLINK_EXCHANGE_V1";
    public const string OutputSchema = "JORNADA_SPLINK_ESTIMATES_V1";
    public const string Generator = "EMBEDDED_NOMINAL_CONFORMANCE_V1";
    public const string SyntheticSource = "SYNTHETIC_FIXTURE_NO_IBGE";
    public const string Runner = "calibrador-splink/run_calibration.py";
    public const string Scope = "NOME";
    public const string NominalSemantics = "IDENTITY_NAME_STATES_V1";
    public const string Method = "JORNADA_EXTERNAL_SPLINK_DIAGNOSTIC_V1";
    public const int DefaultSeed = 20260926;

    private static readonly string[] Levels = ["EXACT", "HIGH", "MEDIUM", "LOW"];
    private static readonly decimal[] Thresholds = [0.92m, 0.80m];

    // Correspondem ao antigo smoke de 17/09, SEM identidade ou referência populacional.
    // A fixture não autoriza inferência estatística por coorte, blocking ou HML.
    private static readonly (string FirstL, string LastL, string FirstR, string LastR)[] Cases =
    [
        ("MARIA", "SILVA", "MARIA", "SILVA"),
        ("MARIA", "SOUSA", "MARIA", "SOUZA"),
        ("JOSE", "SANTOS", "JOSE", "SOUZA"),
        ("MARIA", "SILVA", "JOSE", "SOUZA"),
        ("JOAO", "SILVA", "JOAO", "SANTOS"),
        ("ANA", "LIMA", "ANA", "MORAES"),
        ("CARLOS", "SILVA", "CARLOS", "SANTOS"),
        ("PAULO", "COSTA", "PEDRO", "MORAES"),
        ("MARIA", "SOUZA", "MARIA", "SOUSA")
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static SplinkSyntheticPackage CreateFixture(int seed = DefaultSeed)
    {
        var records = new List<SplinkSyntheticRecord>(Cases.Length * 2);
        var labels = new List<SplinkSyntheticLabel>(Cases.Length);
        for (var i = 0; i < Cases.Length; i++)
        {
            var c = Cases[i];
            var person = string.Concat("SYNTH-", (i + 1).ToString(CultureInfo.InvariantCulture));
            var left = string.Concat(person, ":L");
            var right = string.Concat(person, ":R");
            records.Add(new(left, "jornada_calibrador", person, c.FirstL, c.LastL, null));
            records.Add(new(right, "jornada_calibrador", person, c.FirstR, c.LastR, null));
            labels.Add(new("jornada_calibrador", left, "jornada_calibrador", right, 1m));
        }

        // Fingerprint do gerador/fixture, NÃO simula hash de dados do IBGE.
        var sourceText = string.Join("\n", Cases.Select(c =>
            string.Join("|", c.FirstL, c.LastL, c.FirstR, c.LastR))) + "\n";
        return new(
            InputSchema, Generator, SyntheticSource, Sha(sourceText),
            seed, 0, records, labels);
    }

    public static string SerializeFixture(SplinkSyntheticPackage value)
    {
        RequireExactFixture(value);
        return JsonSerializer.Serialize(value, Json) + "\n";
    }

    public static SplinkSyntheticPackage ReadFixture(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var value = JsonSerializer.Deserialize<SplinkSyntheticPackage>(json, Json)
            ?? throw new InvalidDataException("Pacote Splink vazio.");
        RequireExactFixture(value);
        return value;
    }

    private static void RequireExactFixture(SplinkSyntheticPackage candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        // Regeração integral, não um campo de origem autodeclarado.
        var expected = CreateFixture(candidate.Seed);
        if (candidate.SchemaVersion != expected.SchemaVersion ||
            candidate.GeneratorVersion != expected.GeneratorVersion ||
            candidate.IbgeSourceVersion != expected.IbgeSourceVersion ||
            candidate.IbgeFingerprintSha256 != expected.IbgeFingerprintSha256 ||
            candidate.Partition != 0 ||
            candidate.Records is null || candidate.Labels is null ||
            !candidate.Records.SequenceEqual(expected.Records) ||
            !candidate.Labels.SequenceEqual(expected.Labels))
            throw new InvalidDataException(
                "Exportação externa limitada à fixture sintética compilada; origem arbitrária recusada.");
    }

    public static SplinkExternalEstimates ReadExternal(
        string json,
        SplinkSyntheticPackage source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RequireExactFixture(source);
        var result = JsonSerializer.Deserialize<SplinkExternalEstimates>(json, Json)
            ?? throw new InvalidDataException("Resposta Splink vazia.");
        if (result.SchemaVersion != OutputSchema ||
            result.SourceSchemaVersion != InputSchema ||
            result.Scope != Scope ||
            result.Runner != Runner ||
            result.NominalSemanticsVersion != NominalSemantics ||
            result.GeneratorVersion != source.GeneratorVersion ||
            result.IbgeSourceVersion != source.IbgeSourceVersion ||
            result.IbgeFingerprintSha256 != source.IbgeFingerprintSha256 ||
            result.Partition != source.Partition ||
            result.Seed != source.Seed ||
            string.IsNullOrWhiteSpace(result.SplinkVersion) ||
            result.MaxPairs <= 0 ||
            result.UPopulationRecords != Cases.Length ||
            result.MPositivePairs != Cases.Length ||
            result.NameThresholds is null ||
            !result.NameThresholds.SequenceEqual(Thresholds) ||
            result.Estimates is null || result.Estimates.Count != Levels.Length)
            throw new InvalidDataException("Resultado externo com contrato, método ou origem divergente.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in result.Estimates)
        {
            if (row.Feature != Scope ||
                !Levels.Contains(row.Level, StringComparer.Ordinal) ||
                !seen.Add(row.Level) ||
                row.MProbability <= 0m || row.MProbability >= 1m ||
                row.UProbability <= 0m || row.UProbability >= 1m)
                throw new InvalidDataException("Nível, feature ou probabilidade externa inválidos.");
        }
        if (seen.Count != Levels.Length)
            throw new InvalidDataException("Níveis externos incompletos.");

        return result;
    }

    public static SplinkExternalDiagnostic Diagnose(
        SplinkSyntheticPackage package, string resultJson)
    {
        var result = ReadExternal(resultJson, package);
        // m empírico somente dos nove labels positivos da fixture; u EXATO
        // de todos os pares entre as nove pessoas canônicas (uma :L por pessoa).
        // Não é u condicionado ao blocking e nem o sampler aleatório do Splink.
        var leftById = package.Records.ToDictionary(x => x.UniqueId, StringComparer.Ordinal);
        var mStates = package.Labels.Select(label =>
            IdentityComparison.CompareName(
                FullName(leftById[label.UniqueIdLeft]),
                FullName(leftById[label.UniqueIdRight]))).ToArray();
        var canonical = package.Records.Where(x => x.UniqueId.EndsWith(":L", StringComparison.Ordinal)).ToArray();
        var uStates = new List<NameComparisonState>();
        for (var i = 0; i < canonical.Length; i++)
            for (var j = i + 1; j < canonical.Length; j++)
                uStates.Add(IdentityComparison.CompareName(
                    FullName(canonical[i]), FullName(canonical[j])));

        var m = Distribution(mStates);
        var u = Distribution(uStates);
        var byLevel = result.Estimates.ToDictionary(x => x.Level, StringComparer.Ordinal);
        var detail = Levels.Select(level =>
        {
            var external = byLevel[level];
            var localLlr = Math.Log((double)(m[level] / u[level]));
            var remoteLlr = Math.Log((double)(external.MProbability / external.UProbability));
            return new SplinkExternalDiagnosticLevel(
                level, m[level], external.MProbability, u[level], external.UProbability,
                localLlr, remoteLlr, Math.Abs(localLlr - remoteLlr));
        }).ToArray();
        var mtvd = 0.5m * detail.Sum(x => Math.Abs(x.LocalM - x.ExternalM));
        var utvd = 0.5m * detail.Sum(x => Math.Abs(x.LocalU - x.ExternalU));
        return new(
            Method, "DIAGNOSTICO_NAO_GOVERNADO", Scope,
            "m C# suavizado (alpha=0.5) vs m Splink de labels; " +
            "u C# exato em pares distintos vs u Splink aleatório. " +
            "TVD/LLR refletem também métodos amostrais distintos e não provam paridade do scorer.",
            package.SchemaVersion, result.SchemaVersion,
            Sha(SerializeFixture(package)), Sha(resultJson), result.SplinkVersion,
            package.Seed, package.Partition, mStates.Length, canonical.Length,
            uStates.Count, mtvd, utvd, detail.Max(x => x.AbsoluteLlrDifference), detail);
    }

    public static string SerializeDiagnostic(SplinkExternalDiagnostic report) =>
        JsonSerializer.Serialize(report, Json) + "\n";

    private static Dictionary<string, decimal> Distribution(IEnumerable<NameComparisonState> source)
    {
        var values = source.ToArray();
        const decimal alpha = 0.5m;
        return Levels.ToDictionary(
            name => name,
            name => (values.Count(x => x.ToString() == name) + alpha) /
                    (values.Length + alpha * Levels.Length),
            StringComparer.Ordinal);
    }

    private static string FullName(SplinkSyntheticRecord record) =>
        string.Concat(record.FirstName, " ", record.Surname);

    private static string Sha(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}

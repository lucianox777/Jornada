using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Contracts;

// Contrato novo: não reinterpreta o JORNADA_SPLINK_EXCHANGE_V1 histórico
// (que mistura labels positivos e u aleatório num corpus de 9 pessoas).
public sealed record SplinkIbgeReplayPair(int PairIndex, string LeftName, string RightName,
    string CSharpState);

public sealed record SplinkIbgeReplayDocument(
    string SchemaVersion,
    string ReferenceCode,
    string ReferenceContentSha256,
    string FirstNameSex,
    string SurnameSex,
    string BootstrapMethodVersion,
    string JointConstructionVersion,
    string ObservationChannelVersion,
    string ComparisonVersion,
    int Seed,
    int PairCount,
    long FirstNamePublishedOccurrences,
    long SurnamePublishedOccurrences,
    decimal AnalyticExactSyntheticFullNameProbability,
    IReadOnlyList<SplinkIbgeReplayPair> Pairs);

public sealed record SplinkIbgeReplayExternalPair(int PairIndex, string SplinkState);

public sealed record SplinkIbgeReplayExternalResult(
    string SchemaVersion,
    string SourceSchemaVersion,
    string InputSha256,
    string ReferenceContentSha256,
    string ComparisonVersion,
    string SplinkVersion,
    int Seed,
    int PairCount,
    IReadOnlyList<SplinkIbgeReplayExternalPair> Pairs);

public sealed record SplinkIbgeReplayStateSummary(string State, long CSharpSupport,
    long SplinkSupport, decimal CSharpProbability, decimal SplinkProbability,
    decimal AbsoluteProbabilityDifference);

public sealed record SplinkIbgeReplayDiagnostic(
    string SchemaVersion,
    string MethodVersion,
    string Status,
    string ComparisonVersion,
    string ReferenceCode,
    string ReferenceContentSha256,
    string InputSha256,
    string ExternalOutputSha256,
    string SplinkVersion,
    int Seed,
    int PairCount,
    int PairwiseDisagreements,
    decimal TotalVariation,
    decimal MaximumAbsoluteProbabilityDifference,
    IReadOnlyList<SplinkIbgeReplayStateSummary> States,
    string Limitation);

/// <summary>
/// Contrato estrito do replay IBGE: verifica cada par do arquivo de entrada e
/// cada estado retornado externamente. Não importa estimativas no Calibrador,
/// não persiste conferência governada e não possui acesso SQL/PII.
/// </summary>
public static class SplinkIbgeReplayContract
{
    public const string InputSchema = "JORNADA_SPLINK_IBGE_U_REPLAY_V1";
    public const string ExternalSchema = "JORNADA_SPLINK_IBGE_U_REPLAY_RESULT_V1";
    public const string ReportSchema = "JORNADA_SPLINK_IBGE_U_REPLAY_DIAGNOSTIC_V1";
    public const string ComparisonV1 = IdentityComparison.NameComparisonVersionV1;
    public const string MethodVersion = "IBGE_SAME_PAIR_SPLINK_CONFORMANCE_V1";
    private static readonly string[] States = ["EXACT", "HIGH", "MEDIUM", "LOW"];
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static string SerializeInput(SplinkIbgeReplayDocument doc)
    {
        ValidateInput(doc);
        return JsonSerializer.Serialize(doc, JsonOptions) + "\n";
    }

    public static SplinkIbgeReplayDocument ParseInput(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var doc = JsonSerializer.Deserialize<SplinkIbgeReplayDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Replay IBGE vazio.");
        ValidateInput(doc);
        return doc;
    }

    public static void ValidateInput(SplinkIbgeReplayDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.SchemaVersion != InputSchema ||
            d.ReferenceCode != "CENSO2022_NOMES_BRASIL_V1" ||
            !ShaValid(d.ReferenceContentSha256) ||
            d.FirstNameSex is not ("TODOS" or "FEMININO") ||
            d.SurnameSex != "TODOS" ||
            d.BootstrapMethodVersion != "IBGE_NOMINAL_U_BOOTSTRAP_V1" ||
            d.JointConstructionVersion != "INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1" ||
            d.ObservationChannelVersion != "CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1" ||
            d.ComparisonVersion != ComparisonV1 ||
            d.PairCount is < 1 or > 100_000 ||
            d.Pairs is null || d.Pairs.Count != d.PairCount ||
            d.FirstNamePublishedOccurrences <= 0 || d.SurnamePublishedOccurrences <= 0 ||
            d.AnalyticExactSyntheticFullNameProbability is <= 0m or > 1m)
            throw new InvalidDataException("Metadados IBGE/synthetic replay incompletos ou incompatíveis.");

        var i = 0;
        foreach (var p in d.Pairs)
        {
            if (p.PairIndex != i++ ||
                string.IsNullOrWhiteSpace(p.LeftName) ||
                string.IsNullOrWhiteSpace(p.RightName) ||
                !States.Contains(p.CSharpState, StringComparer.Ordinal) ||
                !string.Equals(
                    IdentityComparison.CompareName(p.LeftName, p.RightName,
                        NameComparisonContract.WholeNameJaroWinklerV1).ToString(),
                    p.CSharpState, StringComparison.Ordinal))
                throw new InvalidDataException("Par do replay com ordem, nome ou estado C# inválido.");
        }
    }

    public static SplinkIbgeReplayDiagnostic Diagnose(string inputJson, string resultJson)
    {
        var source = ParseInput(inputJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);
        var external = JsonSerializer.Deserialize<SplinkIbgeReplayExternalResult>(resultJson, JsonOptions)
            ?? throw new InvalidDataException("Resultado Splink IBGE vazio.");
        var inputSha = Sha(inputJson);
        if (external.SchemaVersion != ExternalSchema ||
            external.SourceSchemaVersion != InputSchema ||
            !string.Equals(external.InputSha256, inputSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(external.ReferenceContentSha256, source.ReferenceContentSha256,
                StringComparison.OrdinalIgnoreCase) ||
            external.ComparisonVersion != source.ComparisonVersion ||
            string.IsNullOrWhiteSpace(external.SplinkVersion) ||
            external.Seed != source.Seed || external.PairCount != source.PairCount ||
            external.Pairs is null || external.Pairs.Count != source.PairCount)
            throw new InvalidDataException("Evidência externa incompatível com o replay IBGE exato.");

        var local = States.ToDictionary(state => state, _ => 0L, StringComparer.Ordinal);
        var remote = States.ToDictionary(state => state, _ => 0L, StringComparer.Ordinal);
        var disagreements = 0;
        var seen = new bool[source.PairCount];
        foreach (var pair in source.Pairs)
            local[pair.CSharpState]++;
        foreach (var pair in external.Pairs)
        {
            if (pair.PairIndex < 0 || pair.PairIndex >= source.PairCount ||
                seen[pair.PairIndex] ||
                !remote.ContainsKey(pair.SplinkState))
                throw new InvalidDataException("Estado Splink duplicado, desconhecido ou fora do replay.");
            seen[pair.PairIndex] = true;
            remote[pair.SplinkState]++;
            if (!string.Equals(
                    source.Pairs[pair.PairIndex].CSharpState, pair.SplinkState,
                    StringComparison.Ordinal))
                disagreements++;
        }
        if (seen.Any(found => !found))
            throw new InvalidDataException("Runner Splink omitiu pares do replay.");

        var summaries = States.Select(state =>
        {
            var localProbability = (decimal)local[state] / source.PairCount;
            var remoteProbability = (decimal)remote[state] / source.PairCount;
            return new SplinkIbgeReplayStateSummary(state, local[state], remote[state],
                localProbability, remoteProbability,
                Math.Abs(localProbability - remoteProbability));
        }).ToArray();
        return new(
            ReportSchema, MethodVersion,
            disagreements == 0 ? "ESTADOS_IDENTICOS_DIAGNOSTICO" : "ESTADOS_DIVERGENTES_DIAGNOSTICO",
            source.ComparisonVersion, source.ReferenceCode, source.ReferenceContentSha256,
            inputSha, Sha(resultJson), external.SplinkVersion, source.Seed, source.PairCount,
            disagreements, summaries.Sum(x => x.AbsoluteProbabilityDifference) / 2m,
            summaries.Max(x => x.AbsoluteProbabilityDifference), summaries,
            "Só compara estados C# vs Splink nos mesmos pares sintéticos IBGE. " +
            "Não valida método de bootstrap, canal de erros, u condicionado, scorer m/u ou população #31.");
    }

    public static string SerializeDiagnostic(SplinkIbgeReplayDiagnostic value) =>
        JsonSerializer.Serialize(value, JsonOptions) + "\n";

    private static bool ShaValid(string? hash) =>
        hash is { Length: 64 } &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    public static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

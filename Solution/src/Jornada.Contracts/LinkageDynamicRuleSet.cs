using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Jornada.Contracts;

public sealed record LinkageBlockingPass(string PassId, IReadOnlyList<string> Fields)
{
    public static LinkageBlockingPass Create(string passId, IEnumerable<string> fields)
    {
        if (string.IsNullOrWhiteSpace(passId))
            throw new ArgumentException("Blocking pass id is required.", nameof(passId));
        ArgumentNullException.ThrowIfNull(fields);

        var normalizedFields = fields
            .Select(static x => x?.Trim() ?? string.Empty)
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        if (normalizedFields.Length == 0)
            throw new ArgumentException("At least one blocking field is required for a pass.", nameof(fields));

        return new LinkageBlockingPass(passId.Trim(), normalizedFields);
    }
}

/// <summary>
/// Pacote imutável de regras promovidas pelo Calibrador e consumidas sem reinterpretação pelo Avaliador/Runner.
/// Rulesets legados continuam representados por BlockingFields; novos rulesets podem declarar múltiplos passes explícitos.
/// </summary>
public sealed record LinkageDynamicRuleSet(
    string RuleSetVersion,
    string AlgorithmVersion,
    IReadOnlyList<string> BlockingFields,
    IReadOnlyDictionary<string, decimal> Parameters,
    string? IbgeSourceVersion,
    string? IbgeFingerprintSha256,
    string FingerprintSha256)
{
    public IReadOnlyList<LinkageBlockingPass> BlockingPasses { get; init; } = Array.Empty<LinkageBlockingPass>();

    [JsonIgnore]
    public IReadOnlyList<LinkageBlockingPass> EffectiveBlockingPasses => BlockingPasses.Count > 0
        ? BlockingPasses
        : new[] { new LinkageBlockingPass("legacy", BlockingFields) };

    public static LinkageDynamicRuleSet Create(
        string ruleSetVersion,
        string algorithmVersion,
        IEnumerable<string> blockingFields,
        IEnumerable<KeyValuePair<string, decimal>> parameters,
        string? ibgeSourceVersion = null,
        string? ibgeFingerprintSha256 = null)
    {
        var normalized = NormalizeCommon(
            ruleSetVersion,
            algorithmVersion,
            blockingFields,
            parameters,
            ibgeSourceVersion,
            ibgeFingerprintSha256);

        var canonical = BuildCanonicalPrefix(normalized);
        foreach (var field in normalized.Fields)
            canonical.Append("B\t").Append(field).Append('\n');
        AppendParameters(canonical, normalized.ParameterArray);

        return new LinkageDynamicRuleSet(
            normalized.RuleSetVersion,
            normalized.AlgorithmVersion,
            normalized.Fields,
            normalized.ParameterMap,
            normalized.IbgeVersion,
            normalized.IbgeHash,
            Hash(canonical));
    }

    public static LinkageDynamicRuleSet CreateWithPasses(
        string ruleSetVersion,
        string algorithmVersion,
        IEnumerable<LinkageBlockingPass> blockingPasses,
        IEnumerable<KeyValuePair<string, decimal>> parameters,
        string? ibgeSourceVersion = null,
        string? ibgeFingerprintSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(blockingPasses);
        var passes = blockingPasses
            .Select(static pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(static pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();

        if (passes.Length == 0)
            throw new ArgumentException("At least one blocking pass is required.", nameof(blockingPasses));
        if (passes.Select(static pass => pass.PassId).Distinct(StringComparer.Ordinal).Count() != passes.Length)
            throw new ArgumentException("Blocking pass ids must be unique.", nameof(blockingPasses));

        var flattenedFields = passes
            .SelectMany(static pass => pass.Fields)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static field => field, StringComparer.Ordinal)
            .ToArray();

        var normalized = NormalizeCommon(
            ruleSetVersion,
            algorithmVersion,
            flattenedFields,
            parameters,
            ibgeSourceVersion,
            ibgeFingerprintSha256);

        var canonical = BuildCanonicalPrefix(normalized);
        foreach (var pass in passes)
        {
            canonical.Append("PASS\t").Append(pass.PassId).Append('\n');
            foreach (var field in pass.Fields)
                canonical.Append("F\t").Append(field).Append('\n');
        }
        AppendParameters(canonical, normalized.ParameterArray);

        return new LinkageDynamicRuleSet(
            normalized.RuleSetVersion,
            normalized.AlgorithmVersion,
            normalized.Fields,
            normalized.ParameterMap,
            normalized.IbgeVersion,
            normalized.IbgeHash,
            Hash(canonical))
        {
            BlockingPasses = passes
        };
    }

    private static NormalizedRuleSet NormalizeCommon(
        string ruleSetVersion,
        string algorithmVersion,
        IEnumerable<string> blockingFields,
        IEnumerable<KeyValuePair<string, decimal>> parameters,
        string? ibgeSourceVersion,
        string? ibgeFingerprintSha256)
    {
        if (string.IsNullOrWhiteSpace(ruleSetVersion))
            throw new ArgumentException("Rule-set version is required.", nameof(ruleSetVersion));
        if (string.IsNullOrWhiteSpace(algorithmVersion))
            throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));
        ArgumentNullException.ThrowIfNull(blockingFields);
        ArgumentNullException.ThrowIfNull(parameters);

        var fields = blockingFields
            .Select(static x => x?.Trim() ?? string.Empty)
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        if (fields.Length == 0)
            throw new ArgumentException("At least one blocking field is required.", nameof(blockingFields));

        var parameterArray = parameters
            .OrderBy(static x => x.Key, StringComparer.Ordinal)
            .ToArray();
        if (parameterArray.Any(static x => string.IsNullOrWhiteSpace(x.Key)))
            throw new ArgumentException("Parameter names cannot be empty.", nameof(parameters));
        if (parameterArray.Select(static x => x.Key).Distinct(StringComparer.Ordinal).Count() != parameterArray.Length)
            throw new ArgumentException("Parameter names must be unique.", nameof(parameters));

        var parameterMap = parameterArray.ToDictionary(static x => x.Key, static x => x.Value, StringComparer.Ordinal);
        var normalizedIbgeVersion = NormalizeOptional(ibgeSourceVersion);
        var normalizedIbgeHash = NormalizeOptional(ibgeFingerprintSha256)?.ToLowerInvariant();
        if ((normalizedIbgeVersion is null) != (normalizedIbgeHash is null))
            throw new ArgumentException("IBGE source version and fingerprint must be informed together.");

        return new NormalizedRuleSet(
            ruleSetVersion.Trim(),
            algorithmVersion.Trim(),
            fields,
            parameterArray,
            parameterMap,
            normalizedIbgeVersion,
            normalizedIbgeHash);
    }

    private static StringBuilder BuildCanonicalPrefix(NormalizedRuleSet normalized) =>
        new StringBuilder()
            .Append(normalized.RuleSetVersion).Append('\n')
            .Append(normalized.AlgorithmVersion).Append('\n')
            .Append(normalized.IbgeVersion ?? "-").Append('\n')
            .Append(normalized.IbgeHash ?? "-").Append('\n');

    private static void AppendParameters(StringBuilder canonical, IEnumerable<KeyValuePair<string, decimal>> parameters)
    {
        foreach (var parameter in parameters)
            canonical.Append("P\t").Append(parameter.Key).Append('\t')
                .Append(parameter.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string Hash(StringBuilder canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record NormalizedRuleSet(
        string RuleSetVersion,
        string AlgorithmVersion,
        IReadOnlyList<string> Fields,
        IReadOnlyList<KeyValuePair<string, decimal>> ParameterArray,
        IReadOnlyDictionary<string, decimal> ParameterMap,
        string? IbgeVersion,
        string? IbgeHash);
}

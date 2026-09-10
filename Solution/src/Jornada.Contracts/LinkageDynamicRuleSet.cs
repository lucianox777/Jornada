using System.Security.Cryptography;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Pacote imutável de regras promovidas pelo Calibrador e consumidas sem reinterpretação pelo Avaliador.
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
    public static LinkageDynamicRuleSet Create(
        string ruleSetVersion,
        string algorithmVersion,
        IEnumerable<string> blockingFields,
        IEnumerable<KeyValuePair<string, decimal>> parameters,
        string? ibgeSourceVersion = null,
        string? ibgeFingerprintSha256 = null)
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

        var canonical = new StringBuilder()
            .Append(ruleSetVersion.Trim()).Append('\n')
            .Append(algorithmVersion.Trim()).Append('\n')
            .Append(normalizedIbgeVersion ?? "-").Append('\n')
            .Append(normalizedIbgeHash ?? "-").Append('\n');

        foreach (var field in fields)
            canonical.Append("B\t").Append(field).Append('\n');
        foreach (var parameter in parameterArray)
            canonical.Append("P\t").Append(parameter.Key).Append('\t')
                .Append(parameter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new LinkageDynamicRuleSet(
            ruleSetVersion.Trim(),
            algorithmVersion.Trim(),
            fields,
            parameterMap,
            normalizedIbgeVersion,
            normalizedIbgeHash,
            fingerprint);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

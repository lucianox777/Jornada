using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vincula uma avaliação independente a exatamente um ruleset produzido pelo Calibrador.
/// O Avaliador registra a identidade do pacote sem reconstituir ou reinterpretar suas regras.
/// </summary>
public sealed record IndependentRuleSetEvaluationManifest(
    IndependentEvaluationManifest Evaluation,
    string RuleSetVersion,
    string RuleSetFingerprintSha256,
    string AlgorithmVersion,
    string? IbgeSourceVersion,
    string? IbgeFingerprintSha256,
    string FingerprintSha256);

public static class IndependentRuleSetEvaluationManifestCatalog
{
    public const string MethodVersion = "LINKAGE_INDEPENDENT_RULESET_EVALUATION_V1";

    public static IndependentRuleSetEvaluationManifest Bind(
        IndependentEvaluationManifest evaluation,
        LinkageDynamicRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(ruleSet);

        var canonical = new StringBuilder()
            .Append(MethodVersion).Append('\n')
            .Append(evaluation.FingerprintSha256).Append('\n')
            .Append(ruleSet.RuleSetVersion).Append('\n')
            .Append(ruleSet.FingerprintSha256).Append('\n')
            .Append(ruleSet.AlgorithmVersion).Append('\n')
            .Append(ruleSet.IbgeSourceVersion ?? "-").Append('\n')
            .Append(ruleSet.IbgeFingerprintSha256 ?? "-").Append('\n');

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new IndependentRuleSetEvaluationManifest(
            evaluation,
            ruleSet.RuleSetVersion,
            ruleSet.FingerprintSha256,
            ruleSet.AlgorithmVersion,
            ruleSet.IbgeSourceVersion,
            ruleSet.IbgeFingerprintSha256,
            fingerprint);
    }

    public static void EnsureSameRuleSet(
        IndependentRuleSetEvaluationManifest expected,
        LinkageDynamicRuleSet actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.RuleSetVersion, actual.RuleSetVersion, StringComparison.Ordinal) ||
            !string.Equals(expected.RuleSetFingerprintSha256, actual.FingerprintSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Evaluator cannot mix or replace the rule-set version bound to this evaluation run.");
        }
    }
}

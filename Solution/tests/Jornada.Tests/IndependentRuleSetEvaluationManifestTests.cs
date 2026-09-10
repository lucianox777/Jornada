using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IndependentRuleSetEvaluationManifestTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string D = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    [Test]
    public void Binding_records_exact_ruleset_identity()
    {
        var evaluation = IndependentEvaluationManifestCatalog.Create(
            "eval-v1", "model-v1", A, B, C, 100, 10,
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var rules = LinkageDynamicRuleSet.Create(
            "rules-v7", "calibrator-v3",
            new[] { BlockingCandidateFeatureCatalog.FirstName, BlockingCandidateFeatureCatalog.BirthYear },
            new[] { new KeyValuePair<string, decimal>("threshold", 0.91m) },
            "ibge-v1", D);

        var manifest = IndependentRuleSetEvaluationManifestCatalog.Bind(evaluation, rules);

        Assert.That(manifest.RuleSetVersion, Is.EqualTo("rules-v7"));
        Assert.That(manifest.RuleSetFingerprintSha256, Is.EqualTo(rules.FingerprintSha256));
        Assert.That(manifest.IbgeFingerprintSha256, Is.EqualTo(D));
        Assert.DoesNotThrow(() => IndependentRuleSetEvaluationManifestCatalog.EnsureSameRuleSet(manifest, rules));
    }

    [Test]
    public void Evaluator_rejects_another_ruleset_even_when_algorithm_is_the_same()
    {
        var evaluation = IndependentEvaluationManifestCatalog.Create(
            "eval-v1", "model-v1", A, B, C, 100, 10,
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var first = LinkageDynamicRuleSet.Create(
            "rules-v7", "calibrator-v3",
            new[] { BlockingCandidateFeatureCatalog.FirstName },
            new[] { new KeyValuePair<string, decimal>("threshold", 0.91m) });
        var second = LinkageDynamicRuleSet.Create(
            "rules-v8", "calibrator-v3",
            new[] { BlockingCandidateFeatureCatalog.FirstName },
            new[] { new KeyValuePair<string, decimal>("threshold", 0.92m) });

        var manifest = IndependentRuleSetEvaluationManifestCatalog.Bind(evaluation, first);

        Assert.Throws<InvalidOperationException>(() =>
            IndependentRuleSetEvaluationManifestCatalog.EnsureSameRuleSet(manifest, second));
    }
}

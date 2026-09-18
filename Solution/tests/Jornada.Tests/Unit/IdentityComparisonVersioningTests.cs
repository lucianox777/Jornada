using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityComparisonVersioningTests
{
    [Test]
    public void Legacy_alias_remains_pinned_to_v1()
    {
        const string left = "MARIA APARECIDA DA SILVA VALIDACAO UNICA";
        const string right = "MARIA APARECIDA DA SOUZA VALIDACAO UNICA";

        Assert.Multiple(() =>
        {
            Assert.That(IdentityComparison.CompareName(left, right),
                Is.EqualTo(IdentityComparison.CompareNameV1(left, right)));
            Assert.That(IdentityComparison.CompareName(left, right,
                    NameComparisonContract.WholeNameJaroWinklerV1),
                Is.EqualTo(IdentityComparison.CompareNameV1(left, right)));
            Assert.That(IdentityComparison.CompareNameV1(left, right),
                Is.EqualTo(NameComparisonState.HIGH));
        });
    }

    [Test]
    public void V2_does_not_let_long_shared_context_hide_one_strong_token_substitution()
    {
        const string left = "MARIA APARECIDA DA SILVA VALIDACAO UNICA";
        const string right = "MARIA APARECIDA DA SOUZA VALIDACAO UNICA";

        Assert.Multiple(() =>
        {
            Assert.That(IdentityComparison.JaroWinkler(
                    IdentityComparison.NormalizeText(left)!,
                    IdentityComparison.NormalizeText(right)!),
                Is.GreaterThanOrEqualTo(0.92d));
            Assert.That(IdentityComparison.CompareNameV2(left, right),
                Is.EqualTo(NameComparisonState.LOW));
        });
    }

    [Test]
    public void V2_preserves_high_similarity_for_small_change_inside_one_aligned_token()
    {
        const string left = "GABRIEL OLIVEIRA LIMA VALIDACAO UNICA";
        const string right = "GABRIELA OLIVEIRA LIMA VALIDACAO UNICA";

        Assert.That(IdentityComparison.CompareNameV2(left, right),
            Is.EqualTo(NameComparisonState.HIGH));
    }

    [Test]
    public void V2_does_not_invent_token_alignment_policy_when_token_counts_differ()
    {
        const string left = "MARIA SILVA";
        const string right = "MARIA DE SILVA";

        Assert.That(IdentityComparison.CompareNameV2(left, right),
            Is.EqualTo(IdentityComparison.CompareNameV1(left, right)));
    }
}

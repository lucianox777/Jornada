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
    public void V2_does_not_inflate_v1_when_full_name_similarity_is_only_medium()
    {
        const string left = "GABRIEL OLIVEIRA LIMA VALIDACAO UNICA";
        const string right = "GABRIELA OLIVEIRA LIMA VALIDACAO UNICA";

        Assert.Multiple(() =>
        {
            Assert.That(IdentityComparison.CompareNameV1(left, right),
                Is.EqualTo(NameComparisonState.MEDIUM));
            Assert.That(IdentityComparison.CompareNameV2(left, right),
                Is.EqualTo(NameComparisonState.MEDIUM));
        });
    }

    [Test]
    public void V2_reuses_versioned_ptbr_particle_boundary_in_structural_guard()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdentityComparison.CompareNameV2("MARIA DA SILVA", "MARIA DE SILVA"),
                Is.EqualTo(NameComparisonState.HIGH));
            Assert.That(IdentityComparison.CompareNameV2("MARIA SILVA", "MARIA DE SILVA"),
                Is.EqualTo(NameComparisonState.HIGH));
        });
    }

    [Test]
    public void V2_does_not_invent_token_alignment_policy_when_token_counts_differ()
    {
        const string left = "MARIA SILVA";
        const string right = "MARIA JOSE SILVA";

        Assert.That(IdentityComparison.CompareNameV2(left, right),
            Is.EqualTo(IdentityComparison.CompareNameV1(left, right)));
    }
}

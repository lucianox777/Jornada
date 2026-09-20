using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration")]
public sealed class IndependentRawComparatorConferenceParityTests
{
    [Test]
    public void Independent_normalization_matches_canonical_contract_on_synthetic_corpus()
    {
        string?[] values =
        [
            null,
            "",
            "   ",
            "José da Silva",
            "  José   da\tSilva  ",
            "MARIA DE FÁTIMA",
            "João\nBatista",
            "ÁÉÍÓÚ Ç ÃÕ",
            "A.",
            "  Ana   Souza   Lima "
        ];

        Assert.Multiple(() =>
        {
            foreach (var value in values)
            {
                Assert.That(
                    IndependentRawComparatorConference.NormalizeText(value),
                    Is.EqualTo(IdentityComparison.NormalizeText(value)),
                    $"Normalização divergente para {value ?? "<null>"}.");
            }
        });
    }

    [Test]
    public void Independent_name_comparator_matches_v1_and_v2_on_cross_product()
    {
        string?[] names =
        [
            null,
            "",
            "MARIA APARECIDA DA SILVA VALIDACAO UNICA",
            "MARIA APARECIDA DA SOUZA VALIDACAO UNICA",
            "GABRIEL OLIVEIRA LIMA VALIDACAO UNICA",
            "GABRIELA OLIVEIRA LIMA VALIDACAO UNICA",
            "MARIA DA SILVA",
            "MARIA DE SILVA",
            "MARIA SILVA",
            "MARIA JOSE SILVA",
            "José de Souza",
            "JOSE DE SOUZA",
            "ANA P. COSTA",
            "ANA PAULA COSTA",
            "CARLOS EDUARDO SANTOS",
            "CARLOS EDGAR SANTOS",
            "X",
            "Y"
        ];

        foreach (var contract in Enum.GetValues<NameComparisonContract>())
        {
            foreach (var left in names)
            foreach (var right in names)
            {
                Assert.That(
                    IndependentRawComparatorConference.CompareName(left, right, contract),
                    Is.EqualTo(IdentityComparison.CompareName(left, right, contract)),
                    $"{contract}: {left ?? "<null>"} x {right ?? "<null>"}");
            }
        }
    }

    [Test]
    public void Independent_name_comparator_reproduces_known_versioned_boundaries()
    {
        const string substitutionLeft =
            "MARIA APARECIDA DA SILVA VALIDACAO UNICA";
        const string substitutionRight =
            "MARIA APARECIDA DA SOUZA VALIDACAO UNICA";
        const string mediumLeft =
            "GABRIEL OLIVEIRA LIMA VALIDACAO UNICA";
        const string mediumRight =
            "GABRIELA OLIVEIRA LIMA VALIDACAO UNICA";

        Assert.Multiple(() =>
        {
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    substitutionLeft,
                    substitutionRight,
                    NameComparisonContract.WholeNameJaroWinklerV1),
                Is.EqualTo(NameComparisonState.HIGH));
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    substitutionLeft,
                    substitutionRight,
                    NameComparisonContract.PtBrContentTokenGuardV2),
                Is.EqualTo(NameComparisonState.LOW));
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    mediumLeft,
                    mediumRight,
                    NameComparisonContract.WholeNameJaroWinklerV1),
                Is.EqualTo(NameComparisonState.MEDIUM));
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    mediumLeft,
                    mediumRight,
                    NameComparisonContract.PtBrContentTokenGuardV2),
                Is.EqualTo(NameComparisonState.MEDIUM));
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    "MARIA DA SILVA",
                    "MARIA DE SILVA",
                    NameComparisonContract.PtBrContentTokenGuardV2),
                Is.EqualTo(NameComparisonState.HIGH));
            Assert.That(
                IndependentRawComparatorConference.CompareName(
                    "MARIA SILVA",
                    "MARIA DE SILVA",
                    NameComparisonContract.PtBrContentTokenGuardV2),
                Is.EqualTo(NameComparisonState.HIGH));
        });
    }

    [Test]
    public void Independent_birth_classifier_matches_canonical_and_covers_all_states()
    {
        var pairs = new[]
        {
            (new DateOnly(1975, 6, 15), new DateOnly(1975, 6, 15)),
            (new DateOnly(1975, 6, 7), new DateOnly(1975, 7, 6)),
            (new DateOnly(1975, 6, 15), new DateOnly(2075, 6, 15)),
            (new DateOnly(1975, 6, 15), new DateOnly(1975, 7, 15)),
            (new DateOnly(1975, 6, 15), new DateOnly(1975, 8, 25)),
            (new DateOnly(1975, 6, 15), new DateOnly(1932, 6, 15)),
            (new DateOnly(1975, 6, 15), new DateOnly(1983, 9, 24))
        };

        var observed = new HashSet<string>(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var (left, right) in pairs)
            {
                var independent =
                    IndependentRawComparatorConference.ClassifyBirth(left, right);
                var canonical = BirthDateSemanticEvidence.Classify(left, right);

                Assert.That(independent, Is.EqualTo(canonical), $"{left} x {right}");
                observed.Add(independent);

                Assert.That(
                    IndependentRawComparatorConference.ClassifyBirth(right, left),
                    Is.EqualTo(BirthDateSemanticEvidence.Classify(right, left)),
                    $"simetria {right} x {left}");
            }

            Assert.That(
                observed.OrderBy(static x => x, StringComparer.Ordinal),
                Is.EqualTo(
                    BirthDateSemanticEvidence.States
                        .OrderBy(static x => x, StringComparer.Ordinal)));
        });
    }

    [Test]
    public void Independent_raw_comparator_source_is_architecturally_isolated()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Evaluation",
            "IndependentRawComparatorConference.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Evaluation",
            "Jornada.Linkage.Evaluation.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("IdentityComparison."));
            Assert.That(source, Does.Not.Contain("BirthDateSemanticEvidence."));
            Assert.That(source, Does.Not.Contain("FellegiSunterScoring"));
            Assert.That(source, Does.Not.Contain("ProbabilisticLinkageDecisions"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Core"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Runner"));
            Assert.That(
                IndependentRawComparatorConference.MethodVersion,
                Is.EqualTo("JORNADA_RAW_COMPARATOR_CONFERENCE_V1"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}

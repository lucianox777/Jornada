using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class AbbreviationCompatibilityTrainingDiagnosticsTests
{
    [Test]
    public void Measures_name_and_mother_support_without_inventing_missing_denominator()
    {
        var pairs = new[]
        {
            new IdentityTrainingPair(
                "Maria S. Silva", new DateOnly(1980,1,1), "Ana M. Souza",
                "Maria Souza Silva", new DateOnly(1980,1,1), "Ana Maria Souza"),
            new IdentityTrainingPair(
                "Joao Pedro Lima", new DateOnly(1981,1,1), null,
                "Joao Paulo Lima", new DateOnly(1982,1,1), "Maria Lima"),
            new IdentityTrainingPair(
                "Carlos A. Rocha", new DateOnly(1983,1,1), "Marta Rocha",
                "Carlos T. Rocha", new DateOnly(1984,1,1), "Marta Rocha")
        };

        var measured = AbbreviationCompatibilityTrainingDiagnostics.Measure(pairs);

        Assert.Multiple(() =>
        {
            Assert.That(measured.NameDenominator, Is.EqualTo(3));
            Assert.That(measured.NameCompatible, Is.EqualTo(1));
            Assert.That(measured.MotherNameDenominator, Is.EqualTo(2));
            Assert.That(measured.MotherNameCompatible, Is.EqualTo(1));
        });
    }

    [Test]
    public void Append_persists_support_and_source_markers_without_creating_scoring_parameter()
    {
        var m = new[]
        {
            new IdentityTrainingPair(
                "Maria S Silva", new DateOnly(1980,1,1), "Ana M Souza",
                "Maria Souza Silva", new DateOnly(1980,1,1), "Ana Maria Souza")
        };
        var u = new[]
        {
            new IdentityTrainingPair(
                "Maria Santos Silva", new DateOnly(1980,1,1), "Ana Marta Souza",
                "Maria Souza Silva", new DateOnly(1981,1,1), "Ana Maria Souza")
        };

        var result = AbbreviationCompatibilityTrainingDiagnostics.Append(
            new Dictionary<string, decimal> { ["KEEP"] = 7m },
            m,
            u);

        Assert.Multiple(() =>
        {
            Assert.That(result["KEEP"], Is.EqualTo(7m));
            Assert.That(result["DIAG_ABBREV_M_NOME_SUPPORT"], Is.EqualTo(1m));
            Assert.That(result["DIAG_ABBREV_U_NOME_SUPPORT"], Is.EqualTo(0m));
            Assert.That(result["DIAG_ABBREV_M_NOME_RATE"], Is.EqualTo(1m));
            Assert.That(result["DIAG_ABBREV_U_NOME_RATE"], Is.EqualTo(0m));
            Assert.That(result["DIAG_ABBREV_NOME_OBSERVED_IN_BOTH_M_U"], Is.EqualTo(0m));
            Assert.That(result["DIAG_ABBREV_M_REFERENCE_CPF_INTERGESTOR_V1"], Is.EqualTo(1m));
            Assert.That(result["DIAG_ABBREV_U_REFERENCE_BLOCKING_GOLD_GOLD_V1"], Is.EqualTo(1m));
            Assert.That(result.Keys.Any(k => k.Contains("LLR", StringComparison.Ordinal)), Is.False);
        });
    }
}

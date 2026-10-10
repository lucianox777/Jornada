using Jornada.Contracts;

namespace Jornada.Tests.Unit;

/// <summary>
/// Fixtures dos 53 pares sintéticos divergentes do replay IBGE × Splink 4.0.17.
/// O estado Splink é evidência externa histórica, NÃO é um resultado exigido do C#.
/// Não muda o contrato persistido WHOLE_NAME_JARO_WINKLER_V1.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class SplinkIbgeObservedDivergenceRegressionTests
{
    // Evidência DEV: replays TODOS/FEMININO, 10.000 pares cada, 27/09/2026.
    // Originais SHA256: a5cea4f00027427190c726f92329724e6f92fc3bcd64c67f4d9f758ca4d2af64
    //                  da89b9a7a6594f52065e64284732f032adb3033c347ba969684161cd028f378c
    private const string Observed = """
TODOS|JOSIANE SANTOS|ELISA SANTOS|LOW|MEDIUM
TODOS|JOSE POSSOBOM|JOSE SILVA|MEDIUM|LOW
TODOS|FRANCIELDA MENDES|FRANCISCO CARDOSO|MEDIUM|LOW
TODOS|MARCO PEREIRA|MARLON OLIVEIRA|LOW|MEDIUM
TODOS|MARINALVA APARECIDA|MARIA SILVA|MEDIUM|LOW
TODOS|JOSEFA SILVA|JOSE CONCEICAO|MEDIUM|LOW
TODOS|ANTONIA ARAUJO|ANDRIANA SANTOS|LOW|MEDIUM
TODOS|EDCARLOS LIMA|EDNA SILVA|LOW|MEDIUM
TODOS|JOAO FIGUEIRA|JOAO CONCEICAO|MEDIUM|LOW
TODOS|MARIA SILVA|AMARILDO SILVA|LOW|MEDIUM
TODOS|DANIEL SILVA|EDINALVA SILVA|LOW|MEDIUM
TODOS|JOSE CUREAU|JOSE ALBANO|MEDIUM|LOW
TODOS|JOSE FELICIO|JOSE BARBOSA|MEDIUM|LOW
TODOS|JENIVAL SANTOS|VANIA SANTOS|LOW|MEDIUM
TODOS|SAMUEL RODRIGUES|DAMIAO RODRIGUES|LOW|MEDIUM
TODOS|JOAO MONTE|JOAO FUSCA|MEDIUM|LOW
TODOS|JOAO TOLENTINO|JOELTON COSTA|LOW|MEDIUM
TODOS|MARIANE SANTOS|MARIA FERREIRA|MEDIUM|LOW
TODOS|MARIA CARVALHO|MARIA PEREIRA|MEDIUM|LOW
TODOS|ADRIELLY DUARTE|ADRIANA ALVES|MEDIUM|LOW
FEMININO|ANA PEREIRA|ANA CRUZ|MEDIUM|LOW
FEMININO|ANA SILVA|ANDREIA UVA|LOW|MEDIUM
FEMININO|MARTA SANTOS|MARIA CARDOSO|LOW|MEDIUM
FEMININO|ANA LEITE|ANA RUFINO|MEDIUM|LOW
FEMININO|CAMILA SILVA|MARIA SILVA|LOW|MEDIUM
FEMININO|DEBORA QUEIROZ|RAFAELA QUEIROZ|LOW|MEDIUM
FEMININO|ANA FERREIRQ|MARIA FEREIRA|LOW|MEDIUM
FEMININO|ANA SOUZA|ANA ALMEIDA|MEDIUM|LOW
FEMININO|LIRIA GONSALVES|LETICIA GESSO|LOW|MEDIUM
FEMININO|MARIA SILVA|MARIA VIOL|MEDIUM|HIGH
FEMININO|ANA ALMEIDA|ANA GUERREIRO|MEDIUM|LOW
FEMININO|MARIA FERREIRA|MARILEIA JESUS|MEDIUM|LOW
FEMININO|TEREZA BARROS|TERESINHA SILVA|MEDIUM|LOW
FEMININO|ANA SANTOS|ANA VIDAL|MEDIUM|LOW
FEMININO|ANA SANTOS|BRUNA SANTOS|LOW|MEDIUM
FEMININO|MARIA CHAVES|MARI MORAIS|MEDIUM|LOW
FEMININO|JOSIMAR LIMA|JOSELI ANDRADE|LOW|MEDIUM
FEMININO|JUCELIA SILVA|CECILIA ALVES|LOW|MEDIUM
FEMININO|MARIA RODRIGUES|MARIANA VENTURA|MEDIUM|LOW
FEMININO|MARIA CORREIA|MARIA AUGUSTO|MEDIUM|LOW
FEMININO|MARIA THIEL|MARIA BARROS|MEDIUM|LOW
FEMININO|MARIA ALBUQUERQUE|MARIA SOUZA|MEDIUM|LOW
FEMININO|SARA FERREIRA|MARIA PEREIRA|LOW|MEDIUM
FEMININO|FRANCILENE UZEDA|FRANCISCA FONSECA|MEDIUM|LOW
FEMININO|ANA PASSOS|ANA ARRUDA|MEDIUM|LOW
FEMININO|MARIA COSTA|MARIANA LIMA|MEDIUM|LOW
FEMININO|IVANIR FERREIRA|CARINE FERREIRA|LOW|MEDIUM
FEMININO|TERESA SILVA|ESTEFANI SILVA|LOW|MEDIUM
FEMININO|MARILIA BRITO|MARIANA DASSI|MEDIUM|LOW
FEMININO|MARIA EVANGELISTA|MARIA FROTA|MEDIUM|LOW
FEMININO|ISABEL FIUZA|ISABELLA SANTOS|MEDIUM|LOW
FEMININO|MARIA MENESES|MARIA GALDINO|MEDIUM|LOW
FEMININO|MARIA CRUZ|MARIA PIMENTEL|MEDIUM|LOW
""";

    [Test]
    public void All53ObservedDivergences_PreserveVersionedCSharpV1States()
    {
        var rows = Observed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|')).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(53));
            Assert.That(rows.Count(x => x[0] == "TODOS"), Is.EqualTo(20));
            Assert.That(rows.Count(x => x[0] == "FEMININO"), Is.EqualTo(33));
            Assert.That(rows.All(x => x.Length == 5), Is.True);
            Assert.That(rows.All(x => x[3] != x[4]), Is.True);
            Assert.That(rows.Count(x => x[3] == "MEDIUM" && x[4] == "HIGH"), Is.EqualTo(1));
        }));
        foreach (var row in rows)
        {
            Assert.That(IdentityComparison.CompareName(row[1], row[2],
                NameComparisonContract.WholeNameJaroWinklerV1).ToString(),
                Is.EqualTo(row[3]), $"{row[0]}: {row[1]} / {row[2]}; Splink 4.0.17={row[4]}");
        }
    }

    [Test]
    public void ExperimentalV3_Explains31ObservedStates_AndLeaves22Unexplained()
    {
        var rows = Observed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|')).ToArray();
        var matchesSplink = 0;
        var changedFromV1 = 0;
        foreach (var row in rows)
        {
            var v1 = IdentityComparison.CompareName(row[1], row[2],
                NameComparisonContract.WholeNameJaroWinklerV1).ToString();
            var v3 = IdentityComparison.CompareName(row[1], row[2],
                NameComparisonContract.WholeNameJaroWinklerPrefixGatedV3).ToString();
            if (v3 == row[4]) matchesSplink++;
            if (v3 != v1) changedFromV1++;
        }

        // Diagnostic only: the historical Splink observations are NOT a general
        // assertion of equivalence and do not authorize operational activation.
        Assert.Multiple((Action)(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(53));
            Assert.That(matchesSplink, Is.EqualTo(31));
            Assert.That(changedFromV1, Is.EqualTo(31));
            Assert.That(rows.Length - matchesSplink, Is.EqualTo(22));
        }));
    }

    [Test]
    public void LegacyAlias_RemainsEquivalentToExplicitV1_ForObservedCases()
    {
        foreach (var line in Observed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var row = line.Split('|');
            Assert.That(IdentityComparison.CompareName(row[1], row[2]),
                Is.EqualTo(IdentityComparison.CompareName(row[1], row[2],
                    NameComparisonContract.WholeNameJaroWinklerV1)), line);
        }
    }
}

using Jornada.Contracts;

namespace Jornada.Tests.Unit;

/// <summary>
/// Ensaio determinístico e estritamente sintético de política conservadora:
/// V2 HIGH/EXACT como proxy de elegibilidade automática e V1 MEDIUM+
/// somente como recuperação para confirmação humana. NÃO é o scorer
/// Fellegi-Sunter, nem testa API, blocking SQL ou confirmação real.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class ConservativeLinkageCounterSyntheticTests
{
    private static readonly string[] Given =
    [
        "MARIA", "MARIANA", "MARINA", "MARTA", "ANA", "ANDREA", "ANTONIA",
        "JOSE", "JOAO", "MARCO", "MARLON", "CARLOS", "CARLA", "JULIANA",
        "LUCIANA", "FRANCISCO", "FRANCISCA", "PAULO", "PEDRO", "RAFAELA",
        "RAFAEL", "FERNANDA", "FERNANDO", "TERESA", "TEREZA", "JOSIANE",
        "ELISA", "SAMUEL", "DAMIAO", "CAMILA", "DANIEL", "DANIELA"
    ];

    private static readonly string[] Family =
    [
        "SILVA", "SANTOS", "OLIVEIRA", "PEREIRA", "COSTA", "SOUZA",
        "FERREIRA", "RODRIGUES", "ALMEIDA", "CARVALHO", "MENDES",
        "LIMA", "RIBEIRO", "BARROS", "MARTINS", "GOMES"
    ];

    private static string Mutate(string value, int index)
    {
        // Perturbações reproduzíveis, sem fonética; algumas são
        // intencionalmente difíceis para um guard por token.
        var tokens = value.Split(' ');
        var token = index % 2;
        var original = tokens[token];
        tokens[token] = (index % 4) switch
        {
            0 => original.Length > 3 ? original[..^1] : original + "A",
            1 => original + original[^1],
            2 => original.Length > 4
                ? original[..2] + original[3] + original[2] + original[4..]
                : original + "S",
            _ => original.Length > 4 ? original[..2] + original[3..] : original + "E"
        };
        return string.Join(' ', tokens);
    }

    private static bool IsAutomatic(string left, string right) =>
        IdentityComparison.CompareName(left, right,
            NameComparisonContract.PtBrContentTokenGuardV2)
            is NameComparisonState.EXACT or NameComparisonState.HIGH;

    private static bool IsCounterCandidate(string left, string right) =>
        IdentityComparison.CompareName(left, right,
            NameComparisonContract.WholeNameJaroWinklerV1)
            is NameComparisonState.EXACT or NameComparisonState.HIGH or NameComparisonState.MEDIUM;

    [Test]
    public void ConservativeV2_AndBroadCounterRetrieval_ReportSyntheticTradeoffs()
    {
        const int population = 4096;
        var names = Enumerable.Range(0, population)
            .Select(i => Given[i % Given.Length] + " " +
                Family[(i / Given.Length) % Family.Length]).ToArray();

        var positives = Enumerable.Range(0, population)
            .Select(i => (Left: names[i], Right: Mutate(names[i], i), Same: true)).ToArray();

        // Negativos difíceis: mesmo prenome ou mesmo sobrenome; a identidade
        // verdadeira é definida pela geração, não inferida pela grafia.
        var negatives = Enumerable.Range(0, population)
            .Select(i =>
            {
                var first = names[i].Split(' ');
                var other = i % 2 == 0
                    ? first[0] + " " + Family[(Array.IndexOf(Family, first[1]) + 1) % Family.Length]
                    : Given[(Array.IndexOf(Given, first[0]) + 1) % Given.Length] + " " + first[1];
                return (Left: names[i], Right: other, Same: false);
            }).ToArray();

        var autoTrue = positives.Count(p => IsAutomatic(p.Left, p.Right));
        var autoFalse = negatives.Count(p => IsAutomatic(p.Left, p.Right));
        var counterRecovered = positives.Count(p =>
            !IsAutomatic(p.Left, p.Right) && IsCounterCandidate(p.Left, p.Right));
        var unresolved = population - autoTrue - counterRecovered;
        var precision = (double)autoTrue / (autoTrue + autoFalse);
        var recall = (double)autoTrue / population;
        var falsePositiveRate = (double)autoFalse / population;

        TestContext.Progress.WriteLine(
            $"SYNTHETIC ONLY; positive={population}; negative={population}; " +
            $"auto_tp={autoTrue}; auto_fp={autoFalse}; " +
            $"counter_recovered_from_auto_fn={counterRecovered}; " +
            $"unresolved_positives={unresolved}; " +
            $"nominal_precision={precision:F6}; nominal_recall={recall:F6}; " +
            $"nominal_fpr={falsePositiveRate:F6}; distinct_base_names={names.Distinct().Count()}");

        Assert.Multiple(() =>
        {
            Assert.That(positives, Has.Length.EqualTo(population));
            Assert.That(negatives, Has.Length.EqualTo(population));
            Assert.That(autoTrue + counterRecovered + unresolved, Is.EqualTo(population));
            Assert.That(autoFalse, Is.LessThan(population));
            Assert.That(names.Distinct().Count(), Is.EqualTo(Given.Length * Family.Length),
                "O corpus repete combinações nominais: não tratar linhas como pessoas independentes.");
            Assert.That(precision, Is.InRange(0d, 1d));
            Assert.That(recall, Is.InRange(0d, 1d));
            Assert.That(counterRecovered, Is.GreaterThan(0),
                "A recuperação para o balcão deve resgatar alguns pares não automáticos.");
        });
    }

    [Test]
    public void CounterCandidates_DoNotAuthorizeAutomaticLinkage()
    {
        const string left = "MARIA SILVA";
        const string right = "MARIA VIOL";
        Assert.That(IsAutomatic(left, right), Is.False);
        Assert.That(IsCounterCandidate(left, right), Is.True);
    }
}

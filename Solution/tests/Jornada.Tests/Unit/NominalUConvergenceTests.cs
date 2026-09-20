using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class NominalUConvergenceTests
{
    [Test]
    public void InsufficientConditionedSupport_UsesIbgeBootstrap()
    {
        var parameters = EmpiricalParameters(namePairs: 800, motherPresentPairs: 600);
        var passes = new[]
        {
            Pass("P001", 500, motherPresent: 400),
            Pass("P002", 300, motherPresent: 200)
        };

        var result = NominalUConvergence.Apply(
            parameters,
            Reference(),
            Bootstrap(seed: 1),
            Bootstrap(seed: 2),
            passes,
            new NominalUConvergenceOptions(2_000, 500));

        Assert.Multiple(() =>
        {
            Assert.That(result["NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"], Is.Zero);
            Assert.That(result["NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED"], Is.Zero);
            Assert.That(result["IBGE_MC_NOMINAL_U_APPLIED_NOME"], Is.EqualTo(1m));
            Assert.That(result["IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE"], Is.EqualTo(1m));
            Assert.That(result["U_NOME_EXACT"], Is.EqualTo(.10m));
            Assert.That(result["U_NOME_MAE_EXACT"], Is.EqualTo(.075m),
                "Missingness de mãe continua empírico: 600/800=0,75 de massa presente × bootstrap EXACT 0,1.");
        });
    }

    [Test]
    public void SufficientUnionAndEveryPass_UsesBlockingConditionedNominalU()
    {
        var parameters = EmpiricalParameters(namePairs: 5_000, motherPresentPairs: 4_500);
        var empiricalNameExact = parameters["U_NOME_EXACT"];
        var empiricalMotherExact = parameters["U_NOME_MAE_EXACT"];
        var passes = new[]
        {
            Pass("P001", 2_500, motherPresent: 2_250),
            Pass("P002", 2_500, motherPresent: 2_250)
        };

        var result = NominalUConvergence.Apply(
            parameters,
            Reference(),
            Bootstrap(seed: 1),
            Bootstrap(seed: 2),
            passes,
            new NominalUConvergenceOptions(4_000, 2_000));

        Assert.Multiple(() =>
        {
            Assert.That(result["NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"], Is.EqualTo(1m));
            Assert.That(result["NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED"], Is.EqualTo(1m));
            Assert.That(result["IBGE_MC_NOMINAL_U_APPLIED_NOME"], Is.Zero);
            Assert.That(result["IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE"], Is.Zero);
            Assert.That(result["U_NOME_EXACT"], Is.EqualTo(empiricalNameExact));
            Assert.That(result["U_NOME_MAE_EXACT"], Is.EqualTo(empiricalMotherExact));
            Assert.That(result["BLOCKING_PASS_U_COUNT"], Is.EqualTo(2m));
            Assert.That(result["BLOCKING_PASS_U_01_SAMPLE_SIZE"], Is.EqualTo(2_500m));
        });
    }

    [Test]
    public void SparsePass_KeepsBootstrapEvenWhenUnionIsLarge()
    {
        var parameters = EmpiricalParameters(namePairs: 10_000, motherPresentPairs: 9_000);
        var passes = new[]
        {
            Pass("P001", 9_900, motherPresent: 8_900),
            Pass("P002", 100, motherPresent: 100)
        };

        var result = NominalUConvergence.Apply(
            parameters,
            Reference(),
            Bootstrap(seed: 1),
            Bootstrap(seed: 2),
            passes,
            new NominalUConvergenceOptions(5_000, 1_000));

        Assert.Multiple(() =>
        {
            Assert.That(result["NOMINAL_U_ALL_PASSES_NAME_SUFFICIENT"], Is.Zero);
            Assert.That(result["NOMINAL_U_ALL_PASSES_MOTHER_SUFFICIENT"], Is.Zero);
            Assert.That(result["NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"], Is.Zero);
            Assert.That(result["NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED"], Is.Zero);
        });
    }

    private static Dictionary<string, decimal> EmpiricalParameters(
        int namePairs,
        int motherPresentPairs)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var nameCounts = Split(namePairs);
        var motherCounts = Split(motherPresentPairs);

        for (var i = 0; i < States.Length; i++)
        {
            var state = States[i];
            result[$"SUPPORT_U_NOME_{state}"] = nameCounts[i];
            result[$"U_NOME_{state}"] = (decimal)nameCounts[i] / namePairs;

            result[$"SUPPORT_U_NOME_MAE_{state}"] = motherCounts[i];
        }

        var motherMissing = Math.Max(0, namePairs - motherPresentPairs);
        var motherTotal = motherPresentPairs + motherMissing;
        result["SUPPORT_U_NOME_MAE_MISSING"] = motherMissing;
        result["U_NOME_MAE_MISSING"] = motherTotal == 0 ? 0m : (decimal)motherMissing / motherTotal;
        for (var i = 0; i < States.Length; i++)
            result[$"U_NOME_MAE_{States[i]}"] = motherTotal == 0 ? 0m : (decimal)motherCounts[i] / motherTotal;

        return result;
    }

    private static BlockingPassNominalUSupport Pass(
        string passId,
        int sampleSize,
        int motherPresent)
    {
        var names = CountsDictionary(Split(sampleSize));
        var mother = CountsDictionary(Split(motherPresent));
        mother["MISSING"] = Math.Max(0, sampleSize - motherPresent);
        return new BlockingPassNominalUSupport(passId, sampleSize, names, mother);
    }

    private static Dictionary<string, long> CountsDictionary(int[] values) =>
        States.Select((state, index) => (state, count: (long)values[index]))
            .ToDictionary(static x => x.state, static x => x.count, StringComparer.Ordinal);

    private static int[] Split(int total)
    {
        var exact = total / 10;
        var high = total / 5;
        var medium = total / 5;
        var low = total - exact - high - medium;
        return [exact, high, medium, low];
    }

    private static IbgeNominalUReferenceInfo Reference() =>
        new(42, "CENSO2022_NOMES_BRASIL_V1", "IBGE", new string('a', 64));

    private static IbgeNominalUBootstrapEstimate Bootstrap(int seed) =>
        new(
            IbgeNominalUBootstrapOptions.MethodVersion,
            IbgeNominalUBootstrapOptions.JointConstructionVersion,
            IbgeNominalUBootstrapOptions.ObservationChannelVersion,
            seed,
            10_000,
            100_000,
            100_000,
            100,
            100,
            .05m,
            .02m,
            .001m,
            [
                new("EXACT", 1_000, .10m, .001m),
                new("HIGH", 2_000, .20m, .001m),
                new("MEDIUM", 2_000, .20m, .001m),
                new("LOW", 5_000, .50m, .001m)
            ]);

    private static readonly string[] States = ["EXACT", "HIGH", "MEDIUM", "LOW"];
}

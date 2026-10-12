using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class FellegiSunterV9JointNominalTests
{
    private static readonly CompositeNominalLevelV9[] Levels =
        Enum.GetValues<CompositeNominalLevelV9>();

    private static IReadOnlyCollection<FellegiSunterJointNominalCellV9> FullFrame(
        CompositeNominalLevelV9 person,
        CompositeNominalLevelV9 mother,
        decimal selectedM,
        decimal selectedU)
    {
        var restM = (1m - selectedM) / 80m;
        var restU = (1m - selectedU) / 80m;
        return Levels.SelectMany(p => Levels.Select(m =>
            new FellegiSunterJointNominalCellV9(
                p, m,
                p == person && m == mother ? selectedM : restM,
                p == person && m == mother ? selectedU : restU))).ToArray();
    }

    private static double Evaluate(
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> cells,
        string? left = "Maria Silva",
        string? right = "MARIA SILVA",
        string? motherLeft = "Ana Costa",
        string? motherRight = "ANA COSTA",
        string? version = null) =>
        FellegiSunterScoring.CalculateCompositeNominalJointV9(
            left, right, motherLeft, motherRight,
            cells, version ?? CompositeNominalComparisonV9.ContractVersion);

    [Test]
    public void JointExactPersonAndMotherApplyOneBayesFactorNotTwoMarginalFactors()
    {
        var c = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            0.20m, 0.05m);
        Assert.That(Evaluate(c), Is.EqualTo(Math.Log(4d)).Within(1e-12));
        // Multiplying the same evidence twice would yield log(16),
        // forbidden by the V9 canonical no-double-counting decision.
        Assert.That(Math.Abs(Evaluate(c) - Math.Log(16d)), Is.GreaterThan(0.1d));
    }

    [Test]
    public void SameMAndUProducesZeroNominalEvidence()
    {
        var c = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            .05m, .05m);
        Assert.That(Evaluate(c), Is.EqualTo(0d).Within(1e-12));
    }

    [Test]
    public void MissingMotherIsASeparateCalibratedJointCellNotAutomaticNeutralEvidence()
    {
        var c = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing,
            .15m, .30m);
        var llr = Evaluate(c, motherLeft: null, motherRight: "Ana Costa");
        Assert.That(llr, Is.EqualTo(-Math.Log(2d)).Within(1e-12));
        Assert.That(llr, Is.LessThan(0d));
    }

    [Test]
    public void ExactFirstLastIsNotMistakenForFullNameExactWithDifferentMiddle()
    {
        var c = FullFrame(
            CompositeNominalLevelV9.ExactFirstLast,
            CompositeNominalLevelV9.ExactFull,
            .45m, .09m);
        var llr = Evaluate(c,
            left: "Maria Clara Silva", right: "Maria Sofia Silva");
        Assert.That(llr, Is.EqualTo(Math.Log(5d)).Within(1e-12));
    }

    [Test]
    public void RejectsIncompleteDuplicateInvalidAndUnnormalizedSnapshots()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            .20m, .05m).ToArray();
        var duplicate = cells.ToArray();
        duplicate[1] = duplicate[0];
        var invalid = cells.ToArray();
        invalid[0] = invalid[0] with { M = 0m };
        var invalidEnum = cells.ToArray();
        invalidEnum[0] = invalidEnum[0] with { Person = (CompositeNominalLevelV9)100 };
        var unnormalized = cells.ToArray();
        unnormalized[0] = unnormalized[0] with { M = .90m };

        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => Evaluate(cells.Skip(1).ToArray())),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Evaluate(duplicate)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Evaluate(invalid)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Evaluate(invalidEnum)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Evaluate(unnormalized)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Evaluate(cells,
                version: "CONTRACT_V8")),
                Throws.InvalidOperationException);
        }));
    }

    private static readonly DateOnly Birth = new(1990, 5, 6);

    private static IReadOnlyDictionary<string, decimal> PreviewParameters() =>
        new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.PriorMatchProbability] = 0.01m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = 0.8m,
            ["U_DATA_NASCIMENTO_EXACT"] = 0.2m,
            ["M_DATA_NASCIMENTO_DIFF"] = 0.2m,
            ["U_DATA_NASCIMENTO_DIFF"] = 0.8m
        };

    [Test]
    public void PreviewCombinesOneJointNominalLlrWithBirthAndPriorInsideSameFsKernel()
    {
        var cells = FullFrame(CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull, 0.20m, 0.05m);
        var result = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            PreviewParameters(),
            "Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA",
            cells, CompositeNominalComparisonV9.ContractVersion,
            leftBirthDate: Birth, rightBirthDate: Birth);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Contributions, Has.Count.EqualTo(2));
            Assert.That(result.Contributions.Count(c => c.Evidence == "NOMINAL_CONJUNTO_V9"),
                Is.EqualTo(1));
            Assert.That(result.Contributions.Any(c => c.Evidence is "NOME" or "NOME_MAE"),
                Is.False);
            Assert.That(result.Contributions.Single(c => c.Evidence == "NOMINAL_CONJUNTO_V9")
                .MProbability, Is.EqualTo(.20m));
            Assert.That(result.Contributions.Single(c => c.Evidence == "NOMINAL_CONJUNTO_V9")
                .UProbability, Is.EqualTo(.05m));
            Assert.That(result.Contributions.Single(c => c.Evidence == "DATA_NASCIMENTO")
                .LogLikelihoodRatio, Is.EqualTo((decimal)Math.Log(4d)).Within(0.000000001m));
            Assert.That(result.Score.LogOdds,
                Is.EqualTo((decimal)(Math.Log(.01d/.99d) + 2*Math.Log(4d)))
                    .Within(0.00000001m));
            Assert.That(result.PriorKind, Is.EqualTo("MODEL_PRIOR"));
            Assert.That(result.PriorProbability, Is.EqualTo(.01m));
        }));
    }

    [Test]
    public void PreviewMissingMotherIsNotSilentlyNeutralizedAndMissingBirthRemainsNeutral()
    {
        var cells = FullFrame(CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing, 0.10m, 0.20m);
        var result = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            PreviewParameters(),
            "Maria Silva", "Maria Silva", null, "Ana Costa",
            cells, CompositeNominalComparisonV9.ContractVersion);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Contributions, Has.Count.EqualTo(2));
            Assert.That(result.Contributions[0].Evidence, Is.EqualTo("NOMINAL_CONJUNTO_V9"));
            Assert.That(result.Contributions[0].State, Does.Contain("Missing"));
            Assert.That(result.Contributions[0].LogLikelihoodRatio,
                Is.EqualTo((decimal)-Math.Log(2d)).Within(0.000000001m));
            Assert.That(result.Contributions[1].Evidence, Is.EqualTo("NASCIMENTO"));
            Assert.That(result.Contributions[1].LogLikelihoodRatio, Is.Zero);
            Assert.That(result.Score.LogOdds,
                Is.EqualTo((decimal)(Math.Log(.01d/.99d)-Math.Log(2d)))
                    .Within(0.00000001m));
        }));
    }

    [Test]
    public void PreviewIsFailClosedAndDoesNotAccidentallyUseHistoricalMarginalParameters()
    {
        var cells = FullFrame(CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull, .2m, .05m);
        var parameters = PreviewParameters();
        var oldOnly = new Dictionary<string, decimal>(parameters)
        {
            ["M_NOME_EXACT"] = 0.01m,
            ["U_NOME_EXACT"] = 0.90m,
            ["M_NOME_MAE_EXACT"] = 0.01m,
            ["U_NOME_MAE_EXACT"] = 0.90m
        };
        var baseline = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            parameters, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
            cells, CompositeNominalComparisonV9.ContractVersion);
        var withMarginals = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            oldOnly, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
            cells, CompositeNominalComparisonV9.ContractVersion);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(baseline.Score, Is.EqualTo(withMarginals.Score));
            Assert.That(baseline.Contributions, Is.EqualTo(withMarginals.Contributions));
            Assert.That((Action)(() =>
                FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                    new Dictionary<string, decimal> { [LinkageParameterCatalog.PriorMatchProbability] = .01m },
                    "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
                    cells, CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                    parameters, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
                    cells.Skip(1).ToArray(), CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void ReorderingCellsDoesNotChangeFrozenJointLikelihoodRatio()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            .40m, .10m).ToArray();
        Assert.That(Evaluate(cells.Reverse().ToArray()),
            Is.EqualTo(Evaluate(cells)).Within(1e-12));
    }
}

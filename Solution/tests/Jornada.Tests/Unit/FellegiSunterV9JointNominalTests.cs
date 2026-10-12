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
        Assert.That(Evaluate(c), Is.Not.EqualTo(Math.Log(16d)).Within(1e-12));
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

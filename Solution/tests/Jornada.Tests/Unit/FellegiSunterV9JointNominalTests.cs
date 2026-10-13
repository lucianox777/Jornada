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
    public void FrozenSnapshotDefensivelyCopiesCallerCellsAndKeepsExactJointLlr()
    {
        var callerOwned = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            .20m, .05m).ToArray();
        var frozen = FrozenFellegiSunterJointNominalV9.Freeze(
            callerOwned, CompositeNominalComparisonV9.ContractVersion);
        var before = frozen.Classify("Maria Silva", "MARIA SILVA",
            "Ana Costa", "ANA COSTA");
        callerOwned[0] = callerOwned[0] with { M = 0m, U = 0m };
        var after = frozen.Classify("Maria Silva", "MARIA SILVA",
            "Ana Costa", "ANA COSTA");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(after, Is.EqualTo(before));
            Assert.That(after.LogLikelihoodRatio, Is.EqualTo(Math.Log(4d)).Within(1e-12));
            Assert.That(after.M, Is.EqualTo(.20m));
            Assert.That(after.U, Is.EqualTo(.05m));
            Assert.That((Action)(() => FrozenFellegiSunterJointNominalV9.Freeze(
                callerOwned, CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void FrozenScorerAndPublicPreviewHaveNumericallyIdenticalBirthAndJointBreakdowns()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull,
            .35m, .07m).ToArray();
        var frozen = FrozenFellegiSunterJointNominalV9.Freeze(
            cells.Reverse().ToArray(), CompositeNominalComparisonV9.ContractVersion);
        var parameters = PreviewParameters();
        var viaPublic = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            parameters, "Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA",
            cells, CompositeNominalComparisonV9.ContractVersion,
            leftBirthDate: Birth, rightBirthDate: Birth);
        var viaFrozen = FellegiSunterScoring.CalculateFrozenCompositeNominalV9PreviewWithBreakdown(
            parameters, "Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA",
            frozen, leftBirthDate: Birth, rightBirthDate: Birth);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(viaFrozen),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(viaPublic)));
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

    private static CompositeNominalTfPreviewV9 FirstNameTf(
        string name, decimal frequency, decimal weight = 1m) =>
        new(
            CompositeNominalTfSideV9.Person,
            new NominalFrequencyReferenceContractV9(
                NominalFrequencyUnitV9.FirstName,
                NominalFrequencySourceV9.IbgePublishedMarginal,
                "IBGE_3550308_NOME",
                "BIRTH_BLOCKING_FRAME",
                0, false,
                new NominalSyntheticIbgeBootstrapV9(
                    CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                    30_000, "CANONICAL_CORPUS", "GENERATOR_V1", 42UL,
                    "HYPOTHESES_V1")),
            name, frequency, .10m, weight, .0001m, 100);

    [Test]
    public void V9SingleUnitTfPreviewMakesRareExactFirstNameMoreInformativeThanCommonName()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFirst,
            CompositeNominalLevelV9.Missing, .20m, .05m);
        FellegiSunterScoreBreakdown Score(string left, string right,
            CompositeNominalTfPreviewV9? tf) =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                PreviewParameters(), left, right, null, null,
                cells, CompositeNominalComparisonV9.ContractVersion,
                nominalTf: tf);

        Assert.That(CompositeNominalComparisonV9.Classify(
            "Maria Lima", "Maria Costa"), Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Zuleica Lima", "Zuleica Costa"), Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        var baseline = Score("Maria Lima", "Maria Costa", null);
        var disabled = Score("Maria Lima", "Maria Costa", FirstNameTf("MARIA", .25m, 0m));
        var common = Score("Maria Lima", "Maria Costa", FirstNameTf("MARIA", .25m));
        var rare = Score("Zuleica Lima", "Zuleica Costa", FirstNameTf("ZULEICA", .005m));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(disabled.Score, Is.EqualTo(baseline.Score));
            Assert.That(common.Score.LogOdds, Is.LessThan(baseline.Score.LogOdds));
            Assert.That(rare.Score.LogOdds, Is.GreaterThan(baseline.Score.LogOdds));
            Assert.That(rare.Score.LogOdds - common.Score.LogOdds,
                Is.EqualTo((decimal)Math.Log(50d)).Within(.0000001m));
            Assert.That(common.Contributions.Count(x => x.Evidence == "NOMINAL_CONJUNTO_V9"),
                Is.EqualTo(1));
            Assert.That(rare.Contributions.Any(x => x.Evidence is "NOME" or "NOME_MAE"),
                Is.False);
            Assert.That(rare.Contributions[0].MProbability, Is.EqualTo(.20m));
            Assert.That(rare.Contributions[0].UProbability, Is.EqualTo(.05m));
        }));
    }

    [Test]
    public void V9TfPreviewRejectsMismatchedMarginalUnitWrongNameAndMissingMother()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull, .20m, .05m);
        var tf = FirstNameTf("MARIA", .25m);
        void Reject(CompositeNominalTfPreviewV9 bad) =>
            Assert.That((Action)(() =>
                FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                    PreviewParameters(), "Maria Silva", "MARIA SILVA",
                    "Ana Costa", "ANA COSTA",
                    cells, CompositeNominalComparisonV9.ContractVersion,
                    nominalTf: bad)), Throws.InvalidOperationException);

        Assert.Multiple((Action)(() =>
        {
            Reject(tf); // IBGE first-name marginal cannot price ExactFull
            Reject(tf with { Side = (CompositeNominalTfSideV9)99 });
            Reject(tf with { Weight = -1m });
            Reject(tf with { Weight = 1.5m });
            Reject(tf with { TermFrequency = 0m });
            Reject(tf with { UnitReferenceU = 0m });
            Reject(tf with { Reference = tf.Reference with
                { Unit = NominalFrequencyUnitV9.FullName } });
        }));

        var exactFirstCells = FullFrame(
            CompositeNominalLevelV9.ExactFirst, CompositeNominalLevelV9.Missing,
            .20m, .05m);
        Assert.That((Action)(() =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                PreviewParameters(), "Maria Lima", "Maria Costa", null, null,
                exactFirstCells, CompositeNominalComparisonV9.ContractVersion,
                nominalTf: tf with { MatchedNormalizedValue = "ZULEICA" })),
            Throws.InvalidOperationException);
        Assert.That((Action)(() =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                PreviewParameters(), "Maria Lima", "Maria Costa", null, null,
                exactFirstCells, CompositeNominalComparisonV9.ContractVersion,
                nominalTf: tf with { Side = CompositeNominalTfSideV9.Mother })),
            Throws.InvalidOperationException);
    }

    [Test]
    public void V9TfPreviewSupportsRealFullNameAndNeutralWeightWithoutMultipleNominalLlr()
    {
        var cells = FullFrame(
            CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing, .20m, .05m);
        var real = FirstNameTf("MARIA", .25m) with
        {
            MatchedNormalizedValue = "MARIA SILVA",
            Reference = new NominalFrequencyReferenceContractV9(
                NominalFrequencyUnitV9.FullName,
                NominalFrequencySourceV9.IndependentlyObservedPopulation,
                "POPULATION", "BLOCKING", 2_000, true)
        };
        var plain = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            PreviewParameters(), "Maria Silva", "MARIA SILVA", null, null,
            cells, CompositeNominalComparisonV9.ContractVersion);
        var withTf = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            PreviewParameters(), "Maria Silva", "MARIA SILVA", null, null,
            cells, CompositeNominalComparisonV9.ContractVersion, nominalTf: real);
        Assert.That(withTf.Score.LogOdds - plain.Score.LogOdds,
            Is.EqualTo((decimal)Math.Log(.10d / .25d)).Within(.0000001m));
        Assert.That(withTf.Contributions.Count(c => c.Evidence == "NOMINAL_CONJUNTO_V9"),
            Is.EqualTo(1));
        Assert.That(withTf.Contributions, Has.Count.EqualTo(2));
        // Declared real support without approval must fail closed.
        Assert.That((Action)(() =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                PreviewParameters(), "Maria Silva", "MARIA SILVA", null, null,
                cells, CompositeNominalComparisonV9.ContractVersion,
                nominalTf: real with { Reference = real.Reference with
                    { IndependentPopulationValidationApproved = false } })),
            Throws.InvalidOperationException);
    }

    [Test]
    public void V9DeclaredBirthEvidenceRequiresCompleteParametersEvenWhenBirthInputIsMissing()
    {
        var cells = FullFrame(CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing, .20m, .05m);
        var valid = new Dictionary<string, decimal>(PreviewParameters());
        FellegiSunterScoreBreakdown Score(
            IReadOnlyDictionary<string, decimal> p,
            DateOnly? left = null, DateOnly? right = null) =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                p, "Maria Silva", "Maria Silva", null, null,
                cells, CompositeNominalComparisonV9.ContractVersion,
                leftBirthDate: left, rightBirthDate: right);
        var original = Score(valid, Birth, Birth);
        var missingM = new Dictionary<string, decimal>(valid);
        missingM.Remove("M_DATA_NASCIMENTO_DIFF");
        var missingU = new Dictionary<string, decimal>(valid);
        missingU.Remove("U_DATA_NASCIMENTO_EXACT");
        var invalidU = new Dictionary<string, decimal>(valid)
            { ["U_DATA_NASCIMENTO_EXACT"] = 0m };
        Assert.Multiple((Action)(() =>
        {
            Assert.That(original.Contributions.Single(x => x.Evidence == "DATA_NASCIMENTO")
                .LogLikelihoodRatio, Is.EqualTo((decimal)Math.Log(4d)).Within(.00000001m));
            // Missing values from a DECLARED birth model must not silently
            // become BF=1 even when this pair has no observed birth date.
            Assert.That((Action)(() => Score(missingM, Birth, Birth)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Score(missingU)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Score(invalidU, Birth, Birth)),
                Throws.InvalidOperationException);
        }));
        // Source data missing is a different situation: calibrated evidence
        // is present in the model, but the observation is missing its value.
        var absent = Score(valid);
        Assert.That(absent.Contributions.Single(x => x.Evidence == "NASCIMENTO")
            .LogLikelihoodRatio, Is.Zero);
    }

    [Test]
    public void V9DeclaredBirthComponentsFailClosedWithoutChangingLegacyV1Neutrality()
    {
        var cells = FullFrame(CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.ExactFull, .20m, .05m);
        var v2 = new Dictionary<string, decimal>(PreviewParameters());
        v2.Remove(LinkageParameterCatalog.BirthSingleEvidenceScoring);
        foreach (var component in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
        foreach (var state in new[] { "EXACT", "DIFF" })
        {
            v2[$"M_{component}_{state}"] = .5m;
            v2[$"U_{component}_{state}"] = .5m;
        }
        v2[LinkageParameterCatalog.BirthComponentScoring] = 1m;
        var valid = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            v2, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
            cells, CompositeNominalComparisonV9.ContractVersion,
            leftBirthDate: Birth, rightBirthDate: Birth);
        Assert.That(valid.Contributions.Count(c => c.Evidence.StartsWith("NASC_", StringComparison.Ordinal)), Is.EqualTo(3));
        v2.Remove("U_NASC_MES_DIFF");
        Assert.That((Action)(() =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                v2, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
                cells, CompositeNominalComparisonV9.ContractVersion,
                leftBirthDate: Birth, rightBirthDate: Birth)),
            Throws.InvalidOperationException);
        // V1 historical replay deliberately omits birth parameters/flags and
        // must retain its existing optional-neutral behavior.
        var historical = new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            ["M_NOME_EXACT"] = .90m, ["U_NOME_EXACT"] = .01m,
            ["M_NOME_MAE_EXACT"] = .90m, ["U_NOME_MAE_EXACT"] = .01m
        };
        var replay = FellegiSunterScoring.CalculateWithBreakdown(
            historical, NameComparisonState.EXACT, NameComparisonState.EXACT,
            leftBirthDate: Birth, rightBirthDate: Birth);
        Assert.That(replay.Contributions.Where(c => c.Evidence.StartsWith("NASC_", StringComparison.Ordinal))
            .All(c => c.LogLikelihoodRatio == 0m), Is.True);
    }
}

using System.Globalization;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class FrozenFellegiSunterV9ContentFingerprintTests
{
    private static FellegiSunterJointNominalCellV9[] Cells() =>
        Enum.GetValues<CompositeNominalLevelV9>()
            .SelectMany(person => Enum.GetValues<CompositeNominalLevelV9>()
                .Select(mother => new FellegiSunterJointNominalCellV9(
                    person, mother,
                    person == CompositeNominalLevelV9.ExactFull
                        && mother == CompositeNominalLevelV9.ExactFull ? .20m : .01m,
                    person == CompositeNominalLevelV9.ExactFull
                        && mother == CompositeNominalLevelV9.ExactFull ? .05m : .95m / 80m)))
            .ToArray();

    private static FrozenFellegiSunterJointNominalV9 Freeze(
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> cells) =>
        FrozenFellegiSunterJointNominalV9.Freeze(
            cells, CompositeNominalComparisonV9.ContractVersion);

    [Test]
    public void IdenticalDistributionsHaveTheSameContentIdDespiteOrderAndDecimalScale()
    {
        var cells = Cells();
        var original = Freeze(cells).ContentFingerprint;
        var reversed = Freeze(cells.Reverse().ToArray()).ContentFingerprint;
        var alteredScale = cells.ToArray();
        var exact = Array.FindIndex(alteredScale, c =>
            c.Person == CompositeNominalLevelV9.ExactFull
            && c.Mother == CompositeNominalLevelV9.ExactFull);
        alteredScale[exact] = alteredScale[exact] with { M = .2000m, U = .05000m };

        Assert.Multiple((Action)(() =>
        {
            Assert.That(original, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(reversed, Is.EqualTo(original));
            Assert.That(Freeze(alteredScale).ContentFingerprint, Is.EqualTo(original));
        }));
    }

    [Test]
    public void ChangingAProbabilityWhileKeepingNormalizationChangesTheContentId()
    {
        var cells = Cells();
        var baseline = Freeze(cells).ContentFingerprint;
        var altered = cells.ToArray();
        altered[1] = altered[1] with { M = altered[1].M + .001m };
        altered[2] = altered[2] with { M = altered[2].M - .001m };

        Assert.That(Freeze(altered).ContentFingerprint, Is.Not.EqualTo(baseline));
    }

    [Test]
    public void FingerprintIsIndependentOfProcessCulture()
    {
        var cells = Cells();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var pt = Freeze(cells).ContentFingerprint;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.That(Freeze(cells).ContentFingerprint, Is.EqualTo(pt));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void CallerMutationCannotChangeFrozenMathematicalInputOrContentId()
    {
        var cells = Cells();
        var frozen = Freeze(cells);
        var before = frozen.Classify(
            "Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA");
        var fingerprint = frozen.ContentFingerprint;
        cells[0] = cells[0] with { M = 0m };

        Assert.Multiple((Action)(() =>
        {
            Assert.That(frozen.ContentFingerprint, Is.EqualTo(fingerprint));
            Assert.That(frozen.Classify(
                "Maria Silva", "MARIA SILVA", "Ana Costa", "ANA COSTA"), Is.EqualTo(before));
            Assert.That((Action)(() => Freeze(cells)), Throws.InvalidOperationException);
        }));
    }
}

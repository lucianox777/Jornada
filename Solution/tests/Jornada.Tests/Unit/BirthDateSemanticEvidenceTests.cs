using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BirthDateSemanticEvidenceTests
{
    [Test]
    public void Exact_dates_are_exact()
    {
        var date = new DateOnly(1975, 6, 15);
        Assert.That(BirthDateSemanticEvidence.Classify(date, date), Is.EqualTo(BirthDateSemanticEvidence.Exact));
    }

    [Test]
    public void Day_month_swap_is_recognized_symmetrically()
    {
        var left = new DateOnly(1975, 6, 7);
        var right = new DateOnly(1975, 7, 6);

        Assert.Multiple(() =>
        {
            Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.DayMonthSwap));
            Assert.That(BirthDateSemanticEvidence.Classify(right, left), Is.EqualTo(BirthDateSemanticEvidence.DayMonthSwap));
        });
    }

    [Test]
    public void Century_shift_has_precedence_over_digit_distance()
    {
        var left = new DateOnly(1975, 6, 15);
        var right = new DateOnly(2075, 6, 15);

        Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.CenturyShift));
    }

    [Test]
    public void Same_day_month_but_unrelated_year_is_partial_not_century_shift()
    {
        var left = new DateOnly(1975, 6, 15);
        var right = new DateOnly(1932, 6, 15);

        Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.PartialComponentAgreement));
    }

    [Test]
    public void One_digit_substitution_is_recognized_in_ddMMyyyy_representation()
    {
        var left = new DateOnly(1975, 6, 15);
        var right = new DateOnly(1975, 7, 15);

        Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.OneDigitError));
    }

    [Test]
    public void Two_digit_substitution_is_recognized_in_ddMMyyyy_representation()
    {
        var left = new DateOnly(1975, 6, 15);
        var right = new DateOnly(1975, 8, 25);

        Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.TwoDigitError));
    }

    [Test]
    public void Unstructured_difference_is_other_disagreement()
    {
        var left = new DateOnly(1975, 6, 15);
        var right = new DateOnly(1983, 9, 24);

        Assert.That(BirthDateSemanticEvidence.Classify(left, right), Is.EqualTo(BirthDateSemanticEvidence.OtherDisagreement));
    }
}

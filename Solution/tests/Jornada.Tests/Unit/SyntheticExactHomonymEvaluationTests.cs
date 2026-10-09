using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticExactHomonymEvaluationTests
{
    [Test]
    public void Reserved_exact_homonyms_are_measured_separately_from_population()
    {
        var report = SyntheticExactHomonymEvaluator.Evaluate(
        [
            Case("population", "TEST", "CPF_ABSENT", null, samePerson: true, linked: true),
            Case("h1", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym, linked: true),
            Case("h2", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym, linked: false),
            Case("h3", "VALIDATION", "CPF_PRESENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym, linked: true),
            Case("not-exact", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym,
                linked: true, motherExact: false)
        ]);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.PopulationPairs, Is.EqualTo(1));
            Assert.That(report.ReservedChallengePairs, Is.EqualTo(4));
            Assert.That(report.ExactDistinctHomonymPairs, Is.EqualTo(3));
            Assert.That(report.ExactDistinctHomonymFalseLinks, Is.EqualTo(2));
            Assert.That(report.ExactDistinctHomonymFalseLinkRate, Is.EqualTo(2m / 3m));
            Assert.That(report.ByStratum, Has.Count.EqualTo(2));
        }));
        Assert.DoesNotThrow(() => SyntheticExactHomonymEvaluator.ConferArithmetic(report));
    }

    [Test]
    public void Same_person_exact_pair_is_not_a_false_link_opportunity()
    {
        var report = SyntheticExactHomonymEvaluator.Evaluate(
        [
            Case("same", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym,
                samePerson: true, linked: true)
        ]);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.ExactDistinctHomonymPairs, Is.Zero);
            Assert.That(report.ExactDistinctHomonymFalseLinks, Is.Zero);
            Assert.That(report.ExactDistinctHomonymFalseLinkRate, Is.Zero);
        }));
    }

    [Test]
    public void Arithmetic_conference_fails_closed_on_tampered_rate()
    {
        var report = SyntheticExactHomonymEvaluator.Evaluate(
        [
            Case("h1", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym, linked: true),
            Case("h2", "TEST", "CPF_ABSENT", SyntheticReservedTruthFamilies.ExactDemographicHomonym, linked: false)
        ]);

        var tampered = report with { ExactDistinctHomonymFalseLinkRate = 0m };
        Assert.Throws<InvalidDataException>((Action)(() =>
            SyntheticExactHomonymEvaluator.ConferArithmetic(tampered)));
    }

    [Test]
    public void Duplicate_pair_id_is_rejected()
    {
        Assert.Throws<InvalidDataException>((Action)(() => SyntheticExactHomonymEvaluator.Evaluate(
        [
            Case("dup", "TEST", "A", SyntheticReservedTruthFamilies.ExactDemographicHomonym),
            Case("dup", "TEST", "B", SyntheticReservedTruthFamilies.ExactDemographicHomonym)
        ])));
    }

    private static SyntheticTruthDecisionCase Case(
        string pairId,
        string partition,
        string stratum,
        string? family,
        bool samePerson = false,
        bool linked = false,
        bool nameExact = true,
        bool motherExact = true,
        bool birthExact = true) =>
        new(
            pairId,
            partition,
            stratum,
            family,
            samePerson,
            nameExact,
            motherExact,
            birthExact,
            linked);
}

using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticStratifiedEvaluationTests
{
    [Test]
    public void Population_is_split_by_partition_wave_and_required_strata()
    {
        var cases = new[]
        {
            Case("a", "TRAIN", 1, hasCpf: false, mother: false, homonym: false,
                truth: true, retrieved: true, SyntheticEvaluationDecision.Linked, correct: true),
            Case("b", "TRAIN", 1, hasCpf: false, mother: false, homonym: false,
                truth: true, retrieved: false, SyntheticEvaluationDecision.Abstained),
            Case("c", "VALIDATION", 2, hasCpf: true, mother: true, homonym: true,
                truth: false, retrieved: true, SyntheticEvaluationDecision.Linked),
            Case("d", "TEST", 3, hasCpf: false, mother: true, homonym: true,
                truth: true, retrieved: true, SyntheticEvaluationDecision.Conflict)
        };

        var report = SyntheticStratifiedEvaluator.Evaluate(cases);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.PopulationCases, Is.EqualTo(4));
            Assert.That(report.ReservedChallengeCases, Is.Zero);
            Assert.That(report.PopulationByStratum, Has.Count.EqualTo(3));
        }));

        var train = report.PopulationByStratum.Single(x => x.Partition == "TRAIN");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(train.Wave, Is.EqualTo(1));
            Assert.That(train.CpfStratum, Is.EqualTo("CPF_ABSENT"));
            Assert.That(train.MotherStratum, Is.EqualTo("MOTHER_ABSENT"));
            Assert.That(train.Counts.TruthPositive, Is.EqualTo(2));
            Assert.That(train.Counts.CandidateRetrievedPositive, Is.EqualTo(1));
            Assert.That(train.Counts.TruePositive, Is.EqualTo(1));
            Assert.That(train.Counts.FalseNegative, Is.EqualTo(1));
            Assert.That(train.Counts.Abstentions, Is.EqualTo(1));
            Assert.That(train.Rates.BlockingRecall, Is.EqualTo(0.5m));
            Assert.That(train.Rates.PrecisionPpv, Is.EqualTo(1m));
            Assert.That(train.Rates.DecisionRecall, Is.EqualTo(0.5m));
            Assert.That(train.Rates.Coverage, Is.EqualTo(0.5m));
        }));
        Assert.DoesNotThrow((Action)(() => SyntheticStratifiedMetricConference.Confer(cases, report)));
    }

    [Test]
    public void Reserved_challenge_families_are_disjoint_from_population()
    {
        var cases = new[]
        {
            Case("population", "TEST", 3, hasCpf: false, mother: true, homonym: false,
                truth: true, retrieved: true, SyntheticEvaluationDecision.Linked, correct: true),
            Case("missing-mother", "CHALLENGE", 3, hasCpf: false, mother: false, homonym: false,
                truth: true, retrieved: true, SyntheticEvaluationDecision.Linked, correct: true,
                family: "CHALLENGE_MISSING_MOTHER_V1"),
            Case("late-cpf", "CHALLENGE", 3, hasCpf: true, mother: true, homonym: false,
                truth: true, retrieved: false, SyntheticEvaluationDecision.Abstained,
                family: "CHALLENGE_LATE_CPF_V1"),
            Case("correlated", "CHALLENGE", 3, hasCpf: false, mother: true, homonym: false,
                truth: false, retrieved: true, SyntheticEvaluationDecision.Linked,
                family: "CHALLENGE_CORRELATED_ERROR_V1"),
            Case("leave-out", "CHALLENGE", 3, hasCpf: false, mother: true, homonym: true,
                truth: false, retrieved: false, SyntheticEvaluationDecision.Abstained,
                family: "CHALLENGE_LEAVE_TRUTH_OUT_V1")
        };

        var report = SyntheticStratifiedEvaluator.Evaluate(cases);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.PopulationCases, Is.EqualTo(1));
            Assert.That(report.ReservedChallengeCases, Is.EqualTo(4));
            Assert.That(report.PopulationByStratum.Sum(x => x.Counts.Cases), Is.EqualTo(1));
            Assert.That(report.ChallengeByStratum.Sum(x => x.Counts.Cases), Is.EqualTo(4));
        }));
        Assert.DoesNotThrow((Action)(() => SyntheticStratifiedMetricConference.Confer(cases, report)));
    }

    [TestCase("TRAIN", "CHALLENGE_MISSING_MOTHER_V1")]
    [TestCase("CHALLENGE", null)]
    [TestCase("DEV", null)]
    public void Population_and_challenge_partition_contract_fails_closed(string partition, string? family)
    {
        var item = Case("x", partition, 1, hasCpf: false, mother: false, homonym: false,
            truth: true, retrieved: false, SyntheticEvaluationDecision.Abstained, family: family);
        Assert.Throws<InvalidDataException>((Action)(() => SyntheticStratifiedEvaluator.Evaluate([item])));
    }

    [Test]
    public void Unknown_reserved_family_fails_closed()
    {
        var item = Case("x", "CHALLENGE", 1, hasCpf: false, mother: false, homonym: false,
            truth: true, retrieved: false, SyntheticEvaluationDecision.Abstained,
            family: "CHALLENGE_UNVERSIONED");
        Assert.Throws<InvalidDataException>((Action)(() => SyntheticStratifiedEvaluator.Evaluate([item])));
    }

    [Test]
    public void Duplicate_case_and_impossible_link_fail_closed()
    {
        var item = Case("dup", "TRAIN", 1, hasCpf: false, mother: true, homonym: false,
            truth: true, retrieved: true, SyntheticEvaluationDecision.Abstained);
        Assert.Throws<InvalidDataException>((Action)(() => SyntheticStratifiedEvaluator.Evaluate([item, item])));

        var impossible = Case("impossible", "TEST", 1, hasCpf: false, mother: true, homonym: false,
            truth: true, retrieved: false, SyntheticEvaluationDecision.Linked, correct: true);
        Assert.Throws<InvalidDataException>((Action)(() => SyntheticStratifiedEvaluator.Evaluate([impossible])));
    }

    [Test]
    public void Independent_conference_rejects_tampered_counts_and_rates()
    {
        var cases = new[]
        {
            Case("a", "TEST", 2, hasCpf: false, mother: false, homonym: true,
                truth: true, retrieved: true, SyntheticEvaluationDecision.Linked, correct: true),
            Case("b", "TEST", 2, hasCpf: false, mother: false, homonym: true,
                truth: false, retrieved: true, SyntheticEvaluationDecision.Linked)
        };
        var report = SyntheticStratifiedEvaluator.Evaluate(cases);
        var slice = report.PopulationByStratum.Single();

        var badCounts = slice with
        {
            Counts = slice.Counts with { FalsePositive = slice.Counts.FalsePositive + 1 }
        };
        var tamperedCounts = report with { PopulationByStratum = [badCounts] };
        Assert.Throws<InvalidDataException>((Action)(() =>
            SyntheticStratifiedMetricConference.Confer(cases, tamperedCounts)));

        var badRates = slice with
        {
            Rates = slice.Rates with { PrecisionPpv = 1m }
        };
        var tamperedRates = report with { PopulationByStratum = [badRates] };
        Assert.Throws<InvalidDataException>((Action)(() =>
            SyntheticStratifiedMetricConference.Confer(cases, tamperedRates)));
    }

    [Test]
    public void Empty_denominators_are_explicitly_null_not_invented_zero()
    {
        var cases = new[]
        {
            Case("negative", "VALIDATION", 1, hasCpf: true, mother: true, homonym: false,
                truth: false, retrieved: false, SyntheticEvaluationDecision.Abstained)
        };
        var report = SyntheticStratifiedEvaluator.Evaluate(cases);
        var slice = report.PopulationByStratum.Single();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(slice.Rates.BlockingRecall, Is.Null);
            Assert.That(slice.Rates.PrecisionPpv, Is.Null);
            Assert.That(slice.Rates.DecisionRecall, Is.Null);
            Assert.That(slice.Rates.Coverage, Is.Zero);
        }));
        Assert.DoesNotThrow((Action)(() => SyntheticStratifiedMetricConference.Confer(cases, report)));
    }

    private static SyntheticEvaluationTruthCase Case(
        string id,
        string partition,
        int wave,
        bool hasCpf,
        bool mother,
        bool homonym,
        bool truth,
        bool retrieved,
        SyntheticEvaluationDecision decision,
        bool correct = false,
        string? family = null) =>
        new(id, partition, family, wave, hasCpf, mother, homonym,
            truth, retrieved, decision, correct);
}

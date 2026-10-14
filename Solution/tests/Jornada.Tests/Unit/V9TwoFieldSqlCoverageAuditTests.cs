using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9TwoFieldSqlCoverageAuditTests
{
    private static readonly V9TwoFieldSqlCoverageAudit.Observation[] Observations =
    [
        new(0, "A", "TRAIN", "G1", "MARIA", "ANA", null, null),
        new(1, "A", "TRAIN", "G2", "MARIA", "ANA", new DateOnly(1980, 2, 1), null),
        new(2, "B", "TRAIN", "G1", null, "ANA", new DateOnly(1980, 2, 1), null),
        new(3, "B", "TRAIN", "G2", null, "ANA", new DateOnly(1980, 2, 1), null),
        new(4, "C", "TRAIN", "G1", "NÃO INFORMADO", null, null, null),
        new(5, "C", "TRAIN", "G2", "CARLOS", "ANA", new DateOnly(1970, 1, 1), null),
        new(6, "D", "TRAIN", "G3", "CARLOS", null, new DateOnly(1970, 1, 1), null),
        new(7, "E", "TRAIN", "G4", "CARLOS", null, new DateOnly(1970, 1, 1), null),
        new(8, "F", "TRAIN", "G1", "CARLOS", null, new DateOnly(1970, 1, 1), null),
        new(9, "F", "TRAIN", "G2", null, null, new DateOnly(1970, 1, 1), null),
        new(10, "H", "TRAIN", "G9", "MARIA", "ANA", null, "CPF_DETERMINISTICO"),
        new(20, "A", "VALIDATION", "G1", "MARIA", "ANA", new DateOnly(1980, 2, 1), null),
        new(21, "A", "VALIDATION", "G2", "MARIA", "ANA", new DateOnly(1980, 2, 1), null),
        new(22, "B", "VALIDATION", "G1", "PAULO", null, null, null),
        new(23, "B", "VALIDATION", "G2", "PAULO", "ANA", new DateOnly(1980, 2, 1), null)
    ];

    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] Sql =
    [
        new(2, 3, "TRAIN", true),
        new(6, 7, "TRAIN", false),
        new(8, 9, "TRAIN", true),
        new(20, 21, "VALIDATION", true)
    ];

    [Test]
    public void Enumerates_all_true_pairs_before_sql_and_reports_both_recall_denominators()
    {
        var report = V9TwoFieldSqlCoverageAudit.Analyze(
            Observations, [..Sql, Sql[1]], 100);
        var train = report.Splits.Single(x => x.Partition == "TRAIN");
        var validation = report.Splits.Single(x => x.Partition == "VALIDATION");

        Assert.That(report.MayPromoteModelOrBlockingPolicy, Is.False);
        Assert.That(report.FdrMeasured, Is.False);
        Assert.That(report.AvailabilityByCombination, Has.Count.EqualTo(16));
        Assert.That(train.AllNoCpfTruthM, Is.EqualTo(4));
        Assert.That(train.EligibleTwoFieldTruthM, Is.EqualTo(2));
        Assert.That(train.IneligibleTruthM, Is.EqualTo(2));
        Assert.That(train.EligibleTruthMWithMissingBirth, Is.EqualTo(1));
        Assert.That(train.LegacySqlM, Is.EqualTo(2));
        Assert.That(train.LegacySqlU, Is.EqualTo(1));
        Assert.That(train.LegacySqlEligibleM, Is.EqualTo(1));
        Assert.That(train.LegacySqlEligibleU, Is.EqualTo(1));
        Assert.That(train.LegacySqlIneligibleM, Is.EqualTo(1));
        Assert.That(train.UnrecoveredEligibleM, Is.EqualTo(1));
        Assert.That(train.RecallAgainstAllNoCpfTruthM, Is.EqualTo(0.25m));
        Assert.That(train.RecallAgainstTwoFieldEligibleM, Is.EqualTo(0.5m));
        Assert.That(train.TwoFieldEligibilityCeilingAgainstAllTruthM, Is.EqualTo(0.5m));
        Assert.That(train.AdditionalEligibleMNeededFor95, Is.EqualTo(1));
        Assert.That(validation.AllNoCpfTruthM, Is.EqualTo(2));
        Assert.That(validation.EligibleTwoFieldTruthM, Is.EqualTo(1));
        Assert.That(validation.LegacySqlEligibleM, Is.EqualTo(1));
        Assert.That(validation.RecallAgainstTwoFieldEligibleM, Is.EqualTo(1m));

        var name = report.NameRequiredSplits.Single(x => x.Partition == "TRAIN");
        Assert.That(report.NameRequiredPolicyVersion,
            Is.EqualTo(V9MinimumEvidenceEligibility.PersonRequiredVersion));
        Assert.That(name.EligibleTruthM, Is.EqualTo(1));
        Assert.That(name.ExcludedTruthM, Is.EqualTo(3));
        Assert.That(name.EligibleTruthMWithoutSqlBirth, Is.EqualTo(1));
        Assert.That(name.SqlEligibleM, Is.EqualTo(0));
        Assert.That(name.SqlEligibleU, Is.EqualTo(1));
        Assert.That(name.SqlExcludedM, Is.EqualTo(2));
        Assert.That(name.UnrecoveredEligibleM, Is.EqualTo(1));
        Assert.That(name.RecallAgainstAllNoCpfTruthM, Is.EqualTo(0m));
        Assert.That(name.RecallAgainstNameEligibleTruthM, Is.EqualTo(0m));
        Assert.That(name.EligibilityCeilingAgainstAllTruthM, Is.EqualTo(0.25m));
        Assert.That(name.MoreMNeededFor95, Is.EqualTo(1));
        var nameValidation=report.NameRequiredSplits.Single(
            x => x.Partition == "VALIDATION");
        Assert.That(nameValidation.EligibleTruthM, Is.EqualTo(1));
        Assert.That(nameValidation.SqlEligibleM, Is.EqualTo(1));
        Assert.That(nameValidation.RecallAgainstNameEligibleTruthM, Is.EqualTo(1m));

        var pm = report.AvailabilityByCombination.Single(x =>
            x.Partition == "TRAIN" && x.Combination == "PM");
        var none = report.AvailabilityByCombination.Single(x =>
            x.Partition == "TRAIN" && x.Combination == "NONE");
        Assert.That(pm.Observations, Is.EqualTo(1));
        Assert.That(pm.ProbabilisticAbstention, Is.False);
        Assert.That(none.Observations, Is.EqualTo(1));
        Assert.That(none.ProbabilisticAbstention, Is.True);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(report),
            Does.Not.Contain("MARIA"));
    }

    [Test]
    public void Rejects_test_cross_partition_same_gestor_wrong_labels_and_caps()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(
                [..Observations,
                    new(30, "C", "TEST", "G1", "ANA", "MARIA", null, null)],
                Sql, 100)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(Observations,
                [new(0, 20, "TRAIN", false)], 100)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(Observations,
                [new(2, 3, "TRAIN", false)], 100)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(Observations,
                [new(2, 3, "TRAIN", true), new(2, 3, "TRAIN", false)], 100)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(Observations, Sql, 2)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TwoFieldSqlCoverageAudit.Analyze(Observations,
                [new(0, 1, "TRAIN", true)], 100)));
    }
    [Test]
    public void Sql_may_physically_retrieve_a_pair_with_sentinel_DOB_if_names_are_valid()
    {
        var withSentinel = new V9TwoFieldSqlCoverageAudit.Observation[]
        {
            ..Observations,
            new(40, "S", "TRAIN", "G1", "MARIA", "ANA", new DateOnly(1900,1,1), null),
            new(41, "S", "TRAIN", "G2", "MARIA", "ANA", new DateOnly(1900,1,1), null)
        };
        var report = V9TwoFieldSqlCoverageAudit.Analyze(
            withSentinel,
            [..Sql, new(40, 41, "TRAIN", true)], 100);
        var train = report.Splits.Single(x => x.Partition == "TRAIN");
        var name = report.NameRequiredSplits.Single(x => x.Partition == "TRAIN");
        Assert.That(train.LegacySqlEligibleM, Is.EqualTo(2));
        Assert.That(train.EligibleTruthMWithMissingBirth, Is.EqualTo(1));
        Assert.That(name.EligibleTruthM, Is.EqualTo(2));
        Assert.That(name.SqlEligibleM, Is.EqualTo(1));
        Assert.That(name.EligibleTruthMWithoutSqlBirth, Is.EqualTo(1));
    }

}

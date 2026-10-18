using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SyntheticBaseFrequencyAuditTests
{
    private static V9SyntheticBaseFrequencyAudit.BasePerson[] Population() =>
    [
        new("T1", "TRAIN", "Maria Aparecida Silva", "Ana Souza"),
        new("T2", "TRAIN", "Maria Cecilia Silva", "Ana Souza"),
        new("T3", "TRAIN", "Maria Silva", null),
        new("T4", "TRAIN", "Joana Costa", "Ana Souza"),
        new("T5", "TRAIN", "Maria Silva", "Eva Maria"),
        new("V1", "VALIDATION", "Maria Silva", "Ana Souza"),
        new("E1", "TEST", "Maria Silva", "Ana Souza")
    ];

    [Test]
    public void Frequency_uses_one_true_person_per_train_base_and_keeps_full_and_joint_distinct()
    {
        var report = V9SyntheticBaseFrequencyAudit.Analyze(Population(), "FRAME_42");
        var person = report.Units.Where(x => x.Side == "PERSON").ToArray();
        var full = person.Single(x => x.Unit == NominalFrequencyUnitV9.FullName);
        var firstLast = person.Single(x => x.Unit == NominalFrequencyUnitV9.FirstLastJoint);
        var first = person.Single(x => x.Unit == NominalFrequencyUnitV9.FirstName);
        var mother = report.Units.Single(x => x.Side == "MOTHER" &&
            x.Unit == NominalFrequencyUnitV9.FirstName);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Status, Is.EqualTo(
                V9SyntheticBaseFrequencyAudit.Status));
            Assert.That(report.Partition, Is.EqualTo("TRAIN"));
            Assert.That(report.CorpusBasePeople, Is.EqualTo(7));
            Assert.That(report.TrainingBasePeople, Is.EqualTo(5));
            Assert.That(report.Units.Count, Is.EqualTo(8));
            Assert.That(full.DistinctValues, Is.EqualTo(4));
            Assert.That(full.LargestValueCount, Is.EqualTo(2));
            Assert.That(firstLast.DistinctValues, Is.EqualTo(2));
            Assert.That(firstLast.LargestValueCount, Is.EqualTo(4));
            Assert.That(first.DistinctValues, Is.EqualTo(2));
            Assert.That(first.LargestValueCount, Is.EqualTo(4));
            Assert.That(mother.MissingTrainingBasePeople, Is.EqualTo(1));
            Assert.That(mother.PresentTrainingBasePeople, Is.EqualTo(4));
            Assert.That(mother.LargestValueCount, Is.EqualTo(3));
            Assert.That(first.ValuesObservedAtLeastFiveBases, Is.Zero);
            Assert.That(first.BasesCoveredByValuesObservedAtLeastFive, Is.Zero);
        }));
    }

    [Test]
    public void Validation_and_test_names_never_change_train_frequency()
    {
        var baseline = V9SyntheticBaseFrequencyAudit.Analyze(Population(), "FRAME_42");
        var withOtherEvaluation = Population();
        withOtherEvaluation[5] = new("V1", "VALIDATION", "Joana Costa", null);
        withOtherEvaluation[6] = new("E1", "TEST", "Anacleto Nogueira", null);
        var changed = V9SyntheticBaseFrequencyAudit.Analyze(withOtherEvaluation, "FRAME_42");
        Assert.That(changed.Units, Is.EqualTo(baseline.Units));
    }

    private static V9SyntheticBaseFrequencyAudit.BasePerson[] Full30kCorpus() =>
        Enumerable.Range(0, 30_000).Select(i => new V9SyntheticBaseFrequencyAudit.BasePerson(
            $"BASE-{i:00000}", i < 20_000 ? "TRAIN" : i < 25_000 ? "VALIDATION" : "TEST",
            i == 0 ? "Maria Aparecida Silva"
            : i == 1 ? "Maria Cecilia Silva"
            : i == 2 ? "Maria Silva" : "Joana Costa",
            i % 7 == 0 ? null : "Ana Souza")).ToArray();

    private static NominalSyntheticIbgeBootstrapV9 FullBootstrap() => new(
        CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
        30_000, "CORPUS_30K_PINNED", "GENERATOR_V1", 42UL, "HYPOTHESES_SHA");

    [Test]
    public void Canonical_30k_materialization_counts_one_train_base_per_exact_unit()
    {
        var people = Full30kCorpus();
        V9SyntheticBaseFrequencyAudit.MaterializedTrainUnitSnapshot Snapshot(
            NominalFrequencyUnitV9 unit, int min = 1) =>
            V9SyntheticBaseFrequencyAudit.MaterializeCanonicalTrainUnit(
                people, "CORPUS_30K_PINNED", FullBootstrap(),
                "PERSON", unit, min);

        var full = Snapshot(NominalFrequencyUnitV9.FullName);
        var firstLast = Snapshot(NominalFrequencyUnitV9.FirstLastJoint);
        var first = Snapshot(NominalFrequencyUnitV9.FirstName);
        var last = Snapshot(NominalFrequencyUnitV9.LastContentSurname);
        var fullMaria = full.Frequencies.Single(x => x.NormalizedValue == "MARIA APARECIDA SILVA");
        var jointMaria = firstLast.Frequencies.Single(x => x.NormalizedValue == "MARIA|SILVA");
        var firstMaria = first.Frequencies.Single(x => x.NormalizedValue == "MARIA");
        var lastSilva = last.Frequencies.Single(x => x.NormalizedValue == "SILVA");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(full.CorpusBaseIdentities, Is.EqualTo(30_000));
            Assert.That(full.TrainBaseIdentities, Is.EqualTo(20_000));
            Assert.That(full.PresentTrainBaseIdentities, Is.EqualTo(20_000));
            Assert.That(full.MissingTrainBaseIdentities, Is.Zero);
            Assert.That(fullMaria.BaseIdentities, Is.EqualTo(1));
            Assert.That(jointMaria.BaseIdentities, Is.EqualTo(3));
            Assert.That(firstMaria.BaseIdentities, Is.EqualTo(3));
            Assert.That(lastSilva.BaseIdentities, Is.EqualTo(3));
            Assert.That(firstMaria.FrequencyAmongPresent, Is.EqualTo(3m / 20_000m));
            Assert.That(fullMaria.FrequencyAmongPresent, Is.EqualTo(1m / 20_000m));
            Assert.That(full.SnapshotFingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(full.Status, Is.EqualTo(
                V9SyntheticBaseFrequencyAudit.MaterializedStatus));
        }));
        var supported = Snapshot(NominalFrequencyUnitV9.FirstName, min: 5);
        Assert.That(supported.Frequencies.Any(x => x.NormalizedValue == "MARIA"), Is.False);
        Assert.That(supported.Frequencies.Single().NormalizedValue, Is.EqualTo("JOANA"));
        Assert.That(supported.Frequencies.Single().FrequencyAmongPresent,
            Is.EqualTo(19_997m / 20_000m));
    }

    [Test]
    public void Nominal_snapshot_sha_binds_train_identities_not_only_term_histogram()
    {
        var people = Full30kCorpus();
        V9SyntheticBaseFrequencyAudit.MaterializedTrainUnitSnapshot Snapshot() =>
            V9SyntheticBaseFrequencyAudit.MaterializeCanonicalTrainUnit(
                people, "CORPUS_30K_PINNED", FullBootstrap(),
                "PERSON", NominalFrequencyUnitV9.FullName);

        var before = Snapshot();
        // VALIDATION/TEST cannot contribute to a TRAIN-only marginal.
        people[20_000] = people[20_000] with { Name = "CHANGED VAL" };
        people[25_000] = people[25_000] with { Name = "CHANGED TEST" };
        Assert.That(Snapshot().SnapshotFingerprintSha256,
            Is.EqualTo(before.SnapshotFingerprintSha256));

        // Swapping two TRAIN identities keeps histogram/term frequencies
        // identical, but the source-person assignment must change the SHA.
        (people[0], people[3]) = (
            people[0] with { Name = people[3].Name },
            people[3] with { Name = people[0].Name });
        var after = Snapshot();
        Assert.That(after.Frequencies, Is.EqualTo(before.Frequencies));
        Assert.That(after.SnapshotFingerprintSha256,
            Is.Not.EqualTo(before.SnapshotFingerprintSha256));
        var reordered = people.Reverse().ToArray();
        var replay = V9SyntheticBaseFrequencyAudit.MaterializeCanonicalTrainUnit(
            reordered, "CORPUS_30K_PINNED", FullBootstrap(),
            "PERSON", NominalFrequencyUnitV9.FullName);
        Assert.That(replay.SnapshotFingerprintSha256,
            Is.EqualTo(after.SnapshotFingerprintSha256));
    }

    [Test]
    public void Physical_sql_evidence_exports_eight_reconciled_hashes_without_pii_or_test_labels()
    {
        var people = Full30kCorpus();
        var audit = V9SyntheticBaseFrequencyAudit.Analyze(people, "CORPUS_30K_PINNED");
        var snapshots = V9SyntheticBaseFrequencyAudit.MaterializePhysicalEvidence(
            people, "CORPUS_30K_PINNED", FullBootstrap(), audit);
        Assert.That(snapshots, Has.Count.EqualTo(8));
        Assert.That(snapshots.Select(x => (x.Side, x.Unit)).Distinct().Count(),
            Is.EqualTo(8));
        foreach (var item in snapshots)
        {
            var aggregate = audit.Units.Single(x => x.Side == item.Side && x.Unit == item.Unit);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(item.CorpusBaseIdentities, Is.EqualTo(30_000));
                Assert.That(item.TrainBaseIdentities, Is.EqualTo(20_000));
                Assert.That(item.PresentTrainBaseIdentities,
                    Is.EqualTo(aggregate.PresentTrainingBasePeople));
                Assert.That(item.MissingTrainBaseIdentities,
                    Is.EqualTo(aggregate.MissingTrainingBasePeople));
                Assert.That(item.DistinctRetainedValues,
                    Is.EqualTo(aggregate.DistinctValues));
                Assert.That(item.SnapshotFingerprintSha256, Has.Length.EqualTo(64));
            }));
        }
        var names = System.Text.Json.JsonSerializer.Serialize(snapshots);
        Assert.That(names, Does.Not.Contain("MARIA"));
        Assert.That(names, Does.Not.Contain("JOANA"));
        Assert.That(names, Does.Not.Contain("BASE-"));

        var fraud = audit with { TrainingBasePeople = audit.TrainingBasePeople - 1 };
        Assert.That((Action)(() => V9SyntheticBaseFrequencyAudit.MaterializePhysicalEvidence(
            people, "CORPUS_30K_PINNED", FullBootstrap(), fraud)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Canonical_bootstrap_rejects_incomplete_split_wrong_hash_duplicate_and_fake_support()
    {
        var people = Full30kCorpus();
        void Reject(
            V9SyntheticBaseFrequencyAudit.BasePerson[] source,
            string frame, NominalSyntheticIbgeBootstrapV9 bootstrap,
            string side = "PERSON", int min = 1) =>
            Assert.That((Action)(() => V9SyntheticBaseFrequencyAudit.MaterializeCanonicalTrainUnit(
                source, frame, bootstrap, side,
                NominalFrequencyUnitV9.FirstName, min)),
                Throws.TypeOf<InvalidDataException>());

        Reject(people[..^1], "CORPUS_30K_PINNED", FullBootstrap());
        Reject(people, "UNMATCHED_FRAME", FullBootstrap());
        Reject(people, "CORPUS_30K_PINNED", FullBootstrap() with { People = 29_999 });
        Reject(people, "CORPUS_30K_PINNED", FullBootstrap() with { Provenance = "OBSERVED" });
        Reject(people, "CORPUS_30K_PINNED", FullBootstrap(), side: "OTHER");
        Reject(people, "CORPUS_30K_PINNED", FullBootstrap(), min: 20_001);
        var copied = people.ToArray();
        copied[29_999] = copied[0] with { Partition = "TEST" };
        Reject(copied, "CORPUS_30K_PINNED", FullBootstrap());
        copied = people.Select(x => x.Partition == "TEST"
            ? x with { Partition = "VALIDATION" } : x).ToArray();
        Reject(copied, "CORPUS_30K_PINNED", FullBootstrap());
    }

    [Test]
    public void Duplicate_base_or_wrong_split_fails_closed()
    {
        var original = Population();
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => V9SyntheticBaseFrequencyAudit.Analyze(
                original.Concat([original[0]]).ToArray(), "FRAME_42")),
                Throws.TypeOf<InvalidDataException>());
            Assert.That((Action)(() => V9SyntheticBaseFrequencyAudit.Analyze(
                [new("X", "OTHER", "Ana Lima", "Eva Costa")], "FRAME_42")),
                Throws.TypeOf<InvalidDataException>());
            Assert.That((Action)(() => V9SyntheticBaseFrequencyAudit.Analyze(
                [new("X", "TEST", "Ana Lima", "Eva Costa")], "FRAME_42")),
                Throws.TypeOf<InvalidDataException>());
        }));
    }
}

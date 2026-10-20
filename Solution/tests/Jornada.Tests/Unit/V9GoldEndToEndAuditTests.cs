using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldEndToEndAuditTests
{
    [Test]
    public void Counts_all_true_pairs_including_unpublished_sources_and_mixed_gold()
    {
        var truth = new Dictionary<long, string>
        {
            [1] = "A", [2] = "A", [3] = "A",
            [4] = "B", [5] = "B", [6] = "C"
        };
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var final = new Dictionary<long, Guid>
        {
            [1] = a, [2] = a, [4] = a, // A pair correct, two A/B false pairs
            [3] = b, [5] = b           // A/B false pair; C still pending
        };
        var report = V9GoldEndToEndAudit.Measure(truth, final);
        Assert.Multiple(() =>
        {
            Assert.That(report.Observations, Is.EqualTo(6));
            Assert.That(report.TrueIdentities, Is.EqualTo(3));
            Assert.That(report.MinimumLinksForPerfectConsolidation, Is.EqualTo(3));
            Assert.That(report.AllTrueObservationPairs, Is.EqualTo(4)); // 3 A + 1 B
            Assert.That(report.CorrectGoldObservationPairs, Is.EqualTo(1));
            Assert.That(report.FalseGoldObservationPairs, Is.EqualTo(3));
            Assert.That(report.FinalGoldPairRecall, Is.EqualTo(.25));
            Assert.That(report.FinalGoldPairPrecision, Is.EqualTo(.25));
            Assert.That(report.PublishedSources, Is.EqualTo(5));
            Assert.That(report.PendingSources, Is.EqualTo(1));
            Assert.That(report.PendingSourceRate, Is.EqualTo(1.0 / 6));
            Assert.That(report.GoldReferences, Is.EqualTo(2));
            Assert.That(report.TruthIdentitiesRepresented, Is.EqualTo(2));
            Assert.That(report.TruthIdentitiesUnrepresented, Is.EqualTo(1));
            Assert.That(report.TruthIdentityCoverage, Is.EqualTo(2.0 / 3));
            Assert.That(report.FragmentedTrueIdentities, Is.EqualTo(2));
            Assert.That(report.ImpureGoldReferences, Is.EqualTo(2));
        });
    }

    [Test]
    public void Perfect_three_observations_are_three_true_pairs_not_two()
    {
        var gold = Guid.NewGuid();
        var report = V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P", [2] = "P", [3] = "P" },
            new Dictionary<long, Guid> { [1] = gold, [2] = gold, [3] = gold });
        Assert.Multiple(() =>
        {
            Assert.That(report.MinimumLinksForPerfectConsolidation, Is.EqualTo(2));
            Assert.That(report.AllTrueObservationPairs, Is.EqualTo(3));
            Assert.That(report.CorrectGoldObservationPairs, Is.EqualTo(3));
            Assert.That(report.FinalGoldPairRecall, Is.EqualTo(1));
            Assert.That(report.FinalGoldPairPrecision, Is.EqualTo(1));
            Assert.That(report.FragmentedTrueIdentities, Is.Zero);
            Assert.That(report.ImpureGoldReferences, Is.Zero);
        });
    }

    [Test]
    public void Duplicate_references_are_not_covered_up_by_identity_coverage()
    {
        var report = V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P", [2] = "P" },
            new Dictionary<long, Guid> { [1] = Guid.NewGuid(), [2] = Guid.NewGuid() });
        Assert.Multiple(() =>
        {
            Assert.That(report.TruthIdentityCoverage, Is.EqualTo(1));
            Assert.That(report.GoldReferences, Is.EqualTo(2));
            Assert.That(report.FragmentedTrueIdentities, Is.EqualTo(1));
            Assert.That(report.FinalGoldPairRecall, Is.Zero);
            Assert.That(report.FinalGoldPairPrecision, Is.Null);
        });
    }

    [Test]
    public void Entirely_pending_truth_is_not_invisible_to_recall_denominator()
    {
        var report = V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P", [2] = "P" },
            new Dictionary<long, Guid>());
        Assert.Multiple(() =>
        {
            Assert.That(report.PendingSources, Is.EqualTo(2));
            Assert.That(report.TruthIdentitiesRepresented, Is.Zero);
            Assert.That(report.TruthIdentitiesUnrepresented, Is.EqualTo(1));
            Assert.That(report.AllTrueObservationPairs, Is.EqualTo(1));
            Assert.That(report.FinalGoldPairRecall, Is.Zero);
            Assert.That(report.FinalGoldPairPrecision, Is.Null);
        });
    }

    [Test]
    public void Singleton_only_corpus_does_not_invent_pairwise_recall()
    {
        var report = V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P", [2] = "Q" },
            new Dictionary<long, Guid> { [1] = Guid.NewGuid() });
        Assert.Multiple(() =>
        {
            Assert.That(report.AllTrueObservationPairs, Is.Zero);
            Assert.That(report.FinalGoldPairRecall, Is.Null);
            Assert.That(report.FinalGoldPairPrecision, Is.Null);
            Assert.That(report.PendingSourceRate, Is.EqualTo(.5));
        });
    }

    [Test]
    public void Refuses_Gold_assignment_outside_truth_and_missing_truth()
    {
        Assert.Throws<InvalidDataException>(() => V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P" },
            new Dictionary<long, Guid> { [2] = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "P" },
            new Dictionary<long, Guid> { [1] = Guid.Empty }));
        Assert.Throws<InvalidDataException>(() => V9GoldEndToEndAudit.Measure(
            new Dictionary<long, string> { [1] = "" },
            new Dictionary<long, Guid>()));
    }

    [TestCase("SEM_CANDIDATO_NO_BLOCO", "BUSCA_COMPLETA_SEM_CANDIDATO")]
    [TestCase("EVIDENCIA_INSUFICIENTE_PARA_BLOCKING", "EVIDENCIA_INSUFICIENTE")]
    [TestCase("TIMEOUT_SQL", "BUSCA_INCOMPLETA_OU_ERRO")]
    [TestCase("POSTERIOR_ABAIXO_LIMIAR", "CANDIDATO_NAO_APROVADO")]
    [TestCase("MARGEM_INSUFICIENTE", "AMBIGUIDADE_OU_CONFLITO")]
    [TestCase("PRECEDENCIA_PROTEGIDA", "PRECEDENCIA_DE_IDENTIDADE")]
    [TestCase(null, "SEM_MOTIVO")]
    [TestCase("nome da pessoa e outros dados sensiveis", "OUTRO_REQUER_AUDITORIA")]
    public void Reasons_are_bucketed_not_exported_as_free_text(string? source, string category)
    {
        Assert.That(V9GoldEndToEndAudit.ReasonCategory(source), Is.EqualTo(category));
    }
}

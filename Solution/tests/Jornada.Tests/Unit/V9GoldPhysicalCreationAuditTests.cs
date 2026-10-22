using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldPhysicalCreationAuditTests
{
    [Test]
    public void Reevaluation_does_not_erase_prior_events()
    {
        var audit = new V9GoldPhysicalCreationAudit();
        audit.Observe(101, true, true, "NOVA_IDENTIDADE");
        audit.Observe(102, false, true, "NOVA_IDENTIDADE");
        audit.Observe(103, true, true, "INDEFINIDA");
        audit.Observe(101, true, false, "ASSOCIACAO_EXISTENTE");
        audit.Observe(102, true, false, "ASSOCIACAO_EXISTENTE");
        audit.Observe(103, true, false, "ASSOCIACAO_EXISTENTE");
        var counts = audit.Snapshot();
        Assert.That(counts.LowerBandProposals, Is.EqualTo(3));
        Assert.That(counts.LowerBandCreated, Is.EqualTo(2));
        Assert.That(counts.FalseLowerBandCreations, Is.EqualTo(1));
        Assert.That(counts.FalseNewIdentityCreations, Is.EqualTo(1));
    }

    [Test]
    public void Repeated_attempts_of_same_source_count_once()
    {
        var audit = new V9GoldPhysicalCreationAudit();
        audit.Observe(12, true, true, "NOVA_IDENTIDADE");
        audit.Observe(12, true, true, "NOVA_IDENTIDADE");
        audit.Observe(12, true, false, "NOVA_IDENTIDADE");
        var counts = audit.Snapshot();
        Assert.That(counts.LowerBandProposals, Is.EqualTo(1));
        Assert.That(counts.LowerBandCreated, Is.EqualTo(1));
        Assert.That(counts.FalseLowerBandCreations, Is.EqualTo(1));
        Assert.That(counts.FalseNewIdentityCreations, Is.EqualTo(1));
    }

    [Test]
    public void Traditional_no_candidate_creation_stays_separate()
    {
        var audit = new V9GoldPhysicalCreationAudit();
        audit.Observe(1, true, false, "NOVA_IDENTIDADE");
        audit.Observe(2, false, false, "NOVA_IDENTIDADE");
        var counts = audit.Snapshot();
        Assert.That(counts.LowerBandProposals, Is.Zero);
        Assert.That(counts.LowerBandCreated, Is.Zero);
        Assert.That(counts.FalseLowerBandCreations, Is.Zero);
        Assert.That(counts.FalseNewIdentityCreations, Is.EqualTo(1));
    }

    [Test]
    public void Invalid_publications_fail_closed()
    {
        var audit = new V9GoldPhysicalCreationAudit();
        Assert.Throws<InvalidDataException>((Action)(() =>
            audit.Observe(1, false, true, "DESCONHECIDO")));
        Assert.That(audit.Snapshot().LowerBandCreated, Is.Zero);
    }
}

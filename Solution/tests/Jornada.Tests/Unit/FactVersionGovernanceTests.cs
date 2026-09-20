using Jornada.Contracts;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class FactVersionGovernanceTests
{
    [Test]
    public void Same_fact_identity_has_no_retification_conflict()
    {
        var uuid = Guid.NewGuid();
        var previous = Benefit(uuid, new DateOnly(2026, 1, 1), type: 10);
        var proposed = Benefit(uuid, new DateOnly(2026, 1, 1), type: 10);

        var result = FactVersionGovernance.Evaluate(previous, proposed);

        Assert.That(result.HasRetificationConflict, Is.False);
    }

    [Test]
    public void Different_canonical_person_is_conflict_but_delivery_id_is_not_an_input()
    {
        var previous = Benefit(Guid.NewGuid(), new DateOnly(2026, 1, 1), type: 10);
        var proposed = Benefit(Guid.NewGuid(), new DateOnly(2026, 1, 1), type: 10);

        var result = FactVersionGovernance.Evaluate(previous, proposed);

        Assert.Multiple(() =>
        {
            Assert.That(result.HasRetificationConflict, Is.True);
            Assert.That(result.ConflictReasons, Is.EquivalentTo(new[] { "PESSOA" }));
        });
    }

    [Test]
    public void Different_type_or_initial_marker_is_governed_conflict()
    {
        var uuid = Guid.NewGuid();
        var previous = Benefit(uuid, new DateOnly(2026, 1, 1), type: 10);
        var proposed = Benefit(uuid, new DateOnly(2026, 2, 1), type: 11);

        var result = FactVersionGovernance.Evaluate(previous, proposed);

        Assert.Multiple(() =>
        {
            Assert.That(result.ConflictReasons, Does.Contain("NATUREZA_TIPO"));
            Assert.That(result.ConflictReasons, Does.Contain("MARCO_INICIAL"));
            Assert.That(result.CanonicalReason, Is.EqualTo("RN_CT_12:MARCO_INICIAL,NATUREZA_TIPO"));
        });
    }

    [Test]
    public void Service_timestamp_is_the_initial_marker()
    {
        var uuid = Guid.NewGuid();
        var previous = new FactVersionGovernanceSnapshot(
            IntegrationNature.SERVICO, 20, uuid, 1, null,
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(-3)));
        var proposed = previous with
        {
            DataHoraServico = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(-3))
        };

        Assert.That(
            FactVersionGovernance.Evaluate(previous, proposed).ConflictReasons,
            Is.EquivalentTo(new[] { "MARCO_INICIAL" }));
    }

    [Test]
    public void Missing_persistent_identity_does_not_invent_person_conflict()
    {
        var previous = Benefit(null, new DateOnly(2026, 1, 1), type: 10);
        var proposed = Benefit(Guid.NewGuid(), new DateOnly(2026, 1, 1), type: 10);

        Assert.That(FactVersionGovernance.Evaluate(previous, proposed).HasRetificationConflict, Is.False);
    }

    private static FactVersionGovernanceSnapshot Benefit(Guid? uuid, DateOnly start, long type) =>
        new(IntegrationNature.BENEFICIO, type, uuid, null, start, null);
}

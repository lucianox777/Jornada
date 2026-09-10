using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class BlockingIndexPlanTests
{
    [Test]
    public void Create_ReusesExistingIndexWhenRequirementMatchesPrefix()
    {
        var plan = BlockingIndexPlan.Create(
            "rules-1",
            new[]
            {
                new BlockingIndexRequirement("birth", new[] { "data_nascimento" })
            },
            new[]
            {
                new BlockingPhysicalIndex(
                    "IX_gold_pessoa_linkage_nascimento",
                    new[] { "data_nascimento", "pessoa_uuid" })
            });

        Assert.That(plan.ProposedIndexes, Is.Empty);
        Assert.That(plan.Assignments.Single().IndexName, Is.EqualTo("IX_gold_pessoa_linkage_nascimento"));
        Assert.That(plan.Assignments.Single().RequiresCreation, Is.False);
    }

    [Test]
    public void Create_ProposesStableIndexForMissingCombination()
    {
        var first = BlockingIndexPlan.Create(
            "rules-1",
            new[]
            {
                new BlockingIndexRequirement("name-year", new[] { "nome_primeiro", "nascimento_ano" })
            });

        var second = BlockingIndexPlan.Create(
            "rules-2",
            new[]
            {
                new BlockingIndexRequirement("name-year", new[] { "nome_primeiro", "nascimento_ano" })
            });

        Assert.That(first.ProposedIndexes, Has.Count.EqualTo(1));
        Assert.That(second.ProposedIndexes, Has.Count.EqualTo(1));
        Assert.That(first.ProposedIndexes.Single().Name, Is.EqualTo(second.ProposedIndexes.Single().Name));
        Assert.That(first.Assignments.Single().RequiresCreation, Is.True);
    }

    [Test]
    public void Create_DeduplicatesEquivalentRequirementsAcrossPasses()
    {
        var plan = BlockingIndexPlan.Create(
            "rules-1",
            new[]
            {
                new BlockingIndexRequirement("pass-a", new[] { "nome_ultimo", "nascimento_ano" }),
                new BlockingIndexRequirement("pass-b", new[] { "nome_ultimo", "nascimento_ano" })
            });

        Assert.That(plan.ProposedIndexes, Has.Count.EqualTo(1));
        Assert.That(plan.Assignments, Has.Count.EqualTo(2));
        Assert.That(plan.Assignments[0].IndexName, Is.EqualTo(plan.Assignments[1].IndexName));
    }

    [Test]
    public void Create_RejectsIndexExplosionBeyondConfiguredLimit()
    {
        Assert.Throws<InvalidOperationException>(() => BlockingIndexPlan.Create(
            "rules-1",
            new[]
            {
                new BlockingIndexRequirement("pass-a", new[] { "nome_primeiro" }),
                new BlockingIndexRequirement("pass-b", new[] { "nome_ultimo" })
            },
            maxProposedIndexes: 1));
    }
}

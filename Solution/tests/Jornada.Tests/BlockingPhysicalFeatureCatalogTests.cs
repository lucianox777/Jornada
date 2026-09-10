using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingPhysicalFeatureCatalogTests
{
    [Test]
    public void RequiredOptimizerFeatures_CoversEveryRequiredLogicalCandidate()
    {
        var physical = BlockingPhysicalFeatureCatalog.RequiredOptimizerFeatures
            .Select(static x => x.Feature)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var logical = BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.That(physical, Is.EqualTo(logical));
    }

    [Test]
    public void FullName_UsesExistingGoldColumnDirectly()
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(
            BlockingCandidateFeatureCatalog.FullName, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.SourceColumn, Is.EqualTo("nome_completo"));
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.DirectColumn));
        });
    }

    [Test]
    public void DerivedNameAndBirthComponents_UseMaterializedProjection()
    {
        var derived = new[]
        {
            BlockingCandidateFeatureCatalog.FirstName,
            BlockingCandidateFeatureCatalog.Surnames,
            BlockingCandidateFeatureCatalog.LastName,
            BlockingCandidateFeatureCatalog.BirthDay,
            BlockingCandidateFeatureCatalog.BirthMonth,
            BlockingCandidateFeatureCatalog.BirthYear
        };

        foreach (var feature in derived)
        {
            Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection), feature);
        }
    }

    [Test]
    public void Surnames_IsExplicitlyMultiValued()
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(
            BlockingCandidateFeatureCatalog.Surnames, out var mapping), Is.True);
        Assert.That(mapping.MultiValued, Is.True);
    }
}

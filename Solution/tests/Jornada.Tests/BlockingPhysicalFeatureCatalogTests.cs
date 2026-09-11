using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingPhysicalFeatureCatalogTests
{
    [Test]
    public void CalibratorFeatures_CoversEveryRequiredLogicalCandidate()
    {
        var physical = BlockingPhysicalFeatureCatalog.RequiredCalibratorFeatures
            .Select(static x => x.Feature)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var logical = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.That(physical, Is.EqualTo(logical));
    }

    [TestCase(BlockingCandidateFeatureCatalog.FullName)]
    [TestCase(BlockingCandidateFeatureCatalog.FullNameUpper)]
    [TestCase(BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics)]
    [TestCase(BlockingCandidateFeatureCatalog.FullNameWithoutParticles)]
    [TestCase(BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr)]
    [TestCase(BlockingCandidateFeatureCatalog.FirstName)]
    [TestCase(BlockingCandidateFeatureCatalog.Surnames)]
    [TestCase(BlockingCandidateFeatureCatalog.LastName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullNameUpper)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullNameUpperNoDiacritics)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullNameWithoutParticles)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFullNamePhoneticPtBr)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherFirstName)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherSurnames)]
    [TestCase(BlockingCandidateFeatureCatalog.MotherLastName)]
    public void NameFeatures_UseMaterializedSilverHistory(string feature)
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection));
            Assert.That(mapping.SourceScope, Is.EqualTo(BlockingPhysicalSourceScope.SilverObservationHistory));
        });
    }

    [TestCase(BlockingCandidateFeatureCatalog.BirthDay)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthMonth)]
    [TestCase(BlockingCandidateFeatureCatalog.BirthYear)]
    public void BirthComponents_UseMaterializedCurrentGoldValue(string feature)
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection));
            Assert.That(mapping.SourceScope, Is.EqualTo(BlockingPhysicalSourceScope.GoldCurrent));
        });
    }

    [Test]
    public void Surnames_IsExplicitlyMultiValued()
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(
            BlockingCandidateFeatureCatalog.Surnames, out var mapping), Is.True);
        Assert.That(mapping.MultiValued, Is.True);
    }

    [TestCase("telefone_contato__canonical", "telefone_contato", true)]
    [TestCase("email_contato__canonical", "email_contato", true)]
    [TestCase("nome_social__normalized", "nome_social", false)]
    [TestCase("nome_social__surnames", "nome_social", true)]
    public void DynamicTransversalFeatures_AreDerivedFromSemanticProjection(
        string feature,
        string sourceAttribute,
        bool multiValued)
    {
        Assert.That(BlockingPhysicalFeatureCatalog.TryGet(feature, out var mapping), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(mapping.SourceColumn, Is.EqualTo($"pessoa_atributo[{sourceAttribute}].valor"));
            Assert.That(mapping.Strategy, Is.EqualTo(BlockingPhysicalStrategy.MaterializedProjection));
            Assert.That(mapping.SourceScope, Is.EqualTo(BlockingPhysicalSourceScope.SilverObservationHistory));
            Assert.That(mapping.MultiValued, Is.EqualTo(multiValued));
        });
    }

    [Test]
    public void ConfidentialShelterAddress_HasNoPhysicalBlockingFeature()
    {
        Assert.That(BlockingCandidateFeatureCatalog.CalibratorCandidates
            .Any(static feature => feature.Contains("endereco_casa_abrigo_sigilosa", StringComparison.Ordinal)), Is.False);
    }
}
